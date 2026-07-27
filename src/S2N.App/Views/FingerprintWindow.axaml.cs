using Avalonia.Controls;
using Avalonia.Interactivity;

namespace S2N.Views;

public partial class FingerprintWindow : Window
{
    public FingerprintWindow(string fingerprint)
    {
        InitializeComponent();
        FingerprintBox.Text = fingerprint;
    }

    private void Trust_Click(object? sender, RoutedEventArgs e) =>
        Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) =>
        Close(false);
}
