using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BooksDb.Views;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();

        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        var product = asm.GetCustomAttribute<AssemblyProductAttribute>()?.Product
                      ?? asm.GetName().Name ?? "BooksDb";

        var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? asm.GetName().Version?.ToString() ?? "—";
        var plus = version.IndexOf('+');           // strip "+<git-hash>" if SourceLink adds it
        if (plus >= 0) version = version[..plus];

        var built = asm.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildTimestamp")?.Value ?? "—";

        NameText.Text    = product;
        VersionText.Text = $"Version {version}";
        BuiltText.Text   = $"Compiled: {built} UTC";
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
    public static Task ShowAsync(Window owner) => new AboutDialog().ShowDialog(owner);
}