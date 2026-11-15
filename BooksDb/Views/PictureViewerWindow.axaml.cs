using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using BooksDb.Data;

namespace BooksDb.Views;

public partial class PictureViewerWindow : Window
{
    private readonly IReadOnlyList<string> _paths = Array.Empty<string>();
    private readonly string _baseTitle = "Image";
    private int _index;
    private Bitmap? _full;
    private bool _changed;

    public PictureViewerWindow() => InitializeComponent();   // designer ctor

    private PictureViewerWindow(IReadOnlyList<string> paths, int startIndex, string title, bool allowRotate)
    {
        InitializeComponent();
        _paths     = paths;
        _baseTitle = title;
        _index     = startIndex < 0 || startIndex >= paths.Count ? 0 : startIndex;

        bool many = paths.Count > 1;
        PrevBtn.IsEnabled = many;
        NextBtn.IsEnabled = many;

        RotateLeftBtn.IsVisible  = allowRotate;
        RotateRightBtn.IsVisible = allowRotate;

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Load();
    }

    private void Load()
    {
        if (_paths.Count == 0) return;
        var old = _full;
        try
        {
            using var s = ImageOrient.OrientedStream(_paths[_index]);
            _full = new Bitmap(s);                 // full resolution; window scales to fit
            FullImage.Source = _full;
        }
        catch { /* keep the previous image if this one fails to load */ }
        if (!ReferenceEquals(old, _full)) old?.Dispose();

        Title = _baseTitle;
        Counter.Text = $"{_index + 1} / {_paths.Count}";
    }

    private void Go(int index)
    {
        if (_paths.Count == 0) return;
        _index = (index % _paths.Count + _paths.Count) % _paths.Count;   // wrap both directions
        Load();
    }

    private void OnPrev(object? sender, RoutedEventArgs e)  => Go(_index - 1);
    private void OnNext(object? sender, RoutedEventArgs e)  => Go(_index + 1);
    private void OnRotateLeft(object? sender, RoutedEventArgs e)  => Rotate(clockwise: false);
    private void OnRotateRight(object? sender, RoutedEventArgs e) => Rotate(clockwise: true);
    private void OnClose(object? sender, RoutedEventArgs e) => Close();
    
    private void Rotate(bool clockwise)
    {
        if (_paths.Count == 0) return;
        try
        {
            ImageOrient.RotateAndSave(_paths[_index], clockwise);
            _changed = true;
            Load();   // re-read the rotated file into the view
        }
        catch { /* leave the current image untouched on failure */ }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:   Go(_index - 1); e.Handled = true; break;
            case Key.Right:  Go(_index + 1); e.Handled = true; break;
            case Key.Escape: Close();        e.Handled = true; break;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _full?.Dispose();
        base.OnClosed(e);
    }

    public static async Task<bool> ShowAsync(Window owner, IReadOnlyList<string> paths, int startIndex,
        string title, bool allowRotate = false)
    {
        var w = new PictureViewerWindow(paths, startIndex, title, allowRotate);
        await w.ShowDialog(owner);
        return w._changed;
    }

    // Back-compat single-image overload (used by the Pocket viewer).
    public static Task<bool> ShowAsync(Window owner, string sourcePath, string title)
        => ShowAsync(owner, new[] { sourcePath }, 0, title);
}