using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BooksDb.Data.Models;

namespace BooksDb.Views;

public partial class ShelfView : UserControl
{
    private const double RowHeight = 198d;   // tile 186 + 2*6 margin; used for PageUp/Down

    public static readonly StyledProperty<IEnumerable?> SourceProperty =
        AvaloniaProperty.Register<ShelfView, IEnumerable?>(nameof(Source));

    public static readonly StyledProperty<double> TileWidthProperty =
        AvaloniaProperty.Register<ShelfView, double>(nameof(TileWidth), 136d);

    public static readonly StyledProperty<BookListItem?> SelectedItemProperty =
        AvaloniaProperty.Register<ShelfView, BookListItem?>(
            nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<ICommand?> ItemActivatedCommandProperty =
        AvaloniaProperty.Register<ShelfView, ICommand?>(nameof(ItemActivatedCommand));

    /// <summary>Flat, already-sorted/filtered book list (bind to BooksView).</summary>
    public IEnumerable? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Per-tile footprint in px (tile width + margins) used to pick column count.</summary>
    public double TileWidth
    {
        get => GetValue(TileWidthProperty);
        set => SetValue(TileWidthProperty, value);
    }

    /// <summary>Two-way bound to the owning VM's selected book.</summary>
    public BookListItem? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>Invoked on double-tap / Enter on a tile (bind to EditCommand).</summary>
    public ICommand? ItemActivatedCommand
    {
        get => GetValue(ItemActivatedCommandProperty);
        set => SetValue(ItemActivatedCommandProperty, value);
    }

    private static readonly CompareInfo Pl = CultureInfo.GetCultureInfo("pl-PL").CompareInfo;

    private INotifyCollectionChanged? _incc;
    private int _cols;
    private double _width;

    // current flat order + its row chunking, cached for keyboard navigation
    private List<BookListItem> _items = new();
    private List<IList<BookListItem>> _rows = new();

    private string _jumpBuffer = "";
    private DateTime _jumpAt;

    public ShelfView()
    {
        InitializeComponent();
        Focusable = true;   // the wall itself takes keyboard focus; tiles/rows do not
        AddHandler(InputElement.TappedEvent,       OnTileTapped,       RoutingStrategies.Bubble);
        AddHandler(InputElement.DoubleTappedEvent, OnTileDoubleTapped, RoutingStrategies.Bubble);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty)
        {
            OnWidth(Bounds.Width);
        }
        else if (change.Property == SourceProperty)
        {
            if (_incc is not null) _incc.CollectionChanged -= OnSourceCollectionChanged;
            _incc = Source as INotifyCollectionChanged;
            if (_incc is not null) _incc.CollectionChanged += OnSourceCollectionChanged;
            Rebuild();
        }
        else if (change.Property == TileWidthProperty)
        {
            _cols = 0;          // force a recompute against the new tile size
            OnWidth(_width);
        }
        else if (change.Property == SelectedItemProperty)
        {
            RefreshSelectionVisuals();
            ScrollSelectedIntoView();
        }
        else if (change.Property == IsVisibleProperty && change.GetNewValue<bool>())
        {
            // switching into cover view: grab focus so the keyboard works without a click
            Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Input);
        }
    }

    // Sort/filter/reload on the underlying view -> re-chunk.
    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void OnWidth(double w)
    {
        if (w <= 0) return;
        _width = w;
        // leave ~20px for the vertical scrollbar so tiles never overflow horizontally
        int cols = Math.Max(1, (int)((w - 20) / Math.Max(1d, TileWidth)));
        if (cols == _cols) return;
        _cols = cols;
        Rebuild();
    }

    private void Rebuild()
    {
        int cols = Math.Max(1, _cols);
        _items = Source?.Cast<BookListItem>().ToList() ?? new List<BookListItem>();
        _rows = new List<IList<BookListItem>>(_items.Count / cols + 1);
        for (int i = 0; i < _items.Count; i += cols)
            _rows.Add(_items.GetRange(i, Math.Min(cols, _items.Count - i)));
        RowsHost.ItemsSource = _rows;
    }

    // ---- keyboard navigation -------------------------------------------------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _items.Count == 0) return;

        int cols = Math.Max(1, _cols);
        int cur  = SelectedItem is null ? -1 : _items.IndexOf(SelectedItem);
        int target;

        switch (e.Key)
        {
            case Key.Left:     target = cur < 0 ? 0 : cur - 1; break;
            case Key.Right:    target = cur < 0 ? 0 : cur + 1; break;
            case Key.Up:       target = cur < 0 ? 0 : cur - cols; break;
            case Key.Down:     target = cur < 0 ? 0 : cur + cols; break;
            case Key.Home:     target = 0; break;
            case Key.End:      target = _items.Count - 1; break;
            case Key.PageUp:   target = cur < 0 ? 0 : cur - cols * VisibleRows(); break;
            case Key.PageDown: target = cur < 0 ? 0 : cur + cols * VisibleRows(); break;
            case Key.Enter:
                ActivateSelected();
                e.Handled = true;
                return;
            default:
                return;     // leave Ctrl+F, Esc, F1, Del, etc. for the other handlers
        }

        target = Math.Clamp(target, 0, _items.Count - 1);
        SelectedItem = _items[target];     // property-changed handler highlights + scrolls
        e.Handled = true;
    }

    // Type-to-jump: first title that prefix-matches the buffer (Polish collation).
    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (_items.Count == 0 || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;

        var now = DateTime.UtcNow;
        if ((now - _jumpAt).TotalMilliseconds > 800) _jumpBuffer = "";   // reset after a pause
        _jumpAt = now;
        _jumpBuffer += e.Text;

        BookListItem? match = null;
        foreach (var item in _items)
            if (item.Title is { } t && Pl.IsPrefix(t, _jumpBuffer, CompareOptions.IgnoreCase))
            {
                match = item;
                break;
            }

        if (match is not null) SelectedItem = match;   // handler highlights + scrolls
        e.Handled = true;
    }

    private int VisibleRows()
    {
        double h = RowsHost.Bounds.Height;
        return h <= 0 ? 1 : Math.Max(1, (int)(h / RowHeight));
    }

    private void ActivateSelected()
    {
        var book = SelectedItem;
        if (book is null) return;
        var cmd = ItemActivatedCommand;
        if (cmd is not null && cmd.CanExecute(book)) cmd.Execute(book);
    }

    private void ScrollSelectedIntoView()
    {
        if (_items.Count == 0 || SelectedItem is null) return;
        int idx = _items.IndexOf(SelectedItem);
        if (idx < 0) return;
        int row = idx / Math.Max(1, _cols);
        if (row >= 0 && row < _rows.Count) RowsHost.ScrollIntoView(row);
    }

    // ---- pointer + selection visuals ----------------------------------------

    private void OnTileTapped(object? sender, TappedEventArgs e)
    {
        var book = TileBook(e.Source);
        if (book is null) return;
        SelectedItem = book;
        Focus();            // keep keyboard navigation alive after a click
    }

    private void OnTileDoubleTapped(object? sender, TappedEventArgs e)
    {
        var book = TileBook(e.Source);
        if (book is null) return;
        SelectedItem = book;                          // ensure the VM has it before activating
        var cmd = ItemActivatedCommand;
        if (cmd is not null && cmd.CanExecute(book)) cmd.Execute(book);
    }

    private static BookListItem? TileBook(object? source) =>
        (source as Visual)?.FindAncestorOfType<CoverTile>(includeSelf: true)?.DataContext as BookListItem;

    // Push the current selection onto the realized (visible) tiles.
    private void RefreshSelectionVisuals()
    {
        foreach (var tile in this.GetVisualDescendants().OfType<CoverTile>())
            tile.UpdateSelected(SelectedItem);
    }
}
