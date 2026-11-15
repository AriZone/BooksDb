using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BooksDb.Data;
using BooksDb.Data.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BooksDb.Views;

public partial class BookPickerDialog : Window
{
    private readonly BookRepository _books = null!;

    public BookPickerDialog() => InitializeComponent();   // designer

    public BookPickerDialog(BookRepository books)
    {
        InitializeComponent();
        _books = books;
        SearchBox.TextChanged += (_, _) => Reload();
        BooksGrid.DoubleTapped += (_, _) =>
        {
            if (BooksGrid.SelectedItem is BookListItem b) Close(b);
        };
        Reload();
    }

    private void Reload() => BooksGrid.ItemsSource = _books.GetAll(SearchBox.Text, null);

    private void OnSelect(object? sender, RoutedEventArgs e)
    {
        if (BooksGrid.SelectedItem is BookListItem b) Close(b);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    public static Task<BookListItem?> ShowAsync(Window owner)
    {
        var books = App.Services.GetRequiredService<BookRepository>();
        return new BookPickerDialog(books).ShowDialog<BookListItem?>(owner);
    }
}