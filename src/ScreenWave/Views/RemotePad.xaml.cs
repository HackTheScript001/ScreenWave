using System.Windows;
using System.Windows.Controls;
using ScreenWave.Services;

namespace ScreenWave.Views;

/// <summary>On-screen TV remote. Each press goes to the TV and is reported, so it can be recorded.</summary>
public partial class RemotePad : UserControl
{
    public event Action<string>? Pressed;

    public RemotePad()
    {
        InitializeComponent();
    }

    private async void OnPad(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key } || !TvRemote.Connected) return;
        try
        {
            await TvRemote.PressAsync(key);
            Pressed?.Invoke(key);
        }
        catch (Exception ex)
        {
            Log.Write($"Couldn't press {TvRemote.Label(key)}: {ex.Message}");
        }
    }
}
