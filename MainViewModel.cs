using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PC2MQTT
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private string _mqttStatus = "Initializing...";
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

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
