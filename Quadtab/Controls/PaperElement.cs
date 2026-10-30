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
    public static readonly DependencyProperty SpiralProperty = DependencyProperty.Register(nameof(Spiral), typeof(bool), typeof(PaperElement), new FrameworkPropertyMetadata(true, R));

    public PaperKind Kind { get => (PaperKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Color Paper { get => (Color)GetValue(PaperProperty); set => SetValue(PaperProperty, value); }
    public Color Ink { get => (Color)GetValue(InkProperty); set => SetValue(InkProperty, value); }
    public bool Spiral { get => (bool)GetValue(SpiralProperty); set => SetValue(SpiralProperty, value); }

    // Pincéis fixos, criados uma vez.
    static readonly Brush Band = ColorMath.Brush(Color.FromArgb(13, 0, 0, 0));            // faixa adesiva: preto 5%
    static readonly Brush BandLine = ColorMath.Brush(Color.FromArgb(102, 255, 255, 255)); // brilho: branco 40%
    static readonly Brush Curl = CreateCurl();

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

    // Espiral de caderno: arame metálico, brilho por cima e furos no papel.
    static readonly Brush Wire = CreateWire();
    static readonly Pen WirePen = Frozen(new Pen(Wire, 2.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    static readonly Pen WireShine = Frozen(new Pen(ColorMath.Brush(Color.FromArgb(120, 255, 255, 255)), 0.8));
    static readonly Brush SpiralHole = ColorMath.Brush(Color.FromArgb(150, 0x3A, 0x34, 0x2E));
    static readonly Brush SpiralShade = CreateSpiralShade();
    const double RingStep = 26;

    /// <summary>
    /// Folha de caderno espiral (como a foto de referência): papel liso, sem pautas, margem, furos de
    /// fichário nem orelha; só os anéis de arame na lateral esquerda.
    /// </summary>
    void RenderPage(DrawingContext dc, double w, double h)
    {
        var sheet = new Rect(0, 0, w, h);
        var paper = ColorMath.Mix(Paper, 0.16, Colors.White);
        dc.DrawRoundedRectangle(ShadowCache.BrushOf(paper), null, sheet, 3, 3);
        if (!Spiral) return; // folha solta: só o papel
        dc.DrawRectangle(SpiralShade, null, new Rect(0, 0, 26, h)); // sombra suave que os anéis fazem no papel

        for (double y = 20; y <= h - 16; y += RingStep)
        {
            dc.DrawEllipse(SpiralHole, null, new Point(15, y + 2), 3.3, 2.7);
            // O anel sai do furo, passa por cima da borda e volta por trás da folha.
            var ring = new Rect(-12, y - 5, 28, 8);
            dc.DrawRoundedRectangle(null, WirePen, ring, 4, 4);
            dc.DrawLine(WireShine, new Point(-8, y - 4.2), new Point(12, y - 4.2));
        }
    }

    static Pen Frozen(Pen p) { p.Freeze(); return p; }

    static Brush CreateWire()
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xB4, 0xB2, 0xAE), 0));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x4E, 0x4B, 0x47), 0.55));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x8C, 0x89, 0x84), 1));
        g.Freeze();
        return g;
    }

    static Brush CreateSpiralShade()
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        g.GradientStops.Add(new GradientStop(Color.FromArgb(26, 0x50, 0x46, 0x3C), 0));
        g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x50, 0x46, 0x3C), 1));
        g.Freeze();
        return g;
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

}
