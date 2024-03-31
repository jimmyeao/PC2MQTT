using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using System;
using Serilog;
using System.Text;
using System.Threading.Tasks;
using System.Data;

namespace PC2MQTT
{
    public class MqttService
    {
        private IManagedMqttClient _mqttClient;
        private readonly string _clientId;
        private readonly AppSettings _settings;
        private bool _isInitialized = false;
        private static MqttService _instance;
        private static readonly object _lock = new object();
        private Queue<MqttApplicationMessage> _messageQueue = new Queue<MqttApplicationMessage>();
       
        public bool IsConnected { get; private set; } = false;
        public event Func<MqttApplicationMessageReceivedEventArgs, Task> MessageReceived;
        public event Action<string> StatusMessageReceived;
        public event Action<string> ConnectionStatusChanged;
        public string CurrentStatus { get; private set; } = "Initializing...";
        private MqttService()
        {
            //InitializeMqttClient().GetAwaiter().GetResult();
            Log.Information("MQTT client initialized.");

        }


        private async Task InitializeMqttClient(AppSettings settings, string clientId)
        {
            if (_isInitialized) return;

            _isInitialized = true;
            var mqttFactory = new MqttFactory();
            _mqttClient = mqttFactory.CreateManagedMqttClient();

            var builder = new MqttClientOptionsBuilder()
                .WithClientId(clientId)
                .WithCredentials(settings.MqttUsername, settings.MqttPassword)
                .WithCleanSession();

            // Determine the port to use
            if (!int.TryParse(settings.MqttPort, out int port))
            {
                port = 1883; // Default MQTT port
            }

            // Configure WebSocket or TCP connection
            if (settings.UseWebsockets)
            {
                var webSocketUri = settings.UseTLS ? $"wss://{settings.MqttAddress}:{port}" : $"ws://{settings.MqttAddress}:{port}";
                builder.WithWebSocketServer(webSocketUri);
            }
            else
            {
                builder.WithTcpServer(settings.MqttAddress, port);
            }

            // Configure TLS if needed
            if (settings.UseTLS)
            {
                // Create TLS parameters
                var tlsParameters = new MqttClientOptionsBuilderTlsParameters
                {
                    AllowUntrustedCertificates = settings.IgnoreCertificateErrors,
                    IgnoreCertificateChainErrors = settings.IgnoreCertificateErrors,
                    IgnoreCertificateRevocationErrors = settings.IgnoreCertificateErrors,
                    UseTls = true
                };

                // If you need to validate the server certificate, you can set the CertificateValidationHandler.
                // Note: Be cautious with bypassing certificate checks in production code!!
                if (!settings.IgnoreCertificateErrors)
                {
                    tlsParameters.CertificateValidationHandler = context =>
                    {
                        // Log the SSL policy errors
                        Log.Debug($"SSL policy errors: {context.SslPolicyErrors}");
                      
                        // Return true if there are no SSL policy errors, or if ignoring
                        // certificate errors is allowed
                        return context.SslPolicyErrors == System.Net.Security.SslPolicyErrors.None;
                    };
                }

                builder.WithTls(tlsParameters);
            }
                var options = builder.Build();
                var managedOptions = new ManagedMqttClientOptionsBuilder()
                .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
                .WithClientOptions(options)
                .Build();

            _mqttClient.ApplicationMessageReceivedAsync += async e =>
            {
                if (MessageReceived != null)
                {
                    await MessageReceived.Invoke(e);
                    Log.Information($"Message received on topic {e.ApplicationMessage.Topic}: {Encoding.UTF8.GetString(e.ApplicationMessage.Payload)}");
                }
            };
            
            _mqttClient.ConnectedAsync += async e =>
            {
                IsConnected = true;
                Log.Information("Connected to MQTT broker.");
              
                ConnectionStatusChanged?.Invoke("MQTT Status: Connected");
                // Additional actions upon connection
            };

            _mqttClient.DisconnectedAsync += async e =>
            {
                IsConnected = false;
                Log.Information("Disconnected from MQTT broker.");
              
                ConnectionStatusChanged?.Invoke("MQTT Status: Disconnected");
                // Additional actions upon disconnection
            };
            _mqttClient.ConnectingFailedAsync += async e =>
            {
                var errorMessage = $"Failed to connect: {e.Exception.Message}";
                Log.Information(errorMessage);
  
                ConnectionStatusChanged?.Invoke(errorMessage);
            };

            await _mqttClient.StartAsync(managedOptions);
            while (_messageQueue.Any())
            {
                var message = _messageQueue.Dequeue();
                await _mqttClient.EnqueueAsync(message); // Make sure this is the correct method to call for publishing
            }
            Log.Information("MQTT client started.");
        }
        public async Task InitializeAsync(AppSettings settings, string clientId)
        {
            if (!_isInitialized)
            {
                // Perform your initialization here using settings and clientId.
                // For example, setting up the MQTT client.
               
                ConnectionStatusChanged?.Invoke($"MQTT Status: Initializing");
                await InitializeMqttClient(settings, clientId);
                _isInitialized = true;
            }
        }
        public async Task ReinitializeAsync(AppSettings settings, string clientId)
        {
            await DisconnectAsync();

            // Clear current instance and reinitialize
            lock (_lock)
            {
                _instance = null;
            }
            ConnectionStatusChanged?.Invoke($"MQTT Status: Reconnecting");
            await Instance.InitializeAsync(settings, clientId);
        
        }

       
        public static MqttService Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new MqttService();
                    }
                    return _instance;
                }
            }
        }
        //public void Initialize(AppSettings settings, string clientId)
        //{
        //    if (!_isInitialized)
        //    {
        //        // Assign settings and clientId to your private fields here
        //        // Then call InitializeMqttClient()
        //        _isInitialized = true;
        //        InitializeMqttClient(settings, clientId).GetAwaiter().GetResult();
        //    }
        //}
        public async Task DisconnectAsync()
        {
            if (_mqttClient != null)
            {
                try
                {
                    await _mqttClient.StopAsync();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error stopping MQTT client.");
                }
                finally
                {
                    //_mqttClient.Dispose();
                    //_mqttClient = null;
                    ConnectionStatusChanged?.Invoke($"MQTT Status: Disconnected");
                }

            }
        }
        public async Task UnsubscribeAsync(params string[] topics)
        {
            if (_mqttClient != null && topics != null)
            {
                await _mqttClient.UnsubscribeAsync(topics);
            }
        }
        public async Task PublishAsync(string topic, string payload, MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtLeastOnce)
        {
            if (_mqttClient == null)
            {
                // Client not initialized, queue the message
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(qos)
                    .Build();
                _messageQueue.Enqueue(message);
            }
            else
            {
                // Client initialized, publish immediately or dequeue the queued messages
                foreach (var message in _messageQueue)
                {
                    await _mqttClient.EnqueueAsync(message);
                }
                _messageQueue.Clear(); // Clear the queue after publishing

                // Publish the current message
                var currentMessage = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(qos)
                    .Build();
                await _mqttClient.EnqueueAsync(currentMessage);
            }
        }

       
        public async Task SubscribeAsync(string topic, MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtLeastOnce)
        {
            var topicFilter = new MqttTopicFilterBuilder().WithTopic(topic).WithQualityOfServiceLevel(qos).Build();
            await _mqttClient.SubscribeAsync(new MqttTopicFilter[] { topicFilter });
        }


    }
}
