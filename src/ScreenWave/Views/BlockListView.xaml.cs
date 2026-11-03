using System.Windows;
using System.Windows.Controls;

namespace ScreenWave.Views;

public partial class BlockListView : UserControl
{
    // Samsung's app-store servers the TV uses to (re)install preloaded apps
    private static readonly string[] Domains =
    [
        "osb-v2.samsungqbe.com",
        "osb-apps.samsungqbe.com",
        "osb-apps-v2.samsungqbe.com",
        "osb-auth-eusvc-v2.samsungqbe.com",
    ];

    public BlockListView()
    {
        InitializeComponent();
        DomainBox.Text = string.Join(Environment.NewLine, Domains);
    }

    private void Copy(string text, string what)
    {
        try
        {
            Clipboard.SetText(text);
            CopiedText.Text = $"Copied {what} – paste it into your blocker.";
        }
        catch
        {
            CopiedText.Text = "Couldn't reach the clipboard – select the addresses above and copy them instead.";
        }
    }

    private void OnCopyAdGuard(object sender, RoutedEventArgs e) =>
        Copy(string.Join(Environment.NewLine, Domains.Select(d => $"||{d}^")), "AdGuard Home rules");

    private void OnCopyHosts(object sender, RoutedEventArgs e) =>
        Copy(string.Join(Environment.NewLine, Domains.Select(d => $"0.0.0.0 {d}")), "hosts-file lines");

    private void OnCopyPlain(object sender, RoutedEventArgs e) =>
        Copy(string.Join(Environment.NewLine, Domains), "the addresses");
}
