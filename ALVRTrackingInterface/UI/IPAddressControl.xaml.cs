using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Controls;

namespace ALXRControls
{
    /// <summary>
    /// Interaction logic for IPAddressControl.xaml
    /// </summary>
    public partial class IPAddressControl : UserControl
    {
        public IPAddressControl()
        {
            InitializeComponent();
        }
        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = (TextBox)sender;
            var text = textBox.Text;

            // Remove any non-digit characters
            var regex = new Regex("[^0-9]+");
            text = regex.Replace(text, "");

            // Limit the value to 255
            if (int.TryParse(text, out int value))
            {
                value = Math.Min(value, 255);
                text = value.ToString();
            }

            // Update the TextBox text
            textBox.Text = text;

            // Move focus to the next TextBox if this one is full
            if (text.Length == 3 && textBox != part4)
            {
                try
                {
                    var nextIndex = Char.GetNumericValue(textBox.Name.Last()) + 1;
                    var nextStr = $"part{nextIndex}";
                    var next = (TextBox)textBox.FindName(nextStr);
                    next.Focus();
                    next.SelectAll();
                } catch (Exception) { }
            }
        }

        // Expose the IP address as a property
        public IPAddress IPAddress
        {
            get
            {
                var bytes = new byte[4];
                bytes[0] = byte.Parse(part1.Text);
                bytes[1] = byte.Parse(part2.Text);
                bytes[2] = byte.Parse(part3.Text);
                bytes[3] = byte.Parse(part4.Text);
                return new IPAddress(bytes);
            }
            set
            {
                var bytes = value.GetAddressBytes();
                part1.Text = bytes[0].ToString();
                part2.Text = bytes[1].ToString();
                part3.Text = bytes[2].ToString();
                part4.Text = bytes[3].ToString();
            }
        }

        public void Reset()
        {
            IPAddress = new IPAddress(new byte[] { 127, 0, 0, 1 });
        }
    }
}
