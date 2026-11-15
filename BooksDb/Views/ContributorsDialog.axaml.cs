using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BooksDb.Views;

public partial class ContributorsDialog : Window
{
    public ContributorsDialog() => InitializeComponent();
    private void OnClose(object? sender, RoutedEventArgs e) => Close();
    public static Task ShowAsync(Window owner) => new ContributorsDialog().ShowDialog(owner);
}