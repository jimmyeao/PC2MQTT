using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PC2MQTT
{
    /// <summary>
    /// Interaction logic for ConfirmationDialog.xaml
    /// </summary>
    public partial class ConfirmationDialog : Window
    {
        public ConfirmationDialog(string title, string message)
        {
            InitializeComponent();
            this.Title = title;
            this.txtMessage.Text = message;
        }

        private void btnOk_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true; // User confirmed
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false; // User cancelled
        }
    }

}
