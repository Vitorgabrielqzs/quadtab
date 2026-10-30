using System.Windows;
using System.Windows.Media;
using Quadtab.Rendering;

namespace Quadtab.Controls;

public enum PaperKind { Note, Lined, Page }

/// <summary>
/// Desenha o papel de um post-it ou de uma página (cor, grão, faixa adesiva, pautas, furos, orelha),
/// tudo num único elemento, sem camadas extras.
/// </summary>
public sealed class PaperElement : FrameworkElement
{
    static readonly FrameworkPropertyMetadataOptions R = FrameworkPropertyMetadataOptions.AffectsRender;

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(PaperKind), typeof(PaperElement), new FrameworkPropertyMetadata(PaperKind.Note, R));
    public static readonly DependencyProperty PaperProperty = DependencyProperty.Register(nameof(Paper), typeof(Color), typeof(PaperElement), new FrameworkPropertyMetadata(Colors.White, R));
    public static readonly DependencyProperty InkProperty = DependencyProperty.Register(nameof(Ink), typeof(Color), typeof(PaperElement), new FrameworkPropertyMetadata(Colors.Black, R));

    public PaperKind Kind { get => (PaperKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Color Paper { get => (Color)GetValue(PaperProperty); set => SetValue(PaperProperty, value); }
    public Color Ink { get => (Color)GetValue(InkProperty); set => SetValue(InkProperty, value); }

    // Pincéis fixos, criados uma vez.
    static readonly Brush Band = ColorMath.Brush(Color.FromArgb(13, 0, 0, 0));            // faixa adesiva: preto 5%
    static readonly Brush BandLine = ColorMath.Brush(Color.FromArgb(102, 255, 255, 255)); // brilho: branco 40%
    static readonly Brush MarginLine = ColorMath.Brush(Color.FromArgb(128, 0xE4, 0x50, 0x50));
    static readonly Brush Rule = ColorMath.Brush(Color.FromArgb(56, 0x46, 0x6E, 0xBE));
    static readonly Brush HoleLight = ColorMath.Brush(Color.FromArgb(153, 255, 255, 255));
    static readonly Brush Curl = CreateCurl();
    static readonly Brush Hole = CreateHole();
    static readonly ShadowLayer[] FoldShadow = ShadowSpec.Parse("-2 2 3 0 #1F000000");

    public PaperElement()
    {
        IsHitTestVisible = true;
        SnapsToDevicePixels = false;
    }

    protected override Size MeasureOverride(Size availableSize) => default;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        if (Kind == PaperKind.Page) RenderPage(dc, w, h);
        else RenderNote(dc, w, h, Kind == PaperKind.Lined);
    }

    void RenderNote(DrawingContext dc, double w, double h, bool lined)
    {
        var shape = Rounded(w, h, 2, 3);
        dc.DrawGeometry(ShadowCache.BrushOf(Paper), null, shape);
        dc.DrawGeometry(BoardSurfaces.GrainBrush, null, shape);
        dc.PushClip(shape);

        dc.DrawRectangle(Band, null, new Rect(0, 0, w, 26));
        dc.DrawRectangle(BandLine, null, new Rect(0, 26, w, 1));

        if (lined)
        {
            // Linhas na cor da tinta a 28%, a cada 27 px.
            var line = ShadowCache.BrushOf(ColorMath.WithAlpha(Ink, 0.28));
            for (double y = 60; y < h - 1; y += 27) dc.DrawRectangle(line, null, new Rect(0, y, w, 1));
        }
        else
        {
            // Clarão branco de 14% que some até 46% da altura e leve escurecimento no pé.
            double mid = Math.Clamp((h * 0.46 - 27) / (h - 27), 0, 1);
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            g.GradientStops.Add(new GradientStop(Color.FromArgb(36, 255, 255, 255), 0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), mid));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), mid));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(10, 0, 0, 0), 1));
            g.Freeze();
            dc.DrawRectangle(g, null, new Rect(0, 27, w, h - 27));
        }

        // Canto levantado (36×36).
        dc.DrawRectangle(Curl, null, new Rect(w - 36, h - 36, 36, 36));
        dc.Pop();
    }

    void RenderPage(DrawingContext dc, double w, double h)
    {
        const double fold = 28;
        var shape = new StreamGeometry();
        using (var c = shape.Open())
        {
            c.BeginFigure(new Point(0, 0), true, true);
            c.LineTo(new Point(w - fold, 0), false, false);
            c.LineTo(new Point(w, fold), false, false);
            c.LineTo(new Point(w, h), false, false);
            c.LineTo(new Point(0, h), false, false);
        }
        shape.Freeze();

        var paper = ColorMath.Mix(Paper, 0.32, Color.FromRgb(0xFF, 0xFE, 0xFA));
        dc.DrawGeometry(ShadowCache.BrushOf(paper), null, shape);
        dc.DrawGeometry(BoardSurfaces.GrainBrush, null, shape);
        dc.PushClip(shape);

        dc.DrawRectangle(MarginLine, null, new Rect(30, 0, 1, h));                      // margem vermelha
        for (double y = 88; y < h - 1; y += 28) dc.DrawRectangle(Rule, null, new Rect(0, y, w, 1)); // pautas azuis

        // 3 furos de caderno: x = 9, de y = 54 até 54 px do fim.
        double top = 54, bottom = h - 54 - 12;
        foreach (var y in new[] { top, (top + bottom) / 2, bottom })
        {
            dc.DrawEllipse(HoleLight, null, new Point(15, y + 7), 6, 6);
            dc.DrawEllipse(Hole, null, new Point(15, y + 6), 6, 6);
        }

        // Orelha dobrada: sombra das pernas do triângulo + o triângulo mais escuro.
        var box = new Rect(w - fold, 0, fold, fold);
        dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, w, h)), new RectangleGeometry(box)));
        ShadowCache.Draw(dc, box, 0, FoldShadow);
        dc.Pop();
        dc.Pop();

        var tri = new StreamGeometry();
        using (var c = tri.Open())
        {
            c.BeginFigure(new Point(w - fold, 0), true, true);
            c.LineTo(new Point(w - fold, fold), false, false);
            c.LineTo(new Point(w, fold), false, false);
        }
        tri.Freeze();
        dc.DrawGeometry(ShadowCache.BrushOf(ColorMath.Mix(Paper, 0.25, Color.FromRgb(0xE6, 0xE0, 0xD2))), null, tri);
    }

    /// <summary>Retângulo com cantos de cima e de baixo diferentes.</summary>
    static Geometry Rounded(double w, double h, double top, double bottom)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(top, 0), true, true);
            c.LineTo(new Point(w - top, 0), false, false);
            c.ArcTo(new Point(w, top), new Size(top, top), 0, false, SweepDirection.Clockwise, false, false);
            c.LineTo(new Point(w, h - bottom), false, false);
            c.ArcTo(new Point(w - bottom, h), new Size(bottom, bottom), 0, false, SweepDirection.Clockwise, false, false);
            c.LineTo(new Point(bottom, h), false, false);
            c.ArcTo(new Point(0, h - bottom), new Size(bottom, bottom), 0, false, SweepDirection.Clockwise, false, false);
            c.LineTo(new Point(0, top), false, false);
            c.ArcTo(new Point(top, 0), new Size(top, top), 0, false, SweepDirection.Clockwise, false, false);
        }
        g.Freeze();
        return g;
    }

    static Brush CreateCurl()
    {
        // linear-gradient(315deg, transparente 44%, sombra 12% 48%, brilho 55% 53%, transparente 78%)
        var g = new LinearGradientBrush { StartPoint = new Point(1, 1), EndPoint = new Point(0, 0) };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.44));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(31, 0, 0, 0), 0.48));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(140, 255, 255, 255), 0.53));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.78));
        g.Freeze();
        return g;
    }

    static Brush CreateHole()
    {
        var baseC = Color.FromArgb(153, 120, 78, 34);
        var g = new RadialGradientBrush { GradientOrigin = new Point(0.6, 0.68), Center = new Point(0.55, 0.6), RadiusX = 0.62, RadiusY = 0.62 };
        g.GradientStops.Add(new GradientStop(baseC, 0));
        g.GradientStops.Add(new GradientStop(baseC, 0.55));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(215, 60, 38, 16), 1));
        g.Freeze();
        return g;
    }
}
