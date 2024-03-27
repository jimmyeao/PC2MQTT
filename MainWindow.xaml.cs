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
using System.Windows.Controls;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Microsoft.VisualBasic.Devices;


namespace PC2MQTT
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private MenuItem _aboutMenuItem;
        private MenuItem _logMenuItem;
        //private MqttManager _mqttManager;
        private MqttService _mqttService;
        private MenuItem _mqttStatusMenuItem;
        //private string Mqtttopic;
        private Dictionary<string, string> _previousSensorStates = new Dictionary<string, string>();
        private AppSettings _settings;
        private string _settingsFilePath;
        private MenuItem _teamsStatusMenuItem;
        private string deviceid;
        private bool isDarkTheme = true;
        private bool mqttCommandToTeams = false;
        private bool mqttConnectionAttempting = false;
        private bool mqttConnectionStatusChanged = false;
        private bool mqttStatusUpdated = false;
        private System.Timers.Timer _updateTimer;
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
            
            this.DataContext = this;
      
            
            // Add event handler for when the main window is loaded
            this.Loaded += MainPage_Loaded;
            SystemEvents.PowerModeChanged += OnPowerModeChanged; //subscribe to power events
            // Set the icon for the notification tray
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Square150x150Logo.scale-200.ico");
            MyNotifyIcon.Icon = new System.Drawing.Icon(iconPath);
            CreateNotifyIconContextMenu();
            // Create a new instance of the MQTT Service class
            _mqttService = new MqttService(_settings, deviceid, new List<string>()); // Use actual sensor names or leave empty if not applicable

            // Now attach the event handlers
            _mqttService.ConnectionStatusChanged += MqttManager_ConnectionStatusChanged;
            _mqttService.ConnectionAttempting += MqttManager_ConnectionAttempting;

            // Set the action to be performed when a new token is updated

            // Initialize connections
            InitializeConnections();
            //foreach (var sensor in sensorNames)
            //{
            //    _previousSensorStates[$"{deviceid}_{sensor}"] = "";
            //}
        }
        private double GetCpuUsage()
        {
            if (cpuCounter == null) return 0; // Safety check
            var value = cpuCounter.NextValue();
            cpuReadings.Enqueue(value);
            while (cpuReadings.Count > 10) cpuReadings.Dequeue();
            return cpuReadings.Average();
        }


        public async Task InitializeConnections()
        {
            if (_mqttService != null)
            {
                await _mqttService.ConnectAsync();
                await _mqttService.SubscribeAsync("homeassistant/switch/+/set", MqttQualityOfServiceLevel.AtLeastOnce);
            }

        }
        private void MqttManager_ConnectionAttempting(string status)
        {
            Dispatcher.Invoke(() =>
            {
                MQTTConnectionStatus.Text = status;
                _mqttStatusMenuItem.Header = status; // Update the system tray menu item as well
                                                     // No need to update other status menu items as
                                                     // this is specifically for MQTT connection
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




        private void MqttManager_ConnectionStatusChanged(string status)
        {
            Dispatcher.Invoke(() =>
            {
                MQTTConnectionStatus.Text = status; // Ensure MQTTConnectionStatus is the correct UI element's name
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
        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                Log.Information("System is waking up from sleep. Re-establishing connections...");
                // Implement logic to re-establish connections
                ReestablishConnections();
            }
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
            string currentTheme = _settings.Theme; // Assuming this is where the theme is stored
            var aboutWindow = new AboutWindow(deviceid, MyNotifyIcon);
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

                // Copy values to local variables after they have been updated
                localCpuUsage = metrics.CpuUsage;
                localMemoryUsage = metrics.MemoryUsage;
                localTotalRam = metrics.TotalRam;
                localFreeRam = metrics.FreeRam;
                localUsedRam = metrics.UsedRam;
            });

            
            metrics.CpuUsage = localCpuUsage;
           
            // Pass the updated singleton instance to your MQTT service for publishing
            _mqttService?.UpdatePCMetrics(metrics);
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
            _updateTimer.Elapsed -= OnTimedEvent; // Unsubscribe from the Elapsed event
            _updateTimer.Stop(); // Stop the timer
            _updateTimer.Dispose(); // Dispose of the timer
            // Unsubscribe from events and clean up
            if (_mqttService != null)
            {
                _mqttService.ConnectionStatusChanged -= MqttManager_ConnectionStatusChanged;
                _mqttService.StatusUpdated -= UpdateMqttStatus;

            }
            
            // we want all the sensors to be off if we are exiting, lets initialise them, to do this
            await _mqttService.SetupMqttSensors();
            if (_mqttService != null)
            {
                await _mqttService.DisconnectAsync(); // Properly disconnect before disposing
                _mqttService.Dispose();
                Log.Debug("MQTT Client Disposed");
            }
            MyNotifyIcon.Dispose();
            base.OnClosing(e); // Call the base class method
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
        private void UpdateStatusMenuItems()
        {
            Dispatcher.Invoke(() =>
            {
                // Update MQTT connection status text
                MQTTConnectionStatus.Text = _mqttService != null && _mqttService.IsConnected ? "MQTT Status: Connected" : "MQTT Status: Disconnected";
                // Update menu items
                _mqttStatusMenuItem.Header = MQTTConnectionStatus.Text; // Reuse the text set above
                
                // Add other status updates here as necessary
            });
        }
        private async void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("SaveSettings_Click: Save Settings Clicked" + _settings.ToString());

            await SaveSettingsAsync();
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
                await _mqttService.UnsubscribeAsync("homeassistant/switch/+/set");
                await _mqttService.DisconnectAsync();
                await _mqttService.UpdateSettingsAsync(settings); // Make sure to pass the updated settings
                await _mqttService.ConnectAsync();
                await _mqttService.SubscribeAsync("homeassistant/switch/+/set", MqttQualityOfServiceLevel.AtLeastOnce);
            }
        }
        private async void ReestablishConnections()
        {
            try
            {
                if (!_mqttService.IsConnected)
                {
                    await _mqttService.ConnectAsync();
                    await _mqttService.SubscribeAsync("homeassistant/switch/+/set", MqttQualityOfServiceLevel.AtLeastOnce);
                    //await _mqttService.SetupMqttSensors();
                    Dispatcher.Invoke(() => UpdateStatusMenuItems());
                }

            }
            catch (Exception ex)
            {
                Log.Error($"Error re-establishing connections: {ex.Message}");
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