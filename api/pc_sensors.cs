﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Serilog;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PC2MQTT.api
{
    public class pc_sensors
    {
        private AppSettings _settings; 
        private List<string> _sensorNames;
        private PCMetrics _pcMetrics;
        public pc_sensors(AppSettings settings)
        {
            _sensorNames = new List<string>
            {
                "cpu_usage",
                "memory_usage",
                // Add all other sensor names here
                "total_ram",
                "free_ram",
                "used_ram"
            };
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        }
         public void PerformShutdown()
        {
            if (_settings.UseSafeCommands)
            {
                // Use Dispatcher to ensure the dialog is opened on the UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
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

        public void PerformStandby()
        {
            // System-specific command to standby
            // Windows does not have a direct standby command, typically handled by hardware
            if (_settings.UseSafeCommands)
            {
                // Use Dispatcher to ensure the dialog is opened on the UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    ConfirmationDialog dialog = new ConfirmationDialog("Standby Confirmation", "Are you sure you want to put the computer to standby?");
                    if (dialog.ShowDialog() == true)
                    {
                        Log.Information("User confirmed the standby command.");
                        System.Windows.Forms.Application.SetSuspendState(PowerState.Suspend, true, true);
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
                System.Windows.Forms.Application.SetSuspendState(PowerState.Suspend, true, true);
            }
        }
        public void PerformReboot()
        {
            // System-specific command to reboot
            if (_settings.UseSafeCommands)
            {
                // Use Dispatcher to ensure the dialog is opened on the UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
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

        public void PerformHibernate()
        {
            // System-specific command to hibernate
            if (_settings.UseSafeCommands)
            {
                // Use Dispatcher to ensure the dialog is opened on the UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
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
        public List<string> GetSensorAndSwitchNames(string _deviceId)
        {
            // Assuming you have a method or way to get all sensor and switch names
            // For example, combining predefined names with dynamic ones from settings
            List<string> names = new List<string>();

            foreach (var sensor in _sensorNames)  // Assuming _sensorNames contains names of your sensors
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
        public void UpdatePCMetrics(PCMetrics metrics)
        {
            _pcMetrics = metrics;  // Make sure this assignment happens correctly

            // Debug logging
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
    }
}
