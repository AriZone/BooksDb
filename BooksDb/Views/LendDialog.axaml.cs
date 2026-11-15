using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BooksDb.Data;
using BooksDb.Data.Models;
using BooksDb.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BooksDb.Views;

public partial class LendDialog : Window
{
    private readonly BorrowerRepository _borrowers = null!;

    public LendDialog() => InitializeComponent();   // designer

    public LendDialog(BorrowerRepository borrowers)
    {
        InitializeComponent();
        _borrowers = borrowers;
        BorrowerBox.AsyncPopulator = PopulateBorrowersAsync;
        LendDatePicker.SelectedDate = DateTime.Today;
    }

    private async Task<IEnumerable<object>> PopulateBorrowersAsync(string? text, CancellationToken ct)
        => string.IsNullOrWhiteSpace(text)
            ? Array.Empty<object>()
            : await Task.Run(() => _borrowers.Search(text), ct);

    private void OnLend(object? sender, RoutedEventArgs e)
    {
        var date  = LendDatePicker.SelectedDate ?? DateTime.Today;
        var notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text!.Trim();

        if (BorrowerBox.SelectedItem is LookupItem picked)
        {
            Close(new LendRequest { BorrowerId = picked.Id, DateLent = date, Notes = notes });
            return;
        }

        var first = NewFirstBox.Text?.Trim();
        var last  = NewLastBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last))
            return;   // need an existing borrower, or both names for a new one

        Close(new LendRequest { FirstName = first, LastName = last, DateLent = date, Notes = notes });
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    public static Task<LendRequest?> ShowAsync(Window owner)
        => new LendDialog(App.Services.GetRequiredService<BorrowerRepository>()).ShowDialog<LendRequest?>(owner);
}