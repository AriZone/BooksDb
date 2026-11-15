using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BooksDb.Data;
using Microsoft.Extensions.DependencyInjection;

namespace BooksDb.Views;

public partial class SettingsDialog : Window
{
    private readonly SettingsStore _store = null!;

    public SettingsDialog() => InitializeComponent();   // designer

    public SettingsDialog(SettingsStore store)
    {
        InitializeComponent();
        _store = store;
        DeletePicturesCheck.IsChecked = store.Current.DeletePictureFilesOnBookDelete;
        RememberWindowCheck.IsChecked = store.Current.RememberWindow;
        PreviewDelayBox.Text = store.Current.PreviewDelayMs.ToString();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        _store.Current.DeletePictureFilesOnBookDelete = DeletePicturesCheck.IsChecked ?? false;
        _store.Current.RememberWindow = RememberWindowCheck.IsChecked ?? true;
        _store.Current.PreviewDelayMs =
            int.TryParse(PreviewDelayBox.Text, out var ms) && ms >= 0 ? ms : _store.Current.PreviewDelayMs;
        _store.Save();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    public static Task ShowAsync(Window owner)
    {
        var store = App.Services.GetRequiredService<SettingsStore>();
        return new SettingsDialog(store).ShowDialog(owner);
    }
}