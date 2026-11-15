using System;
using Avalonia;
using Avalonia.Media.Fonts;

namespace BooksDb.Desktop;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.ServicesReady = CaptureServer.Start;   // desktop head starts the LAN capture listener
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            CaptureServer.Stop();
        }

        Environment.Exit(0);   // belt-and-suspenders: ensure the process terminates after shutdown
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .ConfigureFonts(fm => fm.AddFontCollection(
                new EmbeddedFontCollection(
                    new Uri("fonts:BooksDb", UriKind.Absolute),
                    new Uri("avares://BooksDb/Assets/Fonts", UriKind.Absolute))))
            .LogToTrace();
}