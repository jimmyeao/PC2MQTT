using System;
using System.Collections.Generic;

namespace PC2MQTT
{
    public class PCSensor
    {
        public string Name { get; set; }
        public string Value { get; set; } // General value; used for different types of sensors
    }

    public class PCMetrics
    {
        public double CpuUsage { get; set; } // Represents total CPU usage
        public double MemoryUsage { get; set; } // Represents total memory usage
        public List<DiskMetric> Disks { get; set; } = new List<DiskMetric>(); // List to hold information for all disks
        public double NetworkUsage { get; set; } // Represents total network usage
        public double Temperature { get; set; } // General temperature, could be CPU or system
        public double FanSpeed { get; set; } // General fan speed, could be CPU or system
    }

    public class DiskMetric
    {
        public string Name { get; set; } // Disk identifier, e.g., "C:", "D:"
        public double Usage { get; set; } // Disk usage in percentage or another unit
        public double FreeSpace { get; set; } // Free space available
        public double TotalSpace { get; set; } // Total disk space
    }
}
