using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BooksDb.ViewModels;

namespace BooksDb.Views;

public partial class ReturnDialog : Window
{
    public ReturnDialog()
    {
        InitializeComponent();
        ReturnDatePicker.SelectedDate = DateTime.Today;
    }

    private void OnReturn(object? sender, RoutedEventArgs e)
        => Close(new ReturnRequest
        {
            DateReturned = ReturnDatePicker.SelectedDate ?? DateTime.Today,
            Notes        = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text!.Trim(),
        });

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    public static Task<ReturnRequest?> ShowAsync(Window owner)
        => new ReturnDialog().ShowDialog<ReturnRequest?>(owner);
}