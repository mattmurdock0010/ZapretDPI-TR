using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ZapretDPI.Views;

public partial class DarkMessageBox : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    public DarkMessageBox(string message, string title, MessageBoxButton button, MessageBoxImage icon)
    {
        InitializeComponent();

        TxtTitle.Text = title;
        TxtMessage.Text = message;

        BtnOk.Content = Services.LocalizationService.Get("Btn_Ok");
        BtnCancel.Content = Services.LocalizationService.Get("Btn_Cancel");
        BtnYes.Content = Services.LocalizationService.Get("Btn_Yes");
        BtnNo.Content = Services.LocalizationService.Get("Btn_No");

        switch (icon)
        {
            case MessageBoxImage.Information:
                IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));
                IconSymbol.Text = "ℹ";
                break;
            case MessageBoxImage.Warning:
                IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                IconSymbol.Text = "⚠";
                break;
            case MessageBoxImage.Error:
                IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                IconSymbol.Text = "✕";
                break;
            default:
                IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                IconSymbol.Text = "✓";
                break;
        }

        switch (button)
        {
            case MessageBoxButton.OK:
                BtnOk.Visibility = Visibility.Visible;
                BtnOk.IsDefault = true;
                break;

            case MessageBoxButton.OKCancel:
                BtnOk.Visibility = Visibility.Visible;
                BtnCancel.Visibility = Visibility.Visible;
                BtnOk.IsDefault = true;
                BtnCancel.IsCancel = true;
                break;

            case MessageBoxButton.YesNo:
                BtnOk.Visibility = Visibility.Collapsed;
                BtnYes.Visibility = Visibility.Visible;
                BtnNo.Visibility = Visibility.Visible;
                BtnYes.IsDefault = true;
                BtnNo.IsCancel = true;
                break;

            case MessageBoxButton.YesNoCancel:
                BtnOk.Visibility = Visibility.Collapsed;
                BtnYes.Visibility = Visibility.Visible;
                BtnNo.Visibility = Visibility.Visible;
                BtnCancel.Visibility = Visibility.Visible;
                BtnYes.IsDefault = true;
                BtnCancel.IsCancel = true;
                break;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Cancel;
        Close();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.OK;
        Close();
    }

    private void BtnYes_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Yes;
        Close();
    }

    private void BtnNo_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.No;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Cancel;
        Close();
    }

    public static MessageBoxResult Show(string message, string title = "ZapretDPI", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.Information)
    {
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => Show(message, title, button, icon));
        }

        try
        {
            var activeWindow = Application.Current?.MainWindow;
            var dialog = new DarkMessageBox(message, title, button, icon);

            if (activeWindow != null && activeWindow.IsVisible)
            {
                dialog.Owner = activeWindow;
            }

            dialog.ShowDialog();
            return dialog.Result;
        }
        catch
        {
            return MessageBox.Show(message, title, button, icon);
        }
    }
}
