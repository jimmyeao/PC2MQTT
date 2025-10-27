using MQTTnet;
using MQTTnet.Protocol;
using System;
using System.Buffers;
using Serilog;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using Newtonsoft.Json;
using PC2MQTT.api;

namespace PC2MQTT
{
    public class MqttService
    {
        #region Private Fields

        private static readonly object _lock = new object();
        private static MqttService _instance;
        private readonly string _clientId;
        private readonly AppSettings _settings;
        private bool _isInitialized = false;
        private Queue<MqttApplicationMessage> _messageQueue = new Queue<MqttApplicationMessage>();
        private MqttClient _mqttClient;
        private pc_sensors _pc_sensors;
        private System.Threading.Timer _reconnectTimer;
        private bool _shouldReconnect = true;
        #endregion Private Fields

        #region Private Constructors

        private MqttService(AppSettings settings)
        {
            //InitializeMqttClient().GetAwaiter().GetResult();
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            // Note: pc_sensors will be initialized after MQTT service is ready
            _pc_sensors = null;

            Log.Information("MQTT client initialized.");
        }

        #endregion Private Constructors

        #region Public Events

        public event Action<string> ConnectionStatusChanged;

        public event Func<MqttApplicationMessageReceivedEventArgs, Task> MessageReceived;

        public event Action<string> StatusMessageReceived;

        #endregion Public Events

        #region Public Properties

        public static MqttService Instance
        {
            get
            {
                if (_instance == null)
                {
                    throw new InvalidOperationException("MqttService must be initialized before use. Call InitializeInstance first.");
                }
                return _instance;
            }
        }


        public string CurrentStatus { get; private set; } = "Initializing...";
        public bool IsConnected { get; private set; } = false;

        #endregion Public Properties

        #region Public Methods

        public async Task DisconnectAsync()
        {
            if (_mqttClient != null)
            {
                try
                {
                    _shouldReconnect = false; // Prevent auto-reconnection
                    await _mqttClient.DisconnectAsync();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error stopping MQTT client.");
                }
                finally
                {
                    ConnectionStatusChanged?.Invoke($"MQTT Status: Disconnected");
                }
            }
        }

        public async Task InitializeAsync(AppSettings settings, string clientId)
        {
            if (!_isInitialized)
            {
                // Perform your initialization here using settings and clientId. For example,
                // setting up the MQTT client.

                CurrentStatus = "Initializing...";
                ConnectionStatusChanged?.Invoke($"MQTT Status: Initializing");
                await InitializeMqttClient(settings, clientId);

                // Initialize pc_sensors with MqttService reference
                var deviceId = string.IsNullOrEmpty(settings.SensorPrefix) ? System.Environment.MachineName : settings.SensorPrefix;
                _pc_sensors = new pc_sensors(settings, this, deviceId);

                _isInitialized = true;
            }
        }

        public async Task PublishAsync(string topic, string payload, MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtLeastOnce)
        {
            if (_mqttClient == null || !IsConnected)
            {
                // Client not initialized or not connected, queue the message
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(qos)
                    .Build();
                _messageQueue.Enqueue(message);
            }
            else
            {
                // Client initialized and connected, publish immediately
                // First publish any queued messages
                while (_messageQueue.Any())
                {
                    var queuedMessage = _messageQueue.Dequeue();
                    await _mqttClient.PublishAsync(queuedMessage);
                }

                // Publish the current message
                var currentMessage = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(qos)
                    .Build();
                await _mqttClient.PublishAsync(currentMessage);
            }
        }
        public static MqttService InitializeInstance(AppSettings settings)
        {
            lock (_lock)
            {
                if (_instance == null)
                {
                    _instance = new MqttService(settings);
                }
                // Consider else block to update settings if _instance already exists
            }
            return _instance;
        }

        public async Task SubscribeToSwitchCommandsAsync()
        {
            var deviceId = _settings.SensorPrefix;
            List<string> switchNames = new List<string> { "shutdown", "reboot", "standby", "hibernate" };

            foreach (var switchName in switchNames)
            {
                var topic = $"homeassistant/switch/{deviceId}/{switchName}/set";
                await SubscribeAsync(topic);
            }
        }
        private async Task HandleReceivedMessageAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload.ToArray());
            var topicParts = topic.Split('/');
            if (topicParts.Length < 2)
            {
                Log.Warning($"Invalid topic format: {topic}");
                return;
            }

            var command = topicParts[^2].ToLower(); // Using ^2 to get the second to last element
            Log.Information($"Received control command: {command}, payload: {payload}");

            // Example: Handling a shutdown command
         

            // Handle different actions
            switch (command.ToLower())
            {
                case "shutdown":
                    // Call your shutdown method here
                    
                    _pc_sensors.PerformShutdown();
                    break;
                case "reboot":
                    // Call your reboot method here
                    _pc_sensors.PerformReboot();
                    break;
                case "standby":
                    // Call your standby method here
                    _pc_sensors.PerformStandby();
                    break;
                case "hibernate":
                    // Call your hibernate method here
                    _pc_sensors.PerformHibernate();
                    break;
                // Add more cases as needed
                default:
                    Log.Warning($"Unknown control command: {command}");
                    break;
            }

            // Add similar handling for other commands
        }

        public async Task ReinitializeAsync(AppSettings settings, string clientId)
        {
            await DisconnectAsync();

            // Reset initialization flag to allow reinitialization
            _isInitialized = false;

            CurrentStatus = "Reconnecting...";
            ConnectionStatusChanged?.Invoke($"MQTT Status: Reconnecting");
            await InitializeAsync(settings, clientId);
        }

        public async Task SubscribeAsync(string topic, MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtLeastOnce)
        {
            if (_mqttClient == null)
            {
                Console.WriteLine("MQTT client is not initialized.");
                return;
            }

            var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(f => f.WithTopic(topic).WithQualityOfServiceLevel(qos))
                .Build();
            await _mqttClient.SubscribeAsync(subscribeOptions);
        }


        public async Task UnsubscribeAsync(params string[] topics)
        {
            if (_mqttClient != null && topics != null)
            {
                var builder = new MqttClientUnsubscribeOptionsBuilder();
                foreach (var topic in topics)
                {
                    builder.WithTopicFilter(topic);
                }
                var unsubscribeOptions = builder.Build();
                await _mqttClient.UnsubscribeAsync(unsubscribeOptions);
            }
        }
        

        #endregion Public Methods

        #region Private Methods

        private async Task InitializeMqttClient(AppSettings settings, string clientId)
        {
            if (_isInitialized) return;

            _isInitialized = true;
            var mqttFactory = new MqttClientFactory();
            _mqttClient = mqttFactory.CreateMqttClient() as MqttClient;

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
                builder.WithWebSocketServer(o => o.WithUri(webSocketUri));
            }
            else
            {
                builder.WithTcpServer(settings.MqttAddress, port);
            }

            // Configure TLS if needed
            if (settings.UseTLS)
            {
                builder.WithTlsOptions(o =>
                {
                    o.WithAllowUntrustedCertificates(settings.IgnoreCertificateErrors);
                    o.WithIgnoreCertificateChainErrors(settings.IgnoreCertificateErrors);
                    o.WithIgnoreCertificateRevocationErrors(settings.IgnoreCertificateErrors);

                    // If you need to validate the server certificate, you can set the CertificateValidationHandler.
                    // Note: Be cautious with bypassing certificate checks in production code!!
                    if (!settings.IgnoreCertificateErrors)
                    {
                        o.WithCertificateValidationHandler(context =>
                        {
                            // Log the SSL policy errors
                            Log.Debug($"SSL policy errors: {context.SslPolicyErrors}");

                            // Return true if there are no SSL policy errors
                            return context.SslPolicyErrors == System.Net.Security.SslPolicyErrors.None;
                        });
                    }
                });
            }
            var options = builder.Build();

            // Setup event handlers
            _mqttClient.ApplicationMessageReceivedAsync += async e =>
            {
                if (MessageReceived != null)
                {
                    await MessageReceived.Invoke(e);
                    Log.Information($"Message received on topic {e.ApplicationMessage.Topic}: {Encoding.UTF8.GetString(e.ApplicationMessage.Payload.ToArray())}");
                }
            };

            _mqttClient.ConnectedAsync += async e =>
            {
                IsConnected = true;
                CurrentStatus = "Connected";
                Log.Information("Connected to MQTT broker.");
                await SubscribeToSwitchCommandsAsync();
                ConnectionStatusChanged?.Invoke("MQTT Status: Connected");

                // Publish queued messages
                while (_messageQueue.Any())
                {
                    var message = _messageQueue.Dequeue();
                    await _mqttClient.PublishAsync(message);
                }
            };

            _mqttClient.DisconnectedAsync += async e =>
            {
                IsConnected = false;
                CurrentStatus = "Disconnected";
                Log.Information("Disconnected from MQTT broker.");
                ConnectionStatusChanged?.Invoke("MQTT Status: Disconnected");

                // Implement auto-reconnect
                if (_shouldReconnect && !e.ClientWasConnected)
                {
                    CurrentStatus = "Reconnecting...";
                    ConnectionStatusChanged?.Invoke("MQTT Status: Reconnecting...");
                    Log.Information("Attempting to reconnect in 5 seconds...");
                    await Task.Delay(5000);
                    try
                    {
                        await _mqttClient.ConnectAsync(options);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Reconnection failed");
                        CurrentStatus = "Connection Failed";
                        ConnectionStatusChanged?.Invoke("MQTT Status: Connection Failed");
                    }
                }
            };

            _mqttClient.ApplicationMessageReceivedAsync += HandleReceivedMessageAsync;

            // Connect to MQTT broker
            await _mqttClient.ConnectAsync(options);
            Log.Information("MQTT client started.");

        }

        #endregion Private Methods
    }
}