using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BooksDb.Data;
using BooksDb.ViewModels;
using BooksDb.Views;
using Microsoft.Extensions.DependencyInjection;

namespace BooksDb;

public class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>Fired after DI is built and the DB is migrated. The desktop head uses this to start
    /// the LAN capture listener, keeping that dependency out of shared/Android code.</summary>
    public static Action<IServiceProvider>? ServicesReady;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Services = new ServiceCollection()
            .AddBooksDbServices()
            .BuildServiceProvider();

        // ensure DB exists / is migrated before any ViewModel reads it
        // honour a previously-chosen database before anything reads it
        var savedDb = Services.GetRequiredService<SettingsStore>().Current.DatabasePath;
        if (!string.IsNullOrWhiteSpace(savedDb))
            Services.GetRequiredService<Database>().SwitchTo(savedDb);

        Migrator.EnsureUpToDate(Services.GetRequiredService<Database>());

        // platform head (desktop) can now start optional services, e.g. the capture listener
        ServicesReady?.Invoke(Services);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainViewModel>()
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new MainView
            {
                DataContext = Services.GetRequiredService<MainViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}