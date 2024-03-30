using Microsoft.Win32;
using MQTTnet.Protocol;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using Hardcodet.Wpf.TaskbarNotification;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using PC2MQTT.api;

using System.Windows.Controls;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Microsoft.VisualBasic.Devices;
using MQTTnet.Client;
using System.Text;


namespace PC2MQTT
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private MenuItem _aboutMenuItem;
        private MenuItem _logMenuItem;
        private Dictionary<string, string> _previousSensorStates = new Dictionary<string, string>();
        private AppSettings _settings;
        private string _settingsFilePath;
        private pc_sensors _pcSensors;
        private MenuItem _teamsStatusMenuItem;
        private string deviceid;
        private MenuItem _mqttStatusMenuItem;
        private PCMetrics _pcMetrics;
        private bool isDarkTheme = true;
        private bool mqttConnectionAttempting = false;
        private bool mqttConnectionStatusChanged = false;
        private bool mqttStatusUpdated = false;
        private System.Timers.Timer _updateTimer;
        private MqttService _mqttService;
        private PerformanceCounter cpuCounter; // Declare, but don't initialize here
        private PerformanceCounter memoryCounter;
        private double _totalPhysicalMemory;
        public double TotalPhysicalMemory
        {
            get => _totalPhysicalMemory;
            set
            {
                if (_totalPhysicalMemory != value)
                {
                    _totalPhysicalMemory = value;
                    OnPropertyChanged(nameof(TotalPhysicalMemory));
                }
            }
        }
        private double _usedMemoryPercentage;
        public double UsedMemoryPercentage
        {
            get => _usedMemoryPercentage;
            set
            {
                if (_usedMemoryPercentage != value)
                {
                    _usedMemoryPercentage = value;
                    OnPropertyChanged(nameof(UsedMemoryPercentage));
                }
            }
        }

        private Queue<double> cpuReadings = new Queue<double>();
        public event PropertyChangedEventHandler PropertyChanged;
       
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private double _cpuUsage;
        public double CpuUsage
        {
            get => _cpuUsage;
            set
            {
                if (_cpuUsage != value)
                {
                    _cpuUsage = value;
                    OnPropertyChanged(nameof(CpuUsage)); // Notify the UI that value has changed
                }
            }
        }
        private double _memoryUsage;
        public double MemoryUsage
        {
            get => _memoryUsage;
            set
            {
                if (_memoryUsage != value)
                {
                    _memoryUsage = value;
                    OnPropertyChanged(nameof(MemoryUsage));
                }
            }
        }
        private double memoryAvailableGB;
        private double totalMemoryGB;
        private double memoryUsedGB;

        public MainWindow()
        {
            // Get the local application data folder path
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
       

            // Configure logging
            LoggingConfig.Configure();

            // Create the PC2MQTT folder in the local application data folder
            var appDataFolder = Path.Combine(localAppData, "PC2MQTT");
            Log.Debug("Set Folder Path to {path}", appDataFolder);
            Directory.CreateDirectory(appDataFolder); // Ensure the directory exists

            // Set the settings file path
            _settingsFilePath = Path.Combine(appDataFolder, "settings.json");

            // Get the app settings instance
            var settings = AppSettings.Instance;
            _settings = AppSettings.Instance;
            var _mqttService = new MqttService(_settings, "PC2MQTT");

            // Get the device ID
            if (string.IsNullOrEmpty(_settings.SensorPrefix))
            {
                deviceid = System.Environment.MachineName;
            }
            else
            {
                deviceid = _settings.SensorPrefix;
            }

            // Log the settings file path
            Log.Debug("Settings file path is {path}", _settingsFilePath);

            // Initialize the main window
            this.InitializeComponent();

            InitializeMqttService();
            this.DataContext = this;
           

            // Add event handler for when the main window is loaded
            this.Loaded += MainPage_Loaded;
           
            // Set the icon for the notification tray
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Square150x150Logo.scale-200.ico");
            MyNotifyIcon.Icon = new System.Drawing.Icon(iconPath);
            CreateNotifyIconContextMenu();
            // Create a new instance of the MQTT Service class
           
        }
        private double GetCpuUsage()
        {
            if (cpuCounter == null) return 0; // Safety check
            var value = cpuCounter.NextValue();
            cpuReadings.Enqueue(value);
            while (cpuReadings.Count > 10) cpuReadings.Dequeue();
            return cpuReadings.Average();
        }
        private async Task PublishMessage(string topic, string payload)
        {
            await _mqttService.PublishAsync(topic, payload, MqttQualityOfServiceLevel.AtLeastOnce);
        }
        private async Task SubscribeToTopic(string topic)
        {
            await _mqttService.SubscribeAsync(topic, MqttQualityOfServiceLevel.AtLeastOnce);
        }
        private void InitializeMqttService()
        {
            // Assuming _settings and clientId are properly set up here
            _mqttService = new MqttService(_settings, "YourClientIdHere");
            _mqttService.MessageReceived += OnMessageReceivedAsync;
            _= PublishAutoDiscoveryConfigs();
        }

        private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            // Process received message
            var message = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
            Console.WriteLine($"Message received on topic {e.ApplicationMessage.Topic}: {message}");
            // Implement further processing as needed
        }


        private void OnMqttMessageReceived(string topic, string payload)
        {
            // Handle the received message. Update UI or process data as needed.
            Dispatcher.Invoke(() => {
                // Example: Update UI based on topic and payload
                if (topic == "some/specific/topic")
                {
                    // Update your UI or internal state
                }
            });
        }


        private void UpdateCpuUsageDisplay()
        {
            if (cpuCounter == null) return;
            double cpuUsage = GetCpuUsage(); // Fetch new value
            PCMetrics metrics = PCMetrics.Instance;
            metrics.CpuUsage = cpuUsage; // Update the singleton instance
            Dispatcher.Invoke(() => {
                CpuUsage = cpuUsage; // Update bound property
            });

        }
        private void UpdateMemoryUsageDisplay()
        {
            // Initialization and safety checks.
            if (memoryCounter == null || TotalPhysicalMemory <= 0) return;
            PCMetrics metrics = PCMetrics.Instance;
            // Perform calculations.
            double memoryAvailableGB = memoryCounter.NextValue() / 1024; // Convert from MB to GB
            double totalMemoryGB = TotalPhysicalMemory; // Already in GB from MainPage_Loaded
            double memoryUsedGB = totalMemoryGB - memoryAvailableGB;
            double memoryUsagePercentage = (memoryUsedGB / totalMemoryGB) * 100;

            // Update the singleton instance.
            
            metrics.MemoryUsage = memoryUsagePercentage;
            metrics.TotalRam = totalMemoryGB;
            metrics.FreeRam = memoryAvailableGB;
            metrics.UsedRam = memoryUsedGB;
           

            // Update UI.
            Dispatcher.Invoke(() =>
            {
                MemoryUsage = memoryUsagePercentage; // Update UI element for memory usage
               UsedMemoryPercentage = memoryUsagePercentage;                                   // Update other UI elements if necessary.
            });
        }
      
        private void InitializeTimer()
        {
            _updateTimer = new System.Timers.Timer(1000); // Update every second
            _updateTimer.Elapsed += OnTimedEvent; // Assign the event handler
            _updateTimer.AutoReset = true; // Continue raising events
            _updateTimer.Enabled = true; // Start the timer
        }
        private void MyNotifyIcon_Click(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                // Restore the window if it's minimized
                this.Show();
                this.WindowState = WindowState.Normal;
            }
            else
            {
                // Minimize the window if it's currently normal or maximized
                this.WindowState = WindowState.Minimized;
            }
        }
        private async void InitializePerformanceCounters()
        {
            await Task.Run(() =>
            {
                cpuCounter = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", true);
                var unusedCpu = cpuCounter.NextValue(); // Prime the CPU counter.

                // Initialize and prime memory counter.
                memoryCounter = new PerformanceCounter("Memory", "Available MBytes", true);
                var unusedMem = memoryCounter.NextValue(); // Prime the memory counter. 
            });
        }
      
        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                // Only hide the window and show the NotifyIcon when minimized
                this.Hide();
                MyNotifyIcon.Visibility = Visibility.Visible;
            }
            else
            {
                // Ensure the NotifyIcon is hidden when the window is not minimized
                MyNotifyIcon.Visibility = Visibility.Collapsed;
            }

            base.OnStateChanged(e);
        }
        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Retrieve the current theme from settings
            string currentTheme = _settings.Theme;

            // Get the device ID from the MqttService instance
            if (string.IsNullOrEmpty(_settings.SensorPrefix))
            {
                deviceid = System.Environment.MachineName;
            }
            else
            {
                deviceid = _settings.SensorPrefix;
            }

            // Get the list of sensors and switches from the MqttService instance
            var sensorsAndSwitches = new List<string>(_pcSensors.GetSensorAndSwitchNames(deviceid));

            // Create and display the AboutWindow
            AboutWindow aboutWindow = new AboutWindow(deviceid, MyNotifyIcon, sensorsAndSwitches);
            aboutWindow.Owner = this;
            aboutWindow.ShowDialog();
        }

        private void OnTimedEvent(Object source, System.Timers.ElapsedEventArgs e)
        {
            // Initialize local variables to default values
            double localCpuUsage = 0;
            double localMemoryUsage = 0;
            double localTotalRam = 0;
            double localFreeRam = 0;
            double localUsedRam = 0;
            // Now update the singleton instance of PCMetrics with the latest values
            var metrics = PCMetrics.Instance;
            // Update logic (should run in the UI thread because it might update UI elements)
            Dispatcher.Invoke(() =>
            {
                UpdateCpuUsageDisplay();  // This updates this.CpuUsage based on latest system info
                UpdateMemoryUsageDisplay(); // This updates this.MemoryUsage and RAM values based on latest system info
                PublishMetrics(); // Publish the updated metrics to MQTT
                // Copy values to local variables after they have been updated
                localCpuUsage = metrics.CpuUsage;
                localMemoryUsage = metrics.MemoryUsage;
                localTotalRam = metrics.TotalRam;
                localFreeRam = metrics.FreeRam;
                localUsedRam = metrics.UsedRam;
               
            });

            
            metrics.CpuUsage = localCpuUsage;
           
            // Pass the updated singleton instance to your MQTT service for publishing
            _pcSensors?.UpdatePCMetrics(metrics);
        }
        private void ApplyTheme(string theme)
        {
            isDarkTheme = theme == "Dark";
            Uri themeUri;
            if (theme == "Dark")
            {
                themeUri = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Dark.xaml");
                isDarkTheme = true;
            }
            else
            {
                themeUri = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Light.xaml");
                isDarkTheme = false;
            }

            // Update the theme
            var existingTheme = System.Windows.Application.Current.Resources.MergedDictionaries.FirstOrDefault(d => d.Source == themeUri);
            if (existingTheme == null)
            {
                existingTheme = new ResourceDictionary() { Source = themeUri };
                System.Windows.Application.Current.Resources.MergedDictionaries.Add(existingTheme);
            }

            // Remove the other theme
            var otherThemeUri = isDarkTheme
                ? new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Light.xaml")
                : new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Dark.xaml");

            var currentTheme = System.Windows.Application.Current.Resources.MergedDictionaries.FirstOrDefault(d => d.Source == otherThemeUri);
            if (currentTheme != null)
            {
             
                System.Windows.Application.Current.Resources.MergedDictionaries.Remove(currentTheme);
            }
        }
        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Handle the click event for the exit menu item (Close the application)
            System.Windows.Application.Current.Shutdown();
        }
        private void LogsButton_Click(object sender, RoutedEventArgs e)
        {
            string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PC2MQTT");

            // Ensure the directory exists
            if (!Directory.Exists(folderPath))
            {
                Log.Error("Log directory does not exist.");
                return;
            }

            // Get the most recent log file
            var logFile = Directory.GetFiles(folderPath, "PC2MQTT_Log*.log")
                                   .OrderByDescending(File.GetCreationTime)
                                   .FirstOrDefault();

            if (logFile != null && File.Exists(logFile))
            {
                try
                {
                    ProcessStartInfo processStartInfo = new ProcessStartInfo
                    {
                        FileName = logFile,
                        UseShellExecute = true
                    };

                    Process.Start(processStartInfo);
                }
                catch (Exception ex)
                {
                    Log.Error($"Error opening log file: {ex.Message}");
                }
            }
            else
            {
                Log.Error("Log file does not exist.");
            }
        }
        private void ShowHideMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (this.IsVisible)
            {
                this.Hide();
            }
            else
            {
                this.Show();
                this.WindowState = WindowState.Normal;
            }
        }
        private void CreateNotifyIconContextMenu()
        {
            ContextMenu contextMenu = new ContextMenu();

            // Show/Hide Window
            MenuItem showHideMenuItem = new MenuItem();
            showHideMenuItem.Header = "Show/Hide";
            showHideMenuItem.Click += ShowHideMenuItem_Click;

            // MQTT Status
            _mqttStatusMenuItem = new MenuItem { Header = "MQTT Status: Unknown", IsEnabled = false };



            // Logs
            _logMenuItem = new MenuItem { Header = "View Logs" };
            _logMenuItem.Click += LogsButton_Click; // Reuse existing event handler

            // About
            _aboutMenuItem = new MenuItem { Header = "About" };
            _aboutMenuItem.Click += AboutMenuItem_Click;

            // Exit
            MenuItem exitMenuItem = new MenuItem();
            exitMenuItem.Header = "Exit";
            exitMenuItem.Click += ExitMenuItem_Click;

            contextMenu.Items.Add(showHideMenuItem);
            contextMenu.Items.Add(_logMenuItem);
            contextMenu.Items.Add(_aboutMenuItem);
            contextMenu.Items.Add(new Separator()); // Separator before exit
            contextMenu.Items.Add(exitMenuItem);

            MyNotifyIcon.ContextMenu = contextMenu;
        }
        private void UpdateMqttStatus(string status)
        {
            Dispatcher.Invoke(() =>
            {
                // Assuming MQTTConnectionStatus is a Label or similar control
                MQTTConnectionStatus.Text = $"MQTT Status: {status}";
                UpdateStatusMenuItems();
            });
        }
        protected override async void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            _updateTimer.Elapsed -= OnTimedEvent;
            _updateTimer.Stop();
            _updateTimer.Dispose();

            MyNotifyIcon.Dispose();

            if (_mqttService != null)
            {
                await _mqttService.DisconnectAsync();
            }
        }

        private void UpdateStatusMenuItems()
        {
            Dispatcher.Invoke(() =>
            {
                // Check if _mqttService is not null and use the IsConnected property
                var isConnected = _mqttService?.IsConnected ?? false;
                var statusText = isConnected ? "MQTT Status: Connected" : "MQTT Status: Disconnected";

                // Update MQTT connection status text
                MQTTConnectionStatus.Text = statusText;
                // Update menu items
                if (_mqttStatusMenuItem != null) // Ensure _mqttStatusMenuItem is not null
                {
                    _mqttStatusMenuItem.Header = statusText;
                }

                // Add other status updates here as necessary
            });
        }
        private async Task PublishAutoDiscoveryConfigs()
        {
            var baseTopic = $"homeassistant/sensor/{deviceid}/";
            var sensors = new Dictionary<string, string>
            {
                {"cpu_usage", "%"},
                {"total_ram", "GB"},
                {"free_ram", "GB"},
                {"used_ram", "GB"}
            };

            foreach (var sensor in sensors)
            {
                var configTopic = $"{baseTopic}{sensor.Key}/config";
                var payload = new
                {
                    name = $"{deviceid} {sensor.Key.Replace("_", " ").ToUpper()}",
                    state_topic = $"{baseTopic}{sensor.Key}/state",
                    unit_of_measurement = sensor.Value,
                    value_template = "{{ value_json.value }}",
                    device_class = "measurement",
                    unique_id = $"{deviceid}_{sensor.Key}"
                };

                await _mqttService.PublishAsync(configTopic, JsonConvert.SerializeObject(payload));
            }
        }

        private async Task PublishMetrics()
        {
            if (_pcMetrics == null) return;
            var baseTopic = $"homeassistant/sensor/{deviceid}/";
                    var metrics = new Dictionary<string, object>
            {
                {"cpu_usage", _pcMetrics.CpuUsage},
                {"total_ram", _pcMetrics.TotalRam},
                {"free_ram", _pcMetrics.FreeRam},
                {"used_ram", _pcMetrics.UsedRam}
            };

            foreach (var metric in metrics)
            {
                var stateTopic = $"{baseTopic}{metric.Key}/state";
                var payload = JsonConvert.SerializeObject(new { value = metric.Value });
                await _mqttService.PublishAsync(stateTopic, payload);
            }
        }

        private async void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("SaveSettings_Click: Save Settings Clicked" + _settings.ToString());

            await SaveSettingsAsync();
        }
        private void ToggleThemeButton_Click(object sender, RoutedEventArgs e)
        {
            // Toggle the theme
            isDarkTheme = !isDarkTheme;
            _settings.Theme = isDarkTheme ? "Dark" : "Light";
            ApplyTheme(_settings.Theme);

            // Save settings after changing the theme
            _ = SaveSettingsAsync();
        }
        private async Task SaveSettingsAsync()
        {
            // Get the current settings from the singleton instance
            var settings = AppSettings.Instance;

            // Temporary storage for old values to compare after updating
            var oldMqttAddress = settings.MqttAddress;
            var oldMqttPort = settings.MqttPort;
            var oldMqttUsername = settings.MqttUsername;
            var oldMqttPassword = settings.MqttPassword;
            var oldUseTLS = settings.UseTLS;
            var oldIgnoreCertificateErrors = settings.IgnoreCertificateErrors;
            var oldUseWebsockets = settings.UseWebsockets;
            var oldSensorPrefix = settings.SensorPrefix;
            var oldUseSafeCommands = settings.UseSafeCommands;
            // Update the settings from UI components
            Dispatcher.Invoke(() =>
            {
                settings.MqttAddress = MqttAddress.Text;
                settings.MqttPort = MqttPort.Text;
                settings.MqttUsername = MqttUserNameBox.Text;
                settings.MqttPassword = MQTTPasswordBox.Password;
                settings.UseTLS = UseTLS.IsChecked ?? false;
                settings.IgnoreCertificateErrors = IgnoreCert.IsChecked ?? false;
                settings.RunMinimized = RunMinimisedCheckBox.IsChecked ?? false;
                settings.UseWebsockets = Websockets.IsChecked ?? false;
                settings.RunAtWindowsBoot = RunAtWindowsBootCheckBox.IsChecked ?? false;
                settings.SensorPrefix = string.IsNullOrEmpty(SensorPrefixBox.Text) ? System.Environment.MachineName : SensorPrefixBox.Text;
                settings.UseSafeCommands = SafePowerCheckBox.IsChecked ?? false;
            });

            // Now check if MQTT settings have changed
            bool mqttSettingsChanged = (oldMqttAddress != settings.MqttAddress) ||
                                       (oldMqttPort != settings.MqttPort) ||
                                       (oldMqttUsername != settings.MqttUsername) ||
                                       (oldMqttPassword != settings.MqttPassword) ||
                                       (oldUseTLS != settings.UseTLS) ||
                                       (oldIgnoreCertificateErrors != settings.IgnoreCertificateErrors) ||
                                       (oldUseWebsockets != settings.UseWebsockets);

            bool sensorPrefixChanged = (oldSensorPrefix != settings.SensorPrefix);

            // Save the updated settings to file
            settings.SaveSettingsToFile();

            if (mqttSettingsChanged || sensorPrefixChanged)
            {
                // Perform actions if MQTT settings or sensor prefix have changed
                Log.Debug("SaveSettingsAsync: MQTT settings have changed. Reconnecting MQTT client...");
              
            }
        }
  
        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            //LoadSettings();
            ComputerInfo computerInfo = new ComputerInfo();
            TotalPhysicalMemory = computerInfo.TotalPhysicalMemory / (1024.0 * 1024 * 1024); // Convert from bytes to gigabytes

            await Task.Run(() => InitializePerformanceCounters()); // Move heavy lifting off the UI thread.
            InitializeTimer();
            RunAtWindowsBootCheckBox.IsChecked = _settings.RunAtWindowsBoot;
            RunMinimisedCheckBox.IsChecked = _settings.RunMinimized;
            MqttUserNameBox.Text = _settings.MqttUsername;
            UseTLS.IsChecked = _settings.UseTLS;
            Websockets.IsChecked = _settings.UseWebsockets;
            IgnoreCert.IsChecked = _settings.IgnoreCertificateErrors;
            MQTTPasswordBox.Password = _settings.MqttPassword;
            MqttAddress.Text = _settings.MqttAddress;
            SafePowerCheckBox.IsChecked = _settings.UseSafeCommands;
            // Added to set the sensor prefix
            if (string.IsNullOrEmpty(_settings.SensorPrefix))
            {
                SensorPrefixBox.Text = System.Environment.MachineName;
            } 
            else
            {
                SensorPrefixBox.Text = _settings.SensorPrefix;
            }
            SensorPrefixBox.Text = _settings.SensorPrefix;
            MqttPort.Text = _settings.MqttPort;
            

            ApplyTheme(_settings.Theme);
            if (RunMinimisedCheckBox.IsChecked == true)
            {// Start the window minimized and hide it
                this.WindowState = WindowState.Minimized;
                this.Hide();
                MyNotifyIcon.Visibility = Visibility.Visible; // Show the NotifyIcon in the system tray
            }
            Dispatcher.Invoke(() => UpdateStatusMenuItems());
            ShowOneTimeNoticeIfNeeded();
        }
        private void ShowOneTimeNoticeIfNeeded()
        {
            // Check if the one-time notice has already been shown
            if (!_settings.HasShownOneTimeNotice)
            {
                // Show the notice to the user
                System.Windows.MessageBox.Show("Important: Due to recent updates, the functionality of PC2MQTT has changed. Sensors are now Binarysensors - please make sure you update any automations etc that rely on the sensors.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);

                // Update the setting so that the notice isn't shown again
                _settings.HasShownOneTimeNotice = true;

                // Save the updated settings to file
                _settings.SaveSettingsToFile();
            }
        }
    }
}