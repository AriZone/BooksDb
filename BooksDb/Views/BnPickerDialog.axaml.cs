using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BooksDb.Data.Lookup;

namespace BooksDb.Views;

public partial class BnPickerDialog : Window
{
    public BnPickerDialog() => InitializeComponent();

    public static Task<BnResult?> PickAsync(Window owner, IReadOnlyList<BnResult> results)
    {
        var dlg = new BnPickerDialog();
        dlg.List.ItemsSource  = results;
        dlg.List.SelectedIndex = 0;
        return dlg.ShowDialog<BnResult?>(owner);
    }

    private void OnUse(object? sender, RoutedEventArgs e)    => Close(List.SelectedItem as BnResult);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (List.SelectedItem is BnResult r) Close(r);
    }
}