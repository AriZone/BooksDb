using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using BooksDb.Data;

namespace BooksDb.Views;

public partial class CropDialog : Window
{
    private readonly string _path = "";
    private Bitmap? _bmp;
    private Rect _imgRect;     // displayed image rect within Host (overlay coords)
    private Rect _selN;        // selection, normalized [0..1] over the image

    private enum Drag { None, Move, TL, TR, BL, BR }
    private Drag _mode = Drag.None;
    private Point _last;

    public CropDialog() => InitializeComponent();   // designer

    public CropDialog(string path)
    {
        InitializeComponent();
        _path = path;
        try
        {
            using var s = ImageOrient.OrientedStream(path);
            _bmp = new Bitmap(s);
            Img.Source = _bmp;
        }
        catch { /* leave blank if unreadable */ }

        Host.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) RecomputeLayout(); };
    }

    private void RecomputeLayout()
    {
        if (_bmp is null) return;
        double bw = Host.Bounds.Width, bh = Host.Bounds.Height;
        double pw = _bmp.PixelSize.Width, ph = _bmp.PixelSize.Height;
        if (bw <= 0 || bh <= 0 || pw <= 0 || ph <= 0) return;

        double scale = Math.Min(bw / pw, bh / ph);
        double dispW = pw * scale, dispH = ph * scale;
        _imgRect = new Rect((bw - dispW) / 2, (bh - dispH) / 2, dispW, dispH);

        if (_selN.Width <= 0 || _selN.Height <= 0) _selN = new Rect(0, 0, 1, 1);
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        double cx = _imgRect.X + _selN.X * _imgRect.Width;
        double cy = _imgRect.Y + _selN.Y * _imgRect.Height;
        double cw = _selN.Width  * _imgRect.Width;
        double ch = _selN.Height * _imgRect.Height;

        Canvas.SetLeft(SelRect, cx); Canvas.SetTop(SelRect, cy);
        SelRect.Width = cw; SelRect.Height = ch;

        Place(HTL, cx,      cy);
        Place(HTR, cx + cw, cy);
        Place(HBL, cx,      cy + ch);
        Place(HBR, cx + cw, cy + ch);
    }

    private static void Place(Rectangle h, double x, double y)
    {
        Canvas.SetLeft(h, x - h.Width / 2);
        Canvas.SetTop(h, y - h.Height / 2);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var p = e.GetPosition(Overlay);
        _mode = HitTest(p);
        if (_mode != Drag.None)
        {
            _last = p;
            e.Pointer.Capture(Overlay);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_mode == Drag.None) return;
        var p = e.GetPosition(Overlay);
        var d = p - _last;     // Point - Point => Vector
        _last = p;
        switch (_mode)
        {
            case Drag.Move: MoveBy(d);              break;
            case Drag.TL:   ResizeBy(d, true,  true);  break;
            case Drag.TR:   ResizeBy(d, false, true);  break;
            case Drag.BL:   ResizeBy(d, true,  false); break;
            case Drag.BR:   ResizeBy(d, false, false); break;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _mode = Drag.None;
        e.Pointer.Capture(null);
    }

    private Drag HitTest(Point p)
    {
        double cx = _imgRect.X + _selN.X * _imgRect.Width;
        double cy = _imgRect.Y + _selN.Y * _imgRect.Height;
        double cw = _selN.Width  * _imgRect.Width;
        double ch = _selN.Height * _imgRect.Height;
        const double g = 16;   // grab radius
        if (Near(p, cx,      cy,      g)) return Drag.TL;
        if (Near(p, cx + cw, cy,      g)) return Drag.TR;
        if (Near(p, cx,      cy + ch, g)) return Drag.BL;
        if (Near(p, cx + cw, cy + ch, g)) return Drag.BR;
        if (p.X >= cx && p.X <= cx + cw && p.Y >= cy && p.Y <= cy + ch) return Drag.Move;
        return Drag.None;
    }

    private static bool Near(Point p, double x, double y, double r)
        => Math.Abs(p.X - x) <= r && Math.Abs(p.Y - y) <= r;

    private void ResizeBy(Vector v, bool left, bool top)
    {
        if (_imgRect.Width <= 0 || _imgRect.Height <= 0) return;
        double ndx = v.X / _imgRect.Width, ndy = v.Y / _imgRect.Height;
        double l = _selN.X, t = _selN.Y, r = _selN.X + _selN.Width, b = _selN.Y + _selN.Height;
        if (left) l += ndx; else r += ndx;
        if (top)  t += ndy; else b += ndy;

        const double min = 0.05;
        l = Math.Clamp(l, 0, 1); r = Math.Clamp(r, 0, 1);
        t = Math.Clamp(t, 0, 1); b = Math.Clamp(b, 0, 1);
        if (r - l < min) { if (left) l = r - min; else r = l + min; }
        if (b - t < min) { if (top)  t = b - min; else b = t + min; }
        l = Math.Clamp(l, 0, 1 - min); t = Math.Clamp(t, 0, 1 - min);
        r = Math.Clamp(r, l + min, 1); b = Math.Clamp(b, t + min, 1);

        _selN = new Rect(l, t, r - l, b - t);
        UpdateVisuals();
    }

    private void MoveBy(Vector v)
    {
        if (_imgRect.Width <= 0 || _imgRect.Height <= 0) return;
        double ndx = v.X / _imgRect.Width, ndy = v.Y / _imgRect.Height;
        double x = Math.Clamp(_selN.X + ndx, 0, 1 - _selN.Width);
        double y = Math.Clamp(_selN.Y + ndy, 0, 1 - _selN.Height);
        _selN = new Rect(x, y, _selN.Width, _selN.Height);
        UpdateVisuals();
    }

    private void OnReset(object? sender, RoutedEventArgs e) { _selN = new Rect(0, 0, 1, 1); UpdateVisuals(); }
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        if (_bmp is null) { Close(false); return; }
        int pw = _bmp.PixelSize.Width, ph = _bmp.PixelSize.Height;
        int x = (int)Math.Round(_selN.X * pw);
        int y = (int)Math.Round(_selN.Y * ph);
        int w = (int)Math.Round(_selN.Width  * pw);
        int h = (int)Math.Round(_selN.Height * ph);

        _bmp.Dispose(); _bmp = null;   // release before rewriting the file
        ImageOrient.CropAndSave(_path, _path, x, y, w, h);
        Close(true);
    }

    protected override void OnClosed(EventArgs e) { _bmp?.Dispose(); base.OnClosed(e); }

    public static Task<bool> ShowAsync(Window owner, string path)
        => new CropDialog(path).ShowDialog<bool>(owner);
}