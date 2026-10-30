using System.Windows;
using System.Windows.Media;
using Quadtab.Rendering;

namespace Quadtab.Controls;

/// <summary>
/// Fundo da janela: cor Bg com três manchas em gradiente radial que somem a 62% do raio.
/// Com Saturation > 1 desenha a versão "vista através do vidro" (saturate do CSS).
/// </summary>
public sealed class BackdropBlobs : FrameworkElement
{
    static readonly FrameworkPropertyMetadataOptions R = FrameworkPropertyMetadataOptions.AffectsRender;

    public static readonly DependencyProperty BgProperty = DependencyProperty.Register(nameof(Bg), typeof(Color), typeof(BackdropBlobs), new FrameworkPropertyMetadata(Colors.White, R));
    public static readonly DependencyProperty Blob1Property = DependencyProperty.Register(nameof(Blob1), typeof(Color), typeof(BackdropBlobs), new FrameworkPropertyMetadata(Colors.White, R));
    public static readonly DependencyProperty Blob2Property = DependencyProperty.Register(nameof(Blob2), typeof(Color), typeof(BackdropBlobs), new FrameworkPropertyMetadata(Colors.White, R));
    public static readonly DependencyProperty Blob3Property = DependencyProperty.Register(nameof(Blob3), typeof(Color), typeof(BackdropBlobs), new FrameworkPropertyMetadata(Colors.White, R));
    public static readonly DependencyProperty SaturationProperty = DependencyProperty.Register(nameof(Saturation), typeof(double), typeof(BackdropBlobs), new FrameworkPropertyMetadata(1.0, R));

    public BackdropBlobs() => IsHitTestVisible = false;

    public Color Bg { get => (Color)GetValue(BgProperty); set => SetValue(BgProperty, value); }
    public Color Blob1 { get => (Color)GetValue(Blob1Property); set => SetValue(Blob1Property, value); }
    public Color Blob2 { get => (Color)GetValue(Blob2Property); set => SetValue(Blob2Property, value); }
    public Color Blob3 { get => (Color)GetValue(Blob3Property); set => SetValue(Blob3Property, value); }
    public double Saturation { get => (double)GetValue(SaturationProperty); set => SetValue(SaturationProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => default;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        double s = Saturation;
        Color S(Color c) => s == 1 ? c : ColorMath.Saturate(c, s);

        dc.DrawRectangle(ShadowCache.BrushOf(S(Bg)), null, new Rect(0, 0, w, h));
        Blob(dc, S(Blob1), new Point(0, 0), 640, 520);
        Blob(dc, S(Blob2), new Point(w, h * 0.06), 760, 620);
        Blob(dc, S(Blob3), new Point(w * 0.35, h * 1.08), 700, 520);
    }

    static void Blob(DrawingContext dc, Color c, Point center, double rx, double ry)
    {
        var b = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = center,
            GradientOrigin = center,
            RadiusX = rx,
            RadiusY = ry,
        };
        // Some até a mesma cor com alfa 0 (sem clarear no meio do caminho).
        b.GradientStops.Add(new GradientStop(c, 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 0.62));
        b.Freeze();
        dc.DrawRectangle(b, null, new Rect(center.X - rx, center.Y - ry, rx * 2, ry * 2));
    }
}
