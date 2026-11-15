using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BooksDb.Views;

public enum MessageChoice { Cancel = 0, Primary, Secondary, Tertiary }   // Cancel = default, so Esc / window-close => Cancel

public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    private void OnPrimary(object? sender, RoutedEventArgs e)   => Close(MessageChoice.Primary);
    private void OnSecondary(object? sender, RoutedEventArgs e) => Close(MessageChoice.Secondary);
    private void OnTertiary(object? sender, RoutedEventArgs e)  => Close(MessageChoice.Tertiary);
    private void OnCancel(object? sender, RoutedEventArgs e)    => Close(MessageChoice.Cancel);

    // Two-button yes/no. Returns true for Yes; false for No or closed.
    public static async Task<bool> ConfirmAsync(Window owner, string title, string message)
        => await Show(owner, title, message, "Yes", "No", null, null) == MessageChoice.Primary;

    // Single-button notice. Always returns true.
    public static async Task<bool> InfoAsync(Window owner, string title, string message)
    {
        await Show(owner, title, message, "OK", null, null, null);
        return true;
    }

    // Three- or four-way choice with caller-supplied labels. Esc / window-close => Cancel.
    public static Task<MessageChoice> ChoiceAsync(Window owner, string title, string message,
        string primary, string secondary, string? tertiary = null, string cancel = "Cancel")
        => Show(owner, title, message, primary, secondary, tertiary, cancel);

    private static Task<MessageChoice> Show(Window owner, string title, string message,
        string primary, string? secondary, string? tertiary, string? cancel)
    {
        var dlg = new MessageDialog { Title = title };
        dlg.MessageText.Text      = message;
        dlg.PrimaryButton.Content = primary;

        dlg.SecondaryButton.IsVisible = secondary is not null;
        if (secondary is not null) dlg.SecondaryButton.Content = secondary;

        dlg.TertiaryButton.IsVisible = tertiary is not null;
        if (tertiary is not null) { dlg.TertiaryButton.Content = tertiary; dlg.Width = 560; }   // room for a 4th button

        dlg.CancelButton.IsVisible = cancel is not null;
        if (cancel is not null) dlg.CancelButton.Content = cancel;

        return dlg.ShowDialog<MessageChoice>(owner);
    }
}
