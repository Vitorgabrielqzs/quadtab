using System.Windows;
using System.Windows.Media;
using Quadtab.Rendering;

namespace Quadtab.Controls;

/// <summary>Alfinete de 20 px: brilho, cor, borda escura, sombra e "sombra da agulha".</summary>
public sealed class PinElement : FrameworkElement
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Color), typeof(PinElement), new FrameworkPropertyMetadata(Colors.Red, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DiameterProperty = DependencyProperty.Register(
        nameof(Diameter), typeof(double), typeof(PinElement), new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    static readonly Dictionary<Color, Brush> Bodies = new();
    static readonly Brush Highlight = CreateHighlight();
    static readonly Brush Needle = ColorMath.Brush(Color.FromArgb(71, 40, 20, 0));
    static readonly ShadowLayer[] DropShadow = ShadowSpec.Parse("3 5 4 0 #59281400");
    static readonly ShadowLayer[] InnerShadow = ShadowSpec.Parse("inset 0 -2 2 0 #2E000000");

    public PinElement() => IsHitTestVisible = false;

    public Color Color { get => (Color)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public double Diameter { get => (double)GetValue(DiameterProperty); set => SetValue(DiameterProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(Diameter, Diameter);

    protected override void OnRender(DrawingContext dc)
    {
        double d = Diameter, s = d / 20.0, r = d / 2;
        var box = new Rect(0, 0, d, d);
        ShadowCache.Draw(dc, box, r, DropShadow);

        dc.DrawEllipse(Body(Color), null, new Point(r, r), r, r);
        ShadowCache.Draw(dc, box, r, InnerShadow);

        // Sombra da agulha: 2×9 girada −38°.
        dc.PushTransform(new ScaleTransform(s, s));
        dc.PushTransform(new RotateTransform(-38, 14, 19.5));
        dc.DrawRoundedRectangle(Needle, null, new Rect(13, 15, 2, 9), 1, 1);
        dc.Pop();
        dc.DrawEllipse(Highlight, null, new Point(10, 10), 10, 10);
        dc.Pop();
    }

    static Brush Body(Color c)
    {
        if (Bodies.TryGetValue(c, out var b)) return b;
        // radial-gradient(circle at 50% 55%, cor 52%, cor+45% de preto 100%)
        var dark = ColorMath.Mix(c, 0.55, Colors.Black);
        var g = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            Center = new Point(0.5, 0.55),
            GradientOrigin = new Point(0.5, 0.55),
            RadiusX = 0.744,
            RadiusY = 0.744,
        };
        g.GradientStops.Add(new GradientStop(c, 0.52));
        g.GradientStops.Add(new GradientStop(dark, 1));
        g.Freeze();
        Bodies[c] = g;
        return g;
    }

    static Brush CreateHighlight()
    {
        // radial-gradient(circle at 36% 30%, #fff 0 1.5px, branco 55% 2.6px, transparente 5.5px)
        var center = new Point(7.2, 6);
        var g = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = center, GradientOrigin = center, RadiusX = 5.5, RadiusY = 5.5 };
        g.GradientStops.Add(new GradientStop(Colors.White, 0));
        g.GradientStops.Add(new GradientStop(Colors.White, 1.5 / 5.5));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(140, 255, 255, 255), 2.6 / 5.5));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        g.Freeze();
        return g;
    }
}

/// <summary>Fita adesiva 86×27 com listras diagonais e pontas em zigue-zague.</summary>
public sealed class TapeElement : FrameworkElement
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Color), typeof(TapeElement), new FrameworkPropertyMetadata(Colors.Red, FrameworkPropertyMetadataOptions.AffectsRender));

    static readonly Brush Stripes = CreateStripes();

    public TapeElement()
    {
        IsHitTestVisible = false;
        Width = 86;
        Height = 27;
    }

    public Color Color { get => (Color)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var shape = new StreamGeometry();
        using (var c = shape.Open())
        {
            // polygon(0 0,100% 0,97% 20%,100% 40%,97% 60%,100% 80%,97% 100%,0 100%,3% 80%,0 60%,3% 40%,0 20%)
            c.BeginFigure(new Point(0, 0), true, true);
            c.LineTo(new Point(w, 0), false, false);
            c.LineTo(new Point(w * .97, h * .2), false, false);
            c.LineTo(new Point(w, h * .4), false, false);
            c.LineTo(new Point(w * .97, h * .6), false, false);
            c.LineTo(new Point(w, h * .8), false, false);
            c.LineTo(new Point(w * .97, h), false, false);
            c.LineTo(new Point(0, h), false, false);
            c.LineTo(new Point(w * .03, h * .8), false, false);
            c.LineTo(new Point(0, h * .6), false, false);
            c.LineTo(new Point(w * .03, h * .4), false, false);
            c.LineTo(new Point(0, h * .2), false, false);
        }
        shape.Freeze();
        // color-mix(in srgb, cor do alfinete 42%, branco 50%)
        dc.DrawGeometry(ShadowCache.BrushOf(ColorMath.Mix(Color, 0.42, Color.FromArgb(128, 255, 255, 255))), null, shape);
        dc.DrawGeometry(Stripes, null, shape);
    }

    static Brush CreateStripes()
    {
        // repeating-linear-gradient(45deg, branco 34% 0 5px, transparente 5px 10px)
        double d = 10 / Math.Sqrt(2);
        var g = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(d, -d),
            SpreadMethod = GradientSpreadMethod.Repeat,
        };
        var white = Color.FromArgb(87, 255, 255, 255);
        var clear = Color.FromArgb(0, 255, 255, 255);
        g.GradientStops.Add(new GradientStop(white, 0));
        g.GradientStops.Add(new GradientStop(white, 0.5));
        g.GradientStops.Add(new GradientStop(clear, 0.5));
        g.GradientStops.Add(new GradientStop(clear, 1));
        g.Freeze();
        return g;
    }
}
