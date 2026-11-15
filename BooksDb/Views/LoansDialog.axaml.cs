using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BooksDb.Data;
using BooksDb.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BooksDb.Views;

public partial class LoansDialog : Window
{
    private readonly MainViewModel _vm = null!;
    private readonly LoanRepository _loans = null!;
    private readonly long _bookId;
    private bool _isOut;

    public LoansDialog() => InitializeComponent();   // designer

    public LoansDialog(MainViewModel vm, LoanRepository loans, long bookId, string title)
    {
        InitializeComponent();
        _vm     = vm;
        _loans  = loans;
        _bookId = bookId;
        HeaderText.Text = $"Loans — {title}";
        Reload();
    }

    private void Reload()
    {
        var items = _loans.GetHistory(_bookId);

        HistoryGrid.IsVisible = items.Count > 0;
        EmptyText.IsVisible   = items.Count == 0;
        HistoryGrid.ItemsSource = items.Count > 0 ? items : null;

        var open = items.FirstOrDefault(i => string.IsNullOrWhiteSpace(i.DateReturned));
        _isOut = open is not null;

        StatusText.Text      = _isOut ? $"Lent to {open!.Borrower} since {open.DateLent}" : "Available";
        ActionButton.Content = _isOut ? "Return…" : "Lend…";
    }

    private async void OnAction(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_isOut)
            {
                var req = await ReturnDialog.ShowAsync(this);
                if (req is null) return;
                _vm.Return(_bookId, req);
            }
            else
            {
                var req = await LendDialog.ShowAsync(this);
                if (req is null) return;
                _vm.Lend(_bookId, req);
            }
        }
        catch (Exception ex)
        {
            await MessageDialog.InfoAsync(this, _isOut ? "Return failed" : "Lend failed", ex.Message);
            return;
        }
        Reload();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    public static Task ShowAsync(Window owner, MainViewModel vm, long bookId, string title)
    {
        var loans = App.Services.GetRequiredService<LoanRepository>();
        return new LoansDialog(vm, loans, bookId, title).ShowDialog(owner);
    }
}