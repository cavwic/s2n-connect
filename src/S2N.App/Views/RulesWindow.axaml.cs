using Avalonia.Controls;
using Avalonia.Interactivity;

namespace S2N.Views;

public partial class RulesWindow : Window
{
    public List<string> ProcessNames { get; private set; }
    public List<string> DomainSuffixes { get; private set; }

    public RulesWindow(
        IEnumerable<string> processes,
        IEnumerable<string> domains)
    {
        InitializeComponent();
        ProcessNames = processes.ToList();
        DomainSuffixes = domains.ToList();
        ProcessesBox.Text = string.Join(
            Environment.NewLine,
            ProcessNames);
        DomainsBox.Text = string.Join(
            Environment.NewLine,
            DomainSuffixes);
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        ProcessNames = SplitLines(ProcessesBox.Text);
        DomainSuffixes = SplitLines(DomainsBox.Text);
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) =>
        Close(false);

    private static List<string> SplitLines(string? value) =>
        (value ?? "")
        .Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Trim())
        .Where(x => x.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
