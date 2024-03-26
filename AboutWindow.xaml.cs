﻿using Hardcodet.Wpf.TaskbarNotification;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PC2MQTT
{
    /// <summary>
    /// Interaction logic for AboutWindow.xaml
    /// </summary>
    public partial class AboutWindow : Window
    {
        #region Private Fields

        private TaskbarIcon _notifyIcon;
        private MqttService _mqttService;
        #endregion Private Fields

        #region Public Constructors

        public AboutWindow(string deviceId, TaskbarIcon notifyIcon)
        {
            InitializeComponent();

            DataContext = this; // Set DataContext to access CopyCommand

            _notifyIcon = notifyIcon;
            SetVersionInfo();
            //var entityNames = MqttService.GetEntityNames(deviceId);

            //EntitiesListBox.ItemsSource = entityNames;
        }

        #endregion Public Constructors

        #region Private Methods

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void CopyMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is string textToCopy)
            {
                Clipboard.SetText(textToCopy);

                // Show the balloon tip
                _notifyIcon.ShowBalloonTip("Copied to Clipboard", textToCopy + " has been copied to your clipboard.", BalloonIcon.Info);
            }
        }


        private void EntitiesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.ListBox listBox && listBox.SelectedItem != null)
            {
                string selectedEntity = listBox.SelectedItem.ToString();
                System.Windows.Clipboard.SetText(selectedEntity);

                // Show balloon tip
                _notifyIcon.ShowBalloonTip("Copied to Clipboard", selectedEntity + " has been copied to your clipboard.", BalloonIcon.Info);
            }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }

        private void SetVersionInfo()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            this.VersionTextBlock.Text = $"Version: {version}";
        }

        #endregion Private Methods
    }
}
