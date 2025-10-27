using System;
using System.Collections.Generic;

namespace PC2MQTT.api
{
    public class PCSensor
    {
        public string Name { get; set; }
        public string Value { get; set; } // General value; used for different types of sensors
    }

    public class PCMetrics
    {
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public double TotalRam { get; set; }
        public double FreeRam { get; set; }
        public double UsedRam { get; set; }
        public string PowerState { get; set; } = "on";

        // Singleton instance
        private static PCMetrics _instance;

        // Lock object for thread safety
        private static readonly object _lock = new object();

        // Private constructor to prevent external instantiation
        private PCMetrics() { }

        // Public method to get the singleton instance
        public static PCMetrics Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new PCMetrics();
                    }
                    return _instance;
                }
            }
        }
    }



    public class DiskMetric
    {
        public string Name { get; set; } // Disk identifier, e.g., "C:", "D:"
        public double Usage { get; set; } // Disk usage in percentage or another unit
        public double FreeSpace { get; set; } // Free space available
        public double TotalSpace { get; set; } // Total disk space
    }
}
