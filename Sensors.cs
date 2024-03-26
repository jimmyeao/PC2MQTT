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
        public double MemoryUsage { get; set; } // Represents used memory in percentage
        public double MemoryFree { get; set; } // Represents free memory in MB
        public double TotalMemory { get; set; } // Represents total memory in MB
        public List<DiskMetric> Disks { get; set; } = new List<DiskMetric>(); // Information for all disks
    }


    public class DiskMetric
    {
        public string Name { get; set; } // Disk identifier, e.g., "C:", "D:"
        public double Usage { get; set; } // Disk usage in percentage or another unit
        public double FreeSpace { get; set; } // Free space available
        public double TotalSpace { get; set; } // Total disk space
    }
}
