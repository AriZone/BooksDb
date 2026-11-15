using Avalonia.Controls;
using BooksDb.Data;
using Microsoft.Extensions.DependencyInjection;

namespace BooksDb.Views;

public partial class MainWindow : Window
{
    private readonly SettingsStore _settings;

    public MainWindow()
    {
        InitializeComponent();
        _settings = App.Services.GetRequiredService<SettingsStore>();

        var s = _settings.Current;
        if (s.RememberWindow)
        {
            if (s.WindowWidth is > 0 && s.WindowHeight is > 0)
            {
                Width  = s.WindowWidth.Value;
                Height = s.WindowHeight.Value;
            }
            if (s.WindowMaximized)
                WindowState = WindowState.Maximized;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        var s = _settings.Current;
        if (s.RememberWindow)
        {
            s.WindowMaximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)   // keep last *normal* size; don't capture maximized bounds
            {
                s.WindowWidth  = Width;
                s.WindowHeight = Height;
            }
            _settings.Save();
        }
        base.OnClosing(e);
    }
}