using System.Windows.Media;

namespace Quadtab.Rendering;

/// <summary>Contas de cor equivalentes às do CSS (color-mix, saturate).</summary>
public static class ColorMath
{
    /// <summary>color-mix(in srgb, a p%, b) — mistura com alfa pré-multiplicado, como o navegador.</summary>
    public static Color Mix(Color a, double p, Color b)
    {
        double aa = a.A / 255.0, ba = b.A / 255.0;
        double alpha = p * aa + (1 - p) * ba;
        if (alpha <= 0) return Colors.Transparent;
        byte Ch(byte x, byte y) => (byte)Math.Round(Math.Clamp((p * x * aa + (1 - p) * y * ba) / alpha, 0, 255));
        return Color.FromArgb((byte)Math.Round(alpha * 255), Ch(a.R, b.R), Ch(a.G, b.G), Ch(a.B, b.B));
    }

    /// <summary>Filtro saturate(s) do CSS aplicado a uma cor.</summary>
    public static Color Saturate(Color c, double s)
    {
        double r = c.R, g = c.G, b = c.B;
        double nr = (0.213 + 0.787 * s) * r + (0.715 - 0.715 * s) * g + (0.072 - 0.072 * s) * b;
        double ng = (0.213 - 0.213 * s) * r + (0.715 + 0.285 * s) * g + (0.072 - 0.072 * s) * b;
        double nb = (0.213 - 0.213 * s) * r + (0.715 - 0.715 * s) * g + (0.072 + 0.928 * s) * b;
        return Color.FromArgb(c.A, B(nr), B(ng), B(nb));
    }

    public static Color WithAlpha(Color c, double a) => Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);

    public static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    static byte B(double v) => (byte)Math.Round(Math.Clamp(v, 0, 255));
}
