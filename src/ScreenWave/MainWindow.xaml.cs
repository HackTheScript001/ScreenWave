using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScreenWave.Services;
using ScreenWave.Views;

namespace ScreenWave;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _pages = [];

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = $"v{AppInfo.VersionText}  ·  © {DateTime.Now.Year} SecondWave IT Systems";
        Log.LineWritten += line => Dispatcher.InvokeAsync(() =>
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        });
        Tv.ConnectionChanged += () => Dispatcher.InvokeAsync(UpdateConnection);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ShowPage("connect");
        UpdateConnection();
        var locked = await FirewallLock.IsOnAsync();
        FirewallText.Text = locked ? "✓ Firewall lock is on" : "Firewall lock is off – reinstall to add it";
        FirewallText.Foreground = (Brush)FindResource(locked ? "Good" : "Warn");
        FirewallText.ToolTip = locked
            ? "Windows Firewall blocks ScreenWave and sdb from every internet address."
            : "The installer adds Windows Firewall rules that keep ScreenWave off the internet.";
        if (!Sdb.IsAvailable)
            Log.Write($"Warning: sdb.exe is missing from {Sdb.ExePath}. Reinstall ScreenWave.");
    }

    private void OnNav(object sender, RoutedEventArgs e)
    {
        if (PageHost == null) return;
        if (sender is RadioButton { Tag: string key }) ShowPage(key);
    }

    public void GoTo(string key)
    {
        foreach (var rb in FindNav(this))
            if (rb.Tag as string == key) { rb.IsChecked = true; return; }
        ShowPage(key);
    }

    private static IEnumerable<RadioButton> FindNav(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is RadioButton rb && rb.GroupName == "nav") yield return rb;
            foreach (var x in FindNav(child)) yield return x;
        }
    }

    private void ShowPage(string key)
    {
        if (!_pages.TryGetValue(key, out var page))
        {
            page = key switch
            {
                "apps" => new AppsView(),
                "tips" => new TipsView(),
                "block" => new BlockListView(),
                "finish" => new FinishView(),
                "about" => new AboutView(),
                "settings" => new SettingsView(),
                _ => new ConnectView(),
            };
            _pages[key] = page;
        }
        PageHost.Content = page;
    }

    private void UpdateConnection()
    {
        if (Tv.Connected)
        {
            ConnDot.Foreground = (Brush)FindResource("Green");
            ConnText.Text = " TV connected";
            ConnDetail.Text = Tv.Serial + (Tv.Details.Length > 0 ? "\n" + Tv.Details : "");
        }
        else
        {
            ConnDot.Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
            ConnText.Text = " No TV connected";
            ConnDetail.Text = "";
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (Busy.IsBusy &&
            MessageBox.Show(this, "ScreenWave is still working on the TV. Close anyway?", "ScreenWave",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
        }
    }
}

/// <summary>Whether a page is mid-job, so closing can warn first.</summary>
public static class Busy
{
    private static int _count;
    public static bool IsBusy => _count > 0;
    public static void Enter() => Interlocked.Increment(ref _count);
    public static void Exit() { if (Interlocked.Decrement(ref _count) < 0) _count = 0; }
}
