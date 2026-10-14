using Avalonia.Controls;
using Avalonia.Interactivity;
using S2N.Localization;

namespace S2N.Views;

public partial class FingerprintWindow : Window
{
    public FingerprintWindow() : this("")
    {
    }

    public FingerprintWindow(string fingerprint)
    {
        InitializeComponent();
        L10n.Apply(this);
        FingerprintBox.Text = fingerprint;
    }

    private void Trust_Click(object? sender, RoutedEventArgs e) =>
        Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) =>
        Close(false);
}
