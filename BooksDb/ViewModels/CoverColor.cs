using Avalonia.Media;

namespace BooksDb.ViewModels;

// Deterministic placeholder-cover colour: category sets the hue family, title adds a small
// jitter within it, so same-category books cluster but stay individually distinguishable.
public static class CoverColor
{
    public static Color For(string? category, string? title)
    {
        var hueKey     = !string.IsNullOrWhiteSpace(category) ? category!
            : !string.IsNullOrWhiteSpace(title)    ? title! : "?";
        uint titleHash = Fnv(title ?? "");

        int    hueBase   = (int)(Fnv(hueKey) % 360);
        int    hueJitter = (int)(titleHash % 31) - 15;          // -15..+15, within the family
        double hue       = ((hueBase + hueJitter) % 360 + 360) % 360;

        int    lightStep = (int)((titleHash >> 8) % 9) - 4;     // -4..+4
        double light     = 0.40 + lightStep / 100.0;            // 0.36–0.44 — dark enough for white text on every hue

        return HslToRgb(hue, 0.50, light);
    }

    private static uint Fnv(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (var c in s) { h ^= c; h *= 16777619; }
            return h;
        }
    }

    private static Color HslToRgb(double h, double s, double l)
    {
        double c = (1 - System.Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - System.Math.Abs(h / 60.0 % 2 - 1));
        double m = l - c / 2;
        double r, g, b;
        switch ((int)(h / 60) % 6)
        {
            case 0:  r = c; g = x; b = 0; break;
            case 1:  r = x; g = c; b = 0; break;
            case 2:  r = 0; g = c; b = x; break;
            case 3:  r = 0; g = x; b = c; break;
            case 4:  r = x; g = 0; b = c; break;
            default: r = c; g = 0; b = x; break;
        }
        return Color.FromRgb((byte)System.Math.Round((r + m) * 255),
            (byte)System.Math.Round((g + m) * 255),
            (byte)System.Math.Round((b + m) * 255));
    }
}