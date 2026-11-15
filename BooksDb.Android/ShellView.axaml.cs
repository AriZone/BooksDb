using System;
using Android.App;
using Android.Content;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BooksDb.Android;

public partial class ShellView : UserControl
{
    public ShellView() => InitializeComponent();

    // Native confirm dialog: an Avalonia overlay can't reliably sit above the live camera surface
    // (a NativeControlHost), so use a real Android AlertDialog, which renders in its own window.
    private void OnClearBuffer(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        var activity = MainActivity.Current;
        if (activity is null) return;

        var b = new AlertDialog.Builder(activity);
        b.SetTitle("Delete all pictures?");
        b.SetMessage("This removes every photo from the buffer.");
        b.SetPositiveButton("Yes", (EventHandler<DialogClickEventArgs>)((_, _) => vm.Helper.ClearBuffer()));
        b.SetNegativeButton("No", (EventHandler<DialogClickEventArgs>)((_, _) => { }));
        b.Show();
    }
}