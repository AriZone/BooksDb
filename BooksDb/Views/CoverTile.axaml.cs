using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using BooksDb.Data.Models;
using BooksDb.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BooksDb.Views;

public partial class CoverTile : UserControl
{
    private CoverCache? _cache;

    private CoverCache? Cache
    {
        get
        {
            if (_cache is not null) return _cache;
            var sp = App.Services;          // null at design time
            _cache = sp?.GetService<CoverCache>();
            return _cache;
        }
    }

    public CoverTile()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // A freshly-realized tile reflects the shelf's current selection.
        UpdateSelected(this.FindAncestorOfType<ShelfView>()?.SelectedItem);
    }

    /// <summary>Show/hide the selection ring based on the shelf's current selection.</summary>
    public void UpdateSelected(BookListItem? selected)
    {
        SelectionRing.IsVisible = selected is not null && ReferenceEquals(DataContext, selected);
    }

    private async void OnDataContextChanged(object? sender, EventArgs e)
    {
        // start clean: hide any cover from the recycled tile
        CoverImage.Source = null;
        CoverImage.IsVisible = false;

        if (DataContext is not BookListItem item)
        {
            PlaceholderHost.Background = Brushes.Transparent;
            SelectionRing.IsVisible = false;
            ToolTip.SetTip(this, null);
            return;
        }

        PlaceholderHost.Background = new SolidColorBrush(CoverColor.For(item.MainCategory, item.Title));
        ToolTip.SetTip(this, string.IsNullOrWhiteSpace(item.Authors)
            ? item.Title
            : $"{item.Title}\n{item.Authors}");
        UpdateSelected(this.FindAncestorOfType<ShelfView>()?.SelectedItem);

        var path = item.CoverPath;
        if (string.IsNullOrEmpty(path)) return;

        var cache = Cache;
        if (cache is null) return;

        // synchronous fast-path: already decoded
        if (cache.TryGet(path, out var cached))
        {
            if (cached is not null) ShowCover(cached);
            return;
        }

        // async decode; guard against the tile being recycled mid-flight
        var loaded = await cache.GetAsync(path);
        if (loaded is null) return;
        if (DataContext is BookListItem current && current.CoverPath == path)
            ShowCover(loaded);
    }

    private void ShowCover(Bitmap bmp)
    {
        CoverImage.Source = bmp;
        CoverImage.IsVisible = true;
    }
}
