using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace BooksDb.Android;

public partial class App : Avalonia.Application   // qualify: 'Application' alone means the Android class here
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Android is a single-view app: no window, just a root view.
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var shell = new ShellViewModel();
            singleView.MainView = new ShellView { DataContext = shell };
            MainActivity.BackRequested = shell.TryHandleBack;   // hardware back → page navigation
        }

        base.OnFrameworkInitializationCompleted();
    }
}