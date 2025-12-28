using System.Windows;
using System.Windows.Input;

namespace ConditioningControlPanel
{
    public partial class InputDialog : Window
    {
        public string ResultText { get; private set; } = "";

        public InputDialog(string title, string prompt, string defaultValue = "")
        {
            InitializeComponent();
            
            TxtTitle.Text = title;
            TxtPrompt.Text = prompt;
            TxtInput.Text = defaultValue;
            
            Loaded += (s, e) =>
            {
                TxtInput.Focus();
                TxtInput.SelectAll();
            };
        }

        private void TxtInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Accept();
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            Accept();
        }

        private void Accept()
        {
            ResultText = TxtInput.Text;
            DialogResult = true;
            Close();
        }
    }
}
