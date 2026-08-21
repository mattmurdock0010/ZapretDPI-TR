using System.Windows;
using System.Windows.Input;

namespace ZapretDPI.Views
{
    public partial class InputDialog : Window
    {
        public string InputText => TxtInput.Text;

        public InputDialog(string title, string message, string defaultText = "")
        {
            InitializeComponent();
            LblTitle.Text = title;
            LblMessage.Text = message;
            TxtInput.Text = defaultText;

            BtnOk.Content = ZapretDPI.Services.LocalizationService.Get("Btn_Ok") ?? "Tamam";
            BtnCancel.Content = ZapretDPI.Services.LocalizationService.Get("Btn_Cancel") ?? "İptal";
            
            Loaded += (s, e) => {
                TxtInput.Focus();
                TxtInput.SelectAll();
            };
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtInput.Text))
            {
                var msg = ZapretDPI.Services.LocalizationService.Get("Msg_ProfileNameEmpty");
                if (msg == "Msg_ProfileNameEmpty" || string.IsNullOrEmpty(msg))
                    msg = "Lütfen geçerli bir profil ismi girin.";
                    
                DarkMessageBox.Show(msg, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
