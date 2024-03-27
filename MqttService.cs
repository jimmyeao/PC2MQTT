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
using System.Linq.Expressions;
using System.Windows;

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
            _sensorNames = new List<string>
            {
                "cpu_usage",
                "memory_usage",
                // Add all other sensor names here
                "total_ram",
                "free_ram",
                "used_ram"
            };
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
                await PublishConfigurations();
                await SubscribeToControlCommands();
                _mqttClient.ApplicationMessageReceivedAsync -= OnMessageReceivedAsync;
                _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

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
                        await SubscribeToControlCommands();
                        _mqttClient.ApplicationMessageReceivedAsync -= OnMessageReceivedAsync;
                        _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
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
            // Check if the client is connected before attempting to publish
            if (!_mqttClient.IsConnected)
            {
                Log.Warning("Cannot publish message because the MQTT client is not connected.");
                // Consider handling reconnection here or notifying the rest of your application
                return;
            }

            try
            {
                await _mqttClient.PublishAsync(message, CancellationToken.None);
                Log.Information("Publish successful." + message.Topic);
            }
            catch (Exception ex)
            {
                Log.Information($"Error during MQTT publish: {ex.Message}");
            }
        }

        public async Task PublishConfigurations(bool forcePublish = false)
        {
            // Device common information
            var deviceInfo = new
            {
                identifiers = new[] { _deviceId },
                manufacturer = "Custom",
                model = "PC Monitor",
                name = _deviceId,
                sw_version = "1.0"
            };

            // List of configurations for sensors and switches
            List<(string Topic, string Payload)> configurations = new List<(string Topic, string Payload)>();

            // Include additional sensor and switch names here, similar to how you had in GetEntityNames
            List<string> entityNames = new List<string>
    {
        "cpu_usage", "memory_usage", "total_ram", "free_ram", "used_ram",
        "shutdown", "reboot", "standby", "hibernate" // Add control switches
    };

            // Populate configurations
            foreach (var entityName in entityNames)
            {
                string topic;
                object payload;

                if (entityName.Contains("shutdown") || entityName.Contains("reboot") || entityName.Contains("standby") || entityName.Contains("hibernate"))
                {
                    // Switches (for shutdown, reboot, standby, hibernate)
                    topic = $"homeassistant/switch/{_deviceId}/{entityName}/config";
                    payload = new
                    {
                        name = $"{_deviceId}_{entityName}",
                        unique_id = $"{_deviceId}_{entityName}",
                        device = deviceInfo,
                        command_topic = $"homeassistant/switch/{_deviceId}/{entityName}/set",
                        state_topic = $"homeassistant/switch/{_deviceId}/{entityName}/state",
                        payload_on = "ON",
                        payload_off = "OFF"
                    };
                }
                else
                {
                    // Sensors (for CPU usage, memory usage, etc.)
                    topic = $"homeassistant/sensor/{_deviceId}/{entityName}/config";
                    payload = new
                    {
                        name = $"{_deviceId}_{entityName}",
                        unique_id = $"{_deviceId}_{entityName}",
                        device = deviceInfo,
                        state_topic = $"homeassistant/sensor/{_deviceId}/{entityName}/state",
                        unit_of_measurement = entityName.Contains("usage") ? "%" : "GB",
                        icon = entityName.Contains("cpu") ? "mdi:cpu-64-bit" : "mdi:memory"
                    };
                }

                configurations.Add((topic, JsonConvert.SerializeObject(payload)));
            }

            // Publish configurations and initial state
            foreach (var (Topic, Payload) in configurations)
            {
                // Publish the configuration
                var configMessage = new MqttApplicationMessageBuilder()
                    .WithTopic(Topic)
                    .WithPayload(Payload)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag(true)
                    .Build();

                await PublishAsync(configMessage);

                // If it's a switch, publish its initial state as "OFF"
                if (Topic.Contains("/switch/"))
                {
                    string stateTopic = Topic.Replace("/config", "/state");
                    var stateMessage = new MqttApplicationMessageBuilder()
                        .WithTopic(stateTopic)
                        .WithPayload("OFF") // Ensuring switches are initialized as "OFF"
                        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                        .WithRetainFlag(true)
                        .Build();

                    await PublishAsync(stateMessage);
                }
            }
        }


        public void UpdatePCMetrics(PCMetrics metrics)
        {
            _pcMetrics = metrics;  // Make sure this assignment happens correctly

            // Debug logging
        }

        public async Task SubscribeAsync(string topic, MqttQualityOfServiceLevel qos)
        {
            if (!_mqttClient.IsConnected)
            {
                Log.Warning("Cannot subscribe to topics because the MQTT client is not connected.");
                return; // Exit early if not connected
            }
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
            var controlCommands = new List<string> { "shutdown", "reboot", "standby", "hibernate" };
            try
            {
                foreach (var command in controlCommands)
                {
                    topic = $"homeassistant/switch/{_deviceId}/{command}/set";
                    await SubscribeAsync(topic, MqttQualityOfServiceLevel.AtLeastOnce);
                    Log.Information("Subscribed to " + topic);
                }
            } catch (Exception ex)
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


        public void HandleControlCommand(string payload, string action)
        {
            // Log for debugging
            Log.Information($"Received control command: {action}, payload: {payload}");

            // Handle different actions
            switch (action.ToLower())
            {
                case "shutdown":
                    // Call your shutdown method here
                    PerformShutdown();
                    break;
                case "reboot":
                    // Call your reboot method here
                    PerformReboot();
                    break;
                case "standby":
                    // Call your standby method here
                    PerformStandby();
                    break;
                case "hibernate":
                    // Call your hibernate method here
                    PerformHibernate();
                    break;
                // Add more cases as needed
                default:
                    Log.Warning($"Unknown control command: {action}");
                    break;
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
        private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            string topic = e.ApplicationMessage.Topic;
            string payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
            Log.Information($"Received message on topic {topic}: {payload}");

            // Decompose the topic to understand what is the command
            var topicParts = topic.Split('/');
            if (topicParts.Length > 2 && topicParts[0] == "homeassistant" && topicParts[1] == "switch")
            {
                string command = topicParts[3]; // For example: "shutdown"
                if (topicParts[4] == "set") // To ensure it is a command
                {
                    HandleControlCommand(payload, command); // Handle the command
                }
            }
        }
        public async Task SubscribeToControlCommands()
        {
            var commands = new List<string> { "shutdown", "reboot", "standby", "hibernate" };
            foreach (var command in commands)
            {
                string topic = $"homeassistant/switch/{_deviceId}/{command}/set";
                await SubscribeAsync(topic, MqttQualityOfServiceLevel.AtLeastOnce);
            }
        }
        public async Task PublishPCMetrics()
        {
            PCMetrics metrics = PCMetrics.Instance;

            if (_mqttClient == null || !_mqttClient.IsConnected)
            {
                Log.Warning("MQTT client is not connected. Unable to publish PC metrics.");
                // we should try to reccoonect here
                await ConnectAsync();
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
                    .WithPayload(_pcMetrics.TotalRam.ToString("N2"))  // Format as a string with two decimal places
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
                
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to publish PC metrics: {ex.Message}");
            }

        }
        private void OnMqttPublishTimerElapsed(object sender, ElapsedEventArgs e)
        {
            PublishPCMetrics().GetAwaiter().GetResult(); // This ensures the asynchronous PublishPCMetrics method is called properly.
        }
        private void PerformShutdown()
        {
            if (_settings.UseSafeCommands)
            {
                // Use Dispatcher to ensure the dialog is opened on the UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ConfirmationDialog dialog = new ConfirmationDialog("Shutdown Confirmation", "Are you sure you want to Shutdown the computer?");
                    if (dialog.ShowDialog() == true)
                    {
                        Log.Information("User confirmed the shutdown command.");
                        Process.Start("shutdown", "/s /t 0");

                    }
                    else
                    {
                        Log.Information("User cancelled the standby command.");
                    }
                });
            }
            else
            {
                // System-specific command to shutdown
                Process.Start("shutdown", "/s /t 0");
            }
            
        }

        private void PerformStandby()
        {
            // System-specific command to standby
            // Windows does not have a direct standby command, typically handled by hardware
            if (_settings.UseSafeCommands)
            {
                // Use Dispatcher to ensure the dialog is opened on the UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ConfirmationDialog dialog = new ConfirmationDialog("Standby Confirmation", "Are you sure you want to put the computer to standby?");
                    if (dialog.ShowDialog() == true)
                    {
                        Log.Information("User confirmed the standby command.");
                        // Proceed with the standby
                        // Note: There's no direct command for standby in Windows, handled by hardware
                    }
                    else
                    {
                        Log.Information("User cancelled the standby command.");
                    }
                });
            }
            else
            {
                // Directly enter standby without confirmation, if possible
            }
        }


        private void PerformHibernate()
        {
            // System-specific command to hibernate
            if (_settings.UseSafeCommands)
            {
                // Use Dispatcher to ensure the dialog is opened on the UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ConfirmationDialog dialog = new ConfirmationDialog("Standby Confirmation", "Are you sure you want to hibernate the computer?");
                    if (dialog.ShowDialog() == true)
                    {
                        Log.Information("User confirmed the hibernate command.");
                        // Proceed with the standby
                        Process.Start("shutdown", "/h");
                    }
                    else
                    {
                        Log.Information("User cancelled the hibernate command.");
                    }
                });
            }
            else
            {
                // Directly enter standby without confirmation, if possible
                Process.Start("shutdown", "/h");
            }
        }
        public List<string> GetSensorAndSwitchNames()
        {
            // Assuming you have a method or way to get all sensor and switch names
            // For example, combining predefined names with dynamic ones from settings
            List<string> names = new List<string>();

            foreach(var sensor in _sensorNames)  // Assuming _sensorNames contains names of your sensors
        {
                names.Add($"sensor.{_deviceId}_{sensor}");  // Adjust the format as needed
            }

            // Add switch names
            // If you have a list or pattern for switches, add them here
            names.Add($"switch.{_deviceId}_shutdown");
            names.Add($"switch.{_deviceId}_reboot");
            names.Add($"switch.{_deviceId}_standby");
            names.Add($"switch.{_deviceId}_hibernate");

            // You can extend this list based on how your sensors and switches are named or stored

            return names;
        }

        private void PerformReboot()
        {
            // System-specific command to reboot
            if (_settings.UseSafeCommands)
            {
                // Use Dispatcher to ensure the dialog is opened on the UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ConfirmationDialog dialog = new ConfirmationDialog("Standby Confirmation", "Are you sure you want to reboot the computer?");
                    if (dialog.ShowDialog() == true)
                    {
                        Log.Information("User confirmed the reboot command.");
                        Process.Start("shutdown", "/r /t 0");
                    }
                    else
                    {
                        Log.Information("User cancelled the reboot command.");
                    }
                });
            }
            else
            {
                // Directly enter standby without confirmation, if possible
                Process.Start("shutdown", "/r /t 0");
            }
        }

        #endregion Private Methods
    } //Process.Start("shutdown", "/r /t 0");
}