using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using System;
using Serilog;
using System.Text;
using System.Threading.Tasks;

namespace PC2MQTT
{
    public class MqttService
    {
        private IManagedMqttClient _mqttClient;
        private readonly string _clientId;
        private readonly AppSettings _settings;
        public bool IsConnected { get; private set; } = false;
        public event Func<MqttApplicationMessageReceivedEventArgs, Task> MessageReceived;

        public MqttService(AppSettings settings, string clientId)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
          
            InitializeMqttClient().GetAwaiter().GetResult();
            Log.Information("MQTT client initialized.");

        }
        public async Task DisconnectAsync()
        {
            if (_mqttClient != null)
            {
                await _mqttClient.StopAsync();
                _mqttClient.Dispose(); // Optionally dispose the client if you're not planning to reconnect
            }
        }
        public async Task UnsubscribeAsync(params string[] topics)
        {
            if (_mqttClient != null && topics != null)
            {
                await _mqttClient.UnsubscribeAsync(topics);
            }
        }

        private async Task InitializeMqttClient()
        {
            var mqttFactory = new MqttFactory();
            _mqttClient = mqttFactory.CreateManagedMqttClient();

            var builder = new MqttClientOptionsBuilder()
                .WithClientId(_clientId)
                .WithCredentials(_settings.MqttUsername, _settings.MqttPassword)
                .WithCleanSession();

            // Determine the port to use
            if (!int.TryParse(_settings.MqttPort, out int port))
            {
                port = 1883; // Default MQTT port
            }

            // Configure WebSocket or TCP connection
            if (_settings.UseWebsockets)
            {
                var webSocketUri = _settings.UseTLS ? $"wss://{_settings.MqttAddress}:{port}" : $"ws://{_settings.MqttAddress}:{port}";
                builder.WithWebSocketServer(webSocketUri);
            }
            else
            {
                builder.WithTcpServer(_settings.MqttAddress, port);
            }

            // Configure TLS if needed
            if (_settings.UseTLS)
            {
                builder.WithTls(tlsParams =>
                {
                    tlsParams.AllowUntrustedCertificates = _settings.IgnoreCertificateErrors;
                    tlsParams.IgnoreCertificateChainErrors = _settings.IgnoreCertificateErrors;
                    tlsParams.IgnoreCertificateRevocationErrors = _settings.IgnoreCertificateErrors;
                    tlsParams.UseTls = true; // Set to true to enable TLS, no method group usage

                    if (!_settings.IgnoreCertificateErrors)
                    {
                        tlsParams.CertificateValidationHandler = context =>
                        {
                            // Log and/or handle certificate validation here
                            return context.SslPolicyErrors == System.Net.Security.SslPolicyErrors.None;
                        };
                    }
                });
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
                }
            };

            _mqttClient.ConnectedAsync += async e =>
            {
                IsConnected = true;
                Log.Information("Connected to MQTT broker.");
                // Additional actions upon connection
            };

            _mqttClient.DisconnectedAsync += async e =>
            {
                IsConnected = false;
                Log.Information("Disconnected from MQTT broker.");
                // Additional actions upon disconnection
            };

            await _mqttClient.StartAsync(managedOptions);
            Log.Information("MQTT client started.");
        }


        public async Task PublishAsync(string topic, string payload, MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtLeastOnce)
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(qos)
                .Build();

            // Use EnqueueAsync for managed clients
            await _mqttClient.EnqueueAsync(message);
        }

        public async Task SubscribeAsync(string topic, MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtLeastOnce)
        {
            var topicFilter = new MqttTopicFilterBuilder().WithTopic(topic).WithQualityOfServiceLevel(qos).Build();
            await _mqttClient.SubscribeAsync(new MqttTopicFilter[] { topicFilter });
        }


    }
}
