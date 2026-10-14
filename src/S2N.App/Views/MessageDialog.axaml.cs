using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using S2N.Localization;

namespace S2N.Views;

public enum MessageDialogKind
{
    Information,
    Success,
    Warning,
    Error
}

public partial class MessageDialog : Window
{
    public MessageDialog()
        : this("", "", MessageDialogKind.Information)
    {
    }

    public MessageDialog(
        string title,
        string message,
        MessageDialogKind kind)
    {
        InitializeComponent();

        Title = title;
        TitleText.Text = title;
        BodyText.Text = message;
        CloseButton.Content = L10n.T("确定", "OK");

        var (icon, foreground, background) = kind switch
        {
            MessageDialogKind.Success =>
                ("✓", "#167A45", "#1A2BAE66"),
            MessageDialogKind.Warning =>
                ("!", "#A15C00", "#1AF2A100"),
            MessageDialogKind.Error =>
                ("×", "#C24545", "#1AC24545"),
            _ =>
                ("i", "#2F6FED", "#1A2F6FED")
        };

        IconText.Text = icon;
        IconText.Foreground = new SolidColorBrush(Color.Parse(foreground));
        IconBorder.Background = new SolidColorBrush(Color.Parse(background));
    }

    private void Close_Click(object? sender, RoutedEventArgs e) =>
        Close();
}
