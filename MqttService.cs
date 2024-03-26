using MQTTnet.Client;
using MQTTnet.Protocol;
using MQTTnet;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace PC2MQTT
{
    public class MqttService
    {
        #region Private Fields

        private const int MaxConnectionRetries = 2;
        private const int RetryDelayMilliseconds = 1000;
        private string _deviceId;
        private bool _isAttemptingConnection = false;
        private MqttClient _mqttClient;
        private bool _mqttClientsubscribed = false;
        private MqttClientOptions _mqttOptions;
        private PCMetrics _pcMetrics;
        private Dictionary<string, string> _previousSensorStates;
        private List<string> _sensorNames;
        private AppSettings _settings;
        private HashSet<string> _subscribedTopics = new HashSet<string>();
        private System.Timers.Timer mqttPublishTimer;
        private bool mqttPublishTimerset = false;

    #endregion Private Fields

    #region Public Constructors

    public MqttService(AppSettings settings, string deviceId, List<string> sensorNames)
        {
            _pcMetrics = PCMetrics.Instance;

            _settings = settings;
            _deviceId = deviceId;
            _sensorNames = sensorNames;
            _previousSensorStates = new Dictionary<string, string>();

            InitializeClient();
            InitializeMqttPublishTimer();
        }

        #endregion Public Constructors

        #region Public Delegates

        public delegate Task CommandToTeamsHandler(string jsonMessage);

        #endregion Public Delegates

        #region Public Events

        public event CommandToTeamsHandler CommandToTeams;

        public event Action<string> ConnectionAttempting;

        public event Action<string> ConnectionStatusChanged;

        public event Func<MqttApplicationMessageReceivedEventArgs, Task> MessageReceived;

        public event Action<string> StatusUpdated;

        #endregion Public Events

        #region Public Properties

        public bool IsAttemptingConnection
        {
            get { return _isAttemptingConnection; }
            private set { _isAttemptingConnection = value; }
        }

        public bool IsConnected => _mqttClient.IsConnected;

        #endregion Public Properties

        #region Public Methods
        public async Task SetupMqttSensors()
        {
            // Create a dummy MeetingUpdate with default values
           

            // Call PublishConfigurations with the dummy MeetingUpdate
             // await PublishConfigurations(dummyMeetingUpdate, _settings);
        }
        public async Task ConnectAsync()
        {
            // Check if MQTT client is already connected or connection attempt is in progress
            if (_mqttClient.IsConnected || _isAttemptingConnection)
            {
                Log.Information("MQTT client is already connected or connection attempt is in progress.");
                return;
            }

            _isAttemptingConnection = true;
            ConnectionAttempting?.Invoke("MQTT Status: Connecting...");
            int retryCount = 0;

            // Retry connecting to MQTT broker up to a maximum number of times
            while (retryCount < MaxConnectionRetries && !_mqttClient.IsConnected)
            {
                try
                {
                    Log.Information($"Attempting to connect to MQTT (Attempt {retryCount + 1}/{MaxConnectionRetries})");
                    await _mqttClient.ConnectAsync(_mqttOptions);
                    Log.Information("Connected to MQTT broker.");
                    if (_mqttClient.IsConnected)
                    {
                        ConnectionStatusChanged?.Invoke("MQTT Status: Connected");
                        PublishConfigurations();
                        break; // Exit the loop if successfully connected
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug($"Failed to connect to MQTT broker: {ex.Message}");
                    ConnectionStatusChanged?.Invoke($"MQTT Status: Disconnected (Retry {retryCount + 1}) {ex.Message}");
                    retryCount++;
                    await Task.Delay(RetryDelayMilliseconds); // Delay before retrying
                }
            }

            _isAttemptingConnection = false;
            // Notify if failed to connect after all retry attempts
            if (!_mqttClient.IsConnected)
            {
                ConnectionStatusChanged?.Invoke("MQTT Status: Disconnected (Failed to connect)");
                Log.Error("Failed to connect to MQTT broker after several attempts.");
            }
        }

        public async Task DisconnectAsync()
        {
            if (!_mqttClient.IsConnected)
            {
                Log.Debug("MQTTClient is not connected");
                ConnectionStatusChanged?.Invoke("MQTTClient is not connected");
                return;
            }

            try
            {
                await _mqttClient.DisconnectAsync();
                Log.Information("MQTT Disconnected");
                ConnectionStatusChanged?.Invoke("MQTTClient is not connected");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to disconnect from MQTT broker: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_mqttClient != null)
            {
                _ = _mqttClient.DisconnectAsync(); // Disconnect asynchronously
                _mqttClient.Dispose();
                Log.Information("MQTT Client Disposed");
            }
        }

        public double GetCpuUsage()
        {
            PerformanceCounter cpuCounter = new PerformanceCounter();
            cpuCounter.CategoryName = "Processor";
            cpuCounter.CounterName = "% Processor Time";
            cpuCounter.InstanceName = "_Total";

            // Initial call for consistency with subsequent readings
            cpuCounter.NextValue();
            System.Threading.Thread.Sleep(1000); // Wait a second to get a proper reading
            return cpuCounter.NextValue(); // Return the second reading
        }


        public List<DiskMetric> GetDiskMetrics()
        {
            List<DiskMetric> diskMetrics = new List<DiskMetric>();
            // Logic to fill diskMetrics with data for each disk
            return diskMetrics;
        }

        public double GetMemoryUsage()
        {
            // Dummy implementation - replace with actual logic
            return 0.0; // Return CPU usage percentage
        }

        public void InitializeMqttPublishTimer()
        {
            mqttPublishTimer = new System.Timers.Timer(1000); // Adjust the interval as needed.
            if (!mqttPublishTimerset)
            {
                mqttPublishTimer.Elapsed += OnMqttPublishTimerElapsed;
                mqttPublishTimer.AutoReset = true; // The timer will fire every 60 seconds
                mqttPublishTimer.Enabled = true; // Start the timer
                mqttPublishTimerset = true;
                Log.Information("MQTT Publish Timer initialized and started.");
            }
        }


        public async Task PublishAsync(MqttApplicationMessage message)
        {
            try
            {
                await _mqttClient.PublishAsync(message, CancellationToken.None); // Note: Add using System.Threading; if CancellationToken is undefined
                Log.Information("Publish successful." + message.Topic);
            }
            catch (Exception ex)
            {
                Log.Information($"Error during MQTT publish: {ex.Message}");
            }
        }

        public async Task PublishConfigurations()
        {
                List<(string Topic, string Payload)> configurations = new List<(string Topic, string Payload)>
        {
            // Memory Usage
            (
                $"homeassistant/sensor/{_deviceId}/memory_usage/config",
                JsonConvert.SerializeObject(new
                {
                    name = $"{_deviceId}_memory_usage",
                    unique_id = $"{_deviceId}_memory_usage",
                    device = new
                    {
                        identifiers = new[] { $"{_deviceId}" },
                        manufacturer = "Custom",
                        model = "PC Monitor",
                        name = $"{_deviceId}",
                        sw_version = "1.0"
                    },
                    icon = "mdi:memory",
                    state_topic = $"homeassistant/sensor/{_deviceId}/memory_usage/state",
                    unit_of_measurement = "%"  // Add unit
                })
            ),
            // CPU Usage
            (
                $"homeassistant/sensor/{_deviceId}/cpu_usage/config",
                JsonConvert.SerializeObject(new
                {
                    name = $"{_deviceId}_cpu_usage",
                    unique_id = $"{_deviceId}_cpu_usage",
                    device = new
                    {
                        identifiers = new[] { $"{_deviceId}" },
                        manufacturer = "Custom",
                        model = "PC Monitor",
                        name = $"{_deviceId}",
                        sw_version = "1.0"
                    },
                    icon = "mdi:cpu-64-bit",
                    state_topic = $"homeassistant/sensor/{_deviceId}/cpu_usage/state",
                    unit_of_measurement = "%"  // Add unit
                })
            ),
            // Add more sensor configurations here, with appropriate units
        };
            var additionalConfigs = new List<(string Sensor, string Icon, string Unit)>
    {
        ("total_ram", "mdi:memory", "MB"),
        ("free_ram", "mdi:memory", "MB"),
        ("used_ram", "mdi:memory", "MB")
    };

            foreach (var (Sensor, Icon, Unit) in additionalConfigs)
            {
                configurations.Add((
                    $"homeassistant/sensor/{_deviceId}/{Sensor}/config",
                    JsonConvert.SerializeObject(new
                    {
                        name = $"{_deviceId}_{Sensor}",
                        unique_id = $"{_deviceId}_{Sensor}",
                        device = new
                        {
                            identifiers = new[] { $"{_deviceId}" },
                            manufacturer = "Custom",
                            model = "PC Monitor",
                            name = $"{_deviceId}",
                            sw_version = "1.0"
                        },
                        icon = Icon,
                        state_topic = $"homeassistant/sensor/{_deviceId}/{Sensor}/state",
                        unit_of_measurement = Unit
                    })
                ));
            }

            foreach (var (Topic, Payload) in configurations)
            {
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(Topic)
                    .WithPayload(Payload)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag(true)
                    .Build();

                await _mqttClient.PublishAsync(message);
            }

            Log.Information("Sensor configurations published successfully.");


                Log.Information("Sensor configurations published successfully.");
        }


        public void UpdatePCMetrics(PCMetrics metrics)
        {
            _pcMetrics = metrics;  // Make sure this assignment happens correctly

            // Debug logging
            Log.Information($"PC metrics updated in MQTT Service: CPU Usage = {_pcMetrics.CpuUsage}, Memory Usage = {_pcMetrics.MemoryUsage}, Total RAM = {_pcMetrics.TotalRam}, Free RAM = {_pcMetrics.FreeRam}, Used RAM = {_pcMetrics.UsedRam}");
        }



        public async Task ReconnectAsync()
        {
            // Consolidated reconnection logic
        }

        public async Task SubscribeAsync(string topic, MqttQualityOfServiceLevel qos)
        {
            // Check if already subscribed
            if (_subscribedTopics.Contains(topic))
            {
                Log.Information($"Already subscribed to {topic}.");
                return;
            }

            var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(f => f.WithTopic(topic).WithQualityOfServiceLevel(qos))
                .Build();

            try
            {
                await _mqttClient.SubscribeAsync(subscribeOptions);
                _subscribedTopics.Add(topic); // Track the subscription
                Log.Information("Subscribed to " + topic);
            }
            catch (Exception ex)
            {
                Log.Information($"Error during MQTT subscribe: {ex.Message}");
            }
        }

        public async Task UnsubscribeAsync(string topic)
        {
            if (!_subscribedTopics.Contains(topic))
            {
                Log.Information($"Not subscribed to {topic}, no need to unsubscribe.");
                return;
            }

            try
            {
                // Create the unsubscribe options, similar to how subscription options were created
                var unsubscribeOptions = new MqttClientUnsubscribeOptionsBuilder()
                    .WithTopicFilter(topic) // Add the topic from which to unsubscribe
                    .Build();

                // Perform the unsubscribe operation
                await _mqttClient.UnsubscribeAsync(unsubscribeOptions);

                // Remove the topic from the local tracking set
                _subscribedTopics.Remove(topic);

                Log.Information($"Successfully unsubscribed from {topic}.");
            }
            catch (Exception ex)
            {
                Log.Information($"Error during MQTT unsubscribe: {ex.Message}");
                // Depending on your error handling strategy, you might want to handle this
                // differently For example, you might want to throw the exception to let the caller
                // know the unsubscribe failed
            }
        }

        public async Task UpdateClientOptionsAndReconnect()
        {
            InitializeClientOptions(); // Method to reinitialize client options with updated settings
            await DisconnectAsync();
            await ConnectAsync();
        }

        public void UpdateConnectionStatus(string status) //could be obsolete
        {
            OnConnectionStatusChanged(status);
        }



        public async Task UpdateSettingsAsync(AppSettings newSettings)
        {
            _settings = newSettings;
            _deviceId = _settings.SensorPrefix;
            InitializeClientOptions(); // Reinitialize MQTT client options

            if (IsConnected)
            {
                await DisconnectAsync();
                await ConnectAsync();
            }
        }

        #endregion Public Methods

        #region Protected Methods

        protected virtual void OnConnectionStatusChanged(string status) //could be obsolete
        {
            ConnectionStatusChanged?.Invoke(status);
        }

        #endregion Protected Methods

        #region Private Methods

        private string DetermineDeviceClass(string sensor)
        {
            switch (sensor)
            {
                case "IsMuted":
                case "IsVideoOn":
                case "IsHandRaised":
                case "IsBackgroundBlurred":
                    return "switch"; // These are ON/OFF switches
                case "IsInMeeting":
                case "HasUnreadMessages":
                case "IsRecordingOn":
                case "IsSharing":
                case "teamsRunning":
                    return "binary_sensor"; // These are true/false sensors
                default:
                    return "unknown"; // Or a default device class if appropriate
            }
        }

        //private string DetermineIcon(string sensor, MeetingState state)
        //{
        //    return sensor switch
        //    {
        //        // If the sensor is "IsMuted", return "mdi:microphone-off" if state.IsMuted is true,
        //        // otherwise return "mdi:microphone"
        //        "IsMuted" => state.IsMuted ? "mdi:microphone-off" : "mdi:microphone",

        // // If the sensor is "IsVideoOn", return "mdi:camera" if state.IsVideoOn is true, //
        // otherwise return "mdi:camera-off" "IsVideoOn" => state.IsVideoOn ? "mdi:camera" : "mdi:camera-off",

        // // If the sensor is "IsHandRaised", return "mdi:hand-back-left" if // state.IsHandRaised
        // is true, otherwise return "mdi:hand-back-left-off" "IsHandRaised" => state.IsHandRaised ?
        // "mdi:hand-back-left" : "mdi:hand-back-left-off",

        // // If the sensor is "IsInMeeting", return "mdi:account-group" if state.IsInMeeting // is
        // true, otherwise return "mdi:account-off" "IsInMeeting" => state.IsInMeeting ?
        // "mdi:account-group" : "mdi:account-off",

        // // If the sensor is "IsRecordingOn", return "mdi:record-rec" if state.IsRecordingOn // is
        // true, otherwise return "mdi:record" "IsRecordingOn" => state.IsRecordingOn ?
        // "mdi:record-rec" : "mdi:record",

        // // If the sensor is "IsBackgroundBlurred", return "mdi:blur" if //
        // state.IsBackgroundBlurred is true, otherwise return "mdi:blur-off" "IsBackgroundBlurred"
        // => state.IsBackgroundBlurred ? "mdi:blur" : "mdi:blur-off",

        // // If the sensor is "IsSharing", return "mdi:monitor-share" if state.IsSharing is //
        // true, otherwise return "mdi:monitor-off" "IsSharing" => state.IsSharing ?
        // "mdi:monitor-share" : "mdi:monitor-off",

        // // If the sensor is "HasUnreadMessages", return "mdi:message-alert" if //
        // state.HasUnreadMessages is true, otherwise return "mdi:message-outline"
        // "HasUnreadMessages" => state.HasUnreadMessages ? "mdi:message-alert" : "mdi:message-outline",

        //        // If the sensor does not match any of the above cases, return "mdi:eye"
        //        _ => "mdi:eye"
        //    };
        //}

        //private string GetStateValue(string sensor, MeetingUpdate meetingUpdate)
        //{
        //    switch (sensor)
        //    {
        //        case "IsMuted":
        //            return (bool)meetingUpdate.MeetingState.GetType().GetProperty(sensor).GetValue(meetingUpdate.MeetingState, null) ? "ON" : "OFF";

        // case "IsVideoOn": return
        // (bool)meetingUpdate.MeetingState.GetType().GetProperty(sensor).GetValue(meetingUpdate.MeetingState,
        // null) ? "ON" : "OFF";

        // case "IsBackgroundBlurred": return
        // (bool)meetingUpdate.MeetingState.GetType().GetProperty(sensor).GetValue(meetingUpdate.MeetingState,
        // null) ? "ON" : "OFF";

        // case "IsHandRaised": // Cast to bool and then check the value return
        // (bool)meetingUpdate.MeetingState.GetType().GetProperty(sensor).GetValue(meetingUpdate.MeetingState,
        // null) ? "ON" : "OFF";

        // case "IsInMeeting": return
        // (bool)meetingUpdate.MeetingState.GetType().GetProperty(sensor).GetValue(meetingUpdate.MeetingState,
        // null) ? "True" : "False";

        // case "HasUnreadMessages": return
        // (bool)meetingUpdate.MeetingState.GetType().GetProperty(sensor).GetValue(meetingUpdate.MeetingState,
        // null) ? "True" : "False";

        // case "IsRecordingOn": return
        // (bool)meetingUpdate.MeetingState.GetType().GetProperty(sensor).GetValue(meetingUpdate.MeetingState,
        // null) ? "True" : "False";

        // case "IsSharing": // Similar casting for these properties return
        // (bool)meetingUpdate.MeetingState.GetType().GetProperty(sensor).GetValue(meetingUpdate.MeetingState,
        // null) ? "True" : "False";

        // case "teamsRunning": return
        // (bool)meetingUpdate.MeetingState.GetType().GetProperty(sensor).GetValue(meetingUpdate.MeetingState,
        // null) ? "True" : "False";

        //        default:
        //            return "unknown";
        //    }
        //}

        private void HandleSwitchCommand(string topic, string command)
        {
            // Determine which switch is being controlled based on the topic
            string switchName = topic.Split('/')[2]; // Assuming topic format is "homeassistant/switch/{switchName}/set"
            int underscoreIndex = switchName.IndexOf('_');
            if (underscoreIndex != -1 && underscoreIndex < switchName.Length - 1)
            {
                switchName = switchName.Substring(underscoreIndex + 1);
            }
            string jsonMessage = "";
            switch (switchName)
            {
                case "ismuted":
                    jsonMessage = $"{{\"apiVersion\":\"1.0.0\",\"service\":\"toggle-mute\",\"action\":\"toggle-mute\",\"manufacturer\":\"Jimmy White\",\"device\":\"THFHA\",\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"requestId\":1}}";
                    break;

                case "isvideoon":
                    jsonMessage = $"{{\"apiVersion\":\"1.0.0\",\"service\":\"toggle-video\",\"action\":\"toggle-video\",\"manufacturer\":\"Jimmy White\",\"device\":\"THFHA\",\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"requestId\":1}}";
                    break;

                case "isbackgroundblurred":
                    jsonMessage = $"{{\"apiVersion\":\"1.0.0\",\"service\":\"background-blur\",\"action\":\"toggle-background-blur\",\"manufacturer\":\"Jimmy White\",\"device\":\"THFHA\",\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"requestId\":1}}";
                    break;

                case "ishandraised":
                    jsonMessage = $"{{\"apiVersion\":\"1.0.0\",\"service\":\"raise-hand\",\"action\":\"toggle-hand\",\"manufacturer\":\"Jimmy White\",\"device\":\"THFHA\",\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},\"requestId\":1}}";
                    break;

                    // Add other cases as needed
            }

            if (!string.IsNullOrEmpty(jsonMessage))
            {
                // Raise the event
                CommandToTeams?.Invoke(jsonMessage);
            }
        }

        private void InitializeClient()
        {
            if (_mqttClient == null)
            {
                var factory = new MqttFactory();
                _mqttClient = (MqttClient?)factory.CreateMqttClient(); // This creates an IMqttClient, not a MqttClient.

                InitializeClientOptions(); // Ensure options are initialized with current settings
                if (_mqttClientsubscribed == false)
                {
                    _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
                    _mqttClientsubscribed = true;
                }
            }
        }

        private void InitializeClientOptions()
        {
            try
            {
                var factory = new MqttFactory();
                _mqttClient = (MqttClient?)factory.CreateMqttClient();

                if (!int.TryParse(_settings.MqttPort, out int mqttportInt))
                {
                    mqttportInt = 1883; // Default MQTT port
                    Log.Warning($"Invalid MQTT port provided, defaulting to {mqttportInt}");
                }

                var mqttClientOptionsBuilder = new MqttClientOptionsBuilder()
                    .WithClientId($"PC2MQTT_{_deviceId}")
                    .WithCredentials(_settings.MqttUsername, _settings.MqttPassword)
                    .WithCleanSession()
                    .WithTimeout(TimeSpan.FromSeconds(5));

                string protocol = _settings.UseWebsockets ? "ws" : "tcp";
                string connectionType = _settings.UseTLS ? "with TLS" : "without TLS";

                if (_settings.UseWebsockets)
                {
                    string websocketUri = _settings.UseTLS ? $"wss://{_settings.MqttAddress}:{mqttportInt}" : $"ws://{_settings.MqttAddress}:{mqttportInt}";
                    mqttClientOptionsBuilder.WithWebSocketServer(websocketUri);
                    Log.Information($"Configuring MQTT client for WebSocket {connectionType} connection to {websocketUri}");
                }
                else
                {
                    mqttClientOptionsBuilder.WithTcpServer(_settings.MqttAddress, mqttportInt);
                    Log.Information($"Configuring MQTT client for TCP {connectionType} connection to {_settings.MqttAddress}:{mqttportInt}");
                }

                if (_settings.UseTLS)
                {
                    // Create TLS parameters
                    var tlsParameters = new MqttClientOptionsBuilderTlsParameters
                    {
                        AllowUntrustedCertificates = _settings.IgnoreCertificateErrors,
                        IgnoreCertificateChainErrors = _settings.IgnoreCertificateErrors,
                        IgnoreCertificateRevocationErrors = _settings.IgnoreCertificateErrors,
                        UseTls = true
                    };

                    // If you need to validate the server certificate, you can set the CertificateValidationHandler.
                    // Note: Be cautious with bypassing certificate checks in production code!!
                    if (!_settings.IgnoreCertificateErrors)
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

                    // Apply the TLS parameters to the options builder
                    mqttClientOptionsBuilder.WithTls(tlsParameters);
                }

                _mqttOptions = mqttClientOptionsBuilder.Build();
                if (_mqttClient != null)
                {
                    if (_mqttClientsubscribed == false)
                    {
                        _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
                        _mqttClientsubscribed = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize MqttClientWrapper");
                throw; // Rethrowing the exception to handle it outside or log it as fatal depending on your error handling strategy.
            }
        }

        private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            Log.Information($"Received message on topic {e.ApplicationMessage.Topic}: {e.ApplicationMessage.ConvertPayloadToString()}");
            string topic = e.ApplicationMessage.Topic;
            string payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

            // Assuming the format is homeassistant/switch/{deviceId}/{switchName}/set Validate the
            // topic format and extract the switchName
            var topicParts = topic.Split(','); //not sure this is required
            topicParts = topic.Split('/');
            if (topicParts.Length == 4 && topicParts[0].Equals("homeassistant") && topicParts[1].Equals("switch") && topicParts[3].EndsWith("set"))
            {
                // Extract the action and switch name from the topic
                string switchName = topicParts[2];
                string command = payload; // command should be ON or OFF based on the payload

                // Now call the handle method
                HandleSwitchCommand(topic, command);
            }

            return Task.CompletedTask;
        }
        public async Task PublishPCMetrics()
        {
            PCMetrics metrics = PCMetrics.Instance;
            Log.Information($"Preparing to publish metrics: CPU Usage = {metrics.CpuUsage}, Memory Usage = {metrics.MemoryUsage}, Total RAM = {metrics.TotalRam}, Free RAM = {metrics.FreeRam}, Used RAM = {metrics.UsedRam}");

            if (_mqttClient == null || !_mqttClient.IsConnected)
            {
                Log.Warning("MQTT client is not connected. Unable to publish PC metrics.");
                return;
            }
            
            try
            {
                // CPU Usage
                var cpuUsageTopic = $"homeassistant/sensor/{_deviceId}/cpu_usage/state";
                await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                    .WithTopic(cpuUsageTopic)
                    .WithPayload(_pcMetrics.CpuUsage.ToString("N2"))  // Format as a string with two decimal places
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build());

                // Memory Usage
                var memoryUsageTopic = $"homeassistant/sensor/{_deviceId}/memory_usage/state";
                await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                    .WithTopic(memoryUsageTopic)
                    .WithPayload(_pcMetrics.MemoryUsage.ToString("N2"))  // Format as a string with two decimal places
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build());
                // Total RAM
                var totalRamTopic = $"homeassistant/sensor/{_deviceId}/total_ram/state";
                await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                    .WithTopic(totalRamTopic)
                    .WithPayload(_pcMetrics.TotalRam.ToString("N2"))  // Confirm this is not zero and correctly calculated
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build());

                // Free RAM
                var freeRamTopic = $"homeassistant/sensor/{_deviceId}/free_ram/state";
                await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                    .WithTopic(freeRamTopic)
                    .WithPayload(_pcMetrics.FreeRam.ToString("N2")) // Keep two decimal points
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build());

                // Used RAM
                var usedRamTopic = $"homeassistant/sensor/{_deviceId}/used_ram/state";
                await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                    .WithTopic(usedRamTopic)
                    .WithPayload(_pcMetrics.UsedRam.ToString("N2")) // Keep two decimal points
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build());
                Log.Information("PC metrics published successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to publish PC metrics: {ex.Message}");
            }
            Log.Information($"Updated metrics: CPU Usage = {_pcMetrics.CpuUsage}, Memory Usage = {_pcMetrics.MemoryUsage}, Total RAM = {_pcMetrics.TotalRam}, Free RAM = {_pcMetrics.FreeRam}, Used RAM = {_pcMetrics.UsedRam}");

            // Repeat for other metrics.

            // Repeat for other metrics.

        }





        private void OnMqttPublishTimerElapsed(object sender, ElapsedEventArgs e)
        {
            PublishPCMetrics().GetAwaiter().GetResult(); // This ensures the asynchronous PublishPCMetrics method is called properly.
        }



        #endregion Private Methods
    }
}