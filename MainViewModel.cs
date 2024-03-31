using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;

namespace PC2MQTT
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private string _mqttStatus = "Initializing...";
        private double _cpuUsage;
        private double _usedMemoryPercentage;
        public string MqttStatus
        {
            get => _mqttStatus;
            set
            {
                if (_mqttStatus != value)
                {
                    _mqttStatus = value;
                    OnPropertyChanged(nameof(MqttStatus));
                }
            }
        }
        public double CpuUsage
        {
            get => _cpuUsage;
            set
            {
                if (_cpuUsage != value)
                {
                    _cpuUsage = value;
                    OnPropertyChanged(nameof(CpuUsage));
                }
            }
        }

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

        public event PropertyChangedEventHandler PropertyChanged;

  

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
