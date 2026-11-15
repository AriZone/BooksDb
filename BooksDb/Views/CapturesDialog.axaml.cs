using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BooksDb.Data;
using BooksDb.Data.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BooksDb.Views;

public partial class CapturesDialog : Window
{
    private readonly PendingCaptureRepository _captures = null!;

    public CapturesDialog() => InitializeComponent();   // designer

    public CapturesDialog(PendingCaptureRepository captures)
    {
        InitializeComponent();
        _captures = captures;
        Reload();
    }

    private void Reload()
    {
        var items = _captures.GetPendingWithIsbn();
        CapturesGrid.ItemsSource = items;
        CapturesGrid.IsVisible   = items.Count > 0;
        EmptyText.IsVisible      = items.Count == 0;
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => Reload();

    private void OnDiscard(object? sender, RoutedEventArgs e)
    {
        if (CapturesGrid.SelectedItem is PendingCapture c)
        {
            _captures.Discard(c.CaptureId);   // sets status + removes the staged photo files
            Reload();
        }
    }

    private void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (CapturesGrid.SelectedItem is PendingCapture c)
            Close(c);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    public static Task<PendingCapture?> ShowAsync(Window owner)
    {
        var captures = App.Services.GetRequiredService<PendingCaptureRepository>();
        return new CapturesDialog(captures).ShowDialog<PendingCapture?>(owner);
    }
}