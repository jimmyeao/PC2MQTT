using Microsoft.Win32;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using MQTTnet.Protocol;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Controls;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;


namespace PC2MQTT
{
    public class AppSettings
    {
        #region Private Fields

        // Lock object for thread-safe initialization
        private static readonly object _lock = new object();

        private static readonly string _settingsFilePath;

        // Static variable for the singleton instance
        private static AppSettings _instance;

        private string _mqttPassword; // Store the encrypted version internally
        private string _teamsToken; // Store the encrypted version internally

        #endregion Private Fields

        #region Public Constructors

        // Static constructor to set up file path
        static AppSettings()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDataFolder = Path.Combine(localAppData, "PC2MQTT");
            Directory.CreateDirectory(appDataFolder);
            _settingsFilePath = Path.Combine(appDataFolder, "settings.json");
        }

        #endregion Public Constructors

        #region Private Constructors

        // Private constructor to prevent direct instantiation
        private AppSettings()
        {
            LoadSettingsFromFile();
        }

        #endregion Private Constructors

        #region Public Properties
        public bool HasShownOneTimeNotice { get; set; } = false;

        // Public property to access the singleton instance
        public static AppSettings Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new AppSettings();
                    }
                    return _instance;
                }
            }
        }

        // Properties
        public string EncryptedMqttPassword
        {
            get => _mqttPassword;
            set => _mqttPassword = value; // Only for deserialization
        }

        public string EncryptedTeamsToken
        {
            get => _teamsToken;
            set => _teamsToken = value; // Only for deserialization
        }

        public bool IgnoreCertificateErrors { get; set; }

        public string MqttAddress { get; set; }

        [JsonIgnore]
        public string MqttPassword
        {
            get => CryptoHelper.DecryptString(_mqttPassword);
            set => _mqttPassword = CryptoHelper.EncryptString(value);
        }

        public string MqttPort { get; set; }
        public string MqttUsername { get; set; }

        public bool UseSafeCommands { get; set; }

        public bool RunAtWindowsBoot { get; set; }
        public bool RunMinimized { get; set; }
        public string SensorPrefix { get; set; }
        public static string ExecutablePath { get; }
        public string Theme { get; set; }
        public bool UseTLS { get; set; }
        public bool UseWebsockets { get; set; }

        #endregion Public Properties

        #region Public Methods

        // Save settings to file
        public void SaveSettingsToFile()
        {
            // Encrypt sensitive data
            if (!String.IsNullOrEmpty(this.MqttPassword))
            {
                this.EncryptedMqttPassword = CryptoHelper.EncryptString(this.MqttPassword);
            }
            else
            {
                this.EncryptedMqttPassword = "";
            }
           
            if (string.IsNullOrEmpty(this.SensorPrefix))
            {
                this.SensorPrefix = System.Environment.MachineName;
            }
            // newcode

            const string appName = "PC2MQTT"; // Your application's name
            string exePath = ExecutablePath;

            // Open the registry key for the current user's startup programs
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (this.RunAtWindowsBoot)
                {
                    // Set the application to start with Windows startup by adding a registry value
                    key.SetValue(appName, exePath);
                }
                else
                {
                    // Remove the registry value to prevent the application from starting with
                    // Windows startup
                    key.DeleteValue(appName, false);
                }
            }

            Log.Debug("SetStartupAsync: Startup options set");
            // Serialize and save
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(_settingsFilePath, json);
        }

        #endregion Public Methods

        #region Private Methods

        // Load settings from file
        private void LoadSettingsFromFile()
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                JsonConvert.PopulateObject(json, this);

                // Decrypt sensitive data
                if (!String.IsNullOrEmpty(this.EncryptedMqttPassword))
                {
                    this.MqttPassword = CryptoHelper.DecryptString(this.EncryptedMqttPassword);
                }
               
                if (string.IsNullOrEmpty(this.MqttPort))
                {
                    this.MqttPort = "1883"; // Default MQTT port
                }
            }
            else
            {
                this.MqttPort = "1883"; // Default MQTT port
            }
        }

        #endregion Private Methods
    }
}
