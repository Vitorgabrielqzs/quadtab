using System.Windows;
using System.Windows.Media;
using Quadtab.Models;
using Quadtab.Rendering;

namespace Quadtab.Controls;

/// <summary>Moldura do quadro (madeira clara, alumínio, madeira escura ou papel), com sombras.</summary>
public sealed class FrameBackground : FrameworkElement
{
    public const double Radius = 32;

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(BoardModel), typeof(FrameBackground), new FrameworkPropertyMetadata(BoardModel.Cortica, FrameworkPropertyMetadataOptions.AffectsRender));

    static readonly ShadowLayer[] Outer = ShadowSpec.Parse("0 34 60 -24 #8050320F, 0 8 22 0 #2950320F");
    static readonly ShadowLayer[] Inner = ShadowSpec.Parse("inset 0 2 1 0 #99FFFFFF, inset 0 -2 2 0 #405A370F");

    public FrameBackground() => IsHitTestVisible = false;

    public BoardModel Model { get => (BoardModel)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => default;

    protected override void OnRender(DrawingContext dc)
    {
        var box = new Rect(RenderSize);
        if (box.Width <= 0 || box.Height <= 0) return;
        ShadowCache.Draw(dc, box, Radius, Outer);

        var (stops, stripeLight, stripeDark) = Model switch
        {
            BoardModel.Branco => (new[] { (H(0xF5F6F8), 0.0), (H(0xD2D6DD), .45), (H(0xEEF0F3), .70), (H(0xBBC1CA), 1.0) }, (Color?)null, (Color?)null),
            BoardModel.Lousa => (new[] { (H(0x9A6C43), 0.0), (H(0x6B4628), .45), (H(0x8A623B), .70), (H(0x54361D), 1.0) },
                                 Color.FromArgb(13, 255, 255, 255), Color.FromArgb(18, 0, 0, 0)),
            BoardModel.Pontilhado => (new[] { (H(0xF6F6F2), 0.0), (H(0xDCDCD4), .50), (H(0xEDEDE7), .75), (H(0xCFCFC6), 1.0) }, null, null),
            _ => (new[] { (H(0xEAD1A6), 0.0), (H(0xD3AD75), .36), (H(0xE3C597), .62), (H(0xC0965C), 1.0) },
                  Color.FromArgb(15, 255, 255, 255), Color.FromArgb(13, 90, 50, 10)),
        };

        dc.DrawRoundedRectangle(BoardSurfaces.CssGradient(160, box, stops), null, box, Radius, Radius);
        if (stripeLight is { } l && stripeDark is { } d)
            dc.DrawRoundedRectangle(Grain(l, d), null, box, Radius, Radius); // veios verticais finos
        ShadowCache.Draw(dc, box, Radius, Inner);
    }

    static readonly Dictionary<(Color, Color), Brush> GrainCache = new();

    static Brush Grain(Color light, Color dark)
    {
        if (GrainCache.TryGetValue((light, dark), out var b)) return b;
        // repeating-linear-gradient(90deg, claro 0 2px, nada 2-9px, escuro 9-11px, nada 11-19px)
        var g = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(19, 0),
            SpreadMethod = GradientSpreadMethod.Repeat,
        };
        var clear = Color.FromArgb(0, 255, 255, 255);
        void S(Color c, double px) => g.GradientStops.Add(new GradientStop(c, px / 19));
        S(light, 0); S(light, 2); S(clear, 2); S(clear, 9); S(dark, 9); S(dark, 11); S(clear, 11); S(clear, 19);
        g.Freeze();
        GrainCache[(light, dark)] = g;
        return g;
    }

    static Color H(uint rgb) => Palette.C(rgb);
}

/// <summary>
/// Superfície do quadro infinito (cortiça, branco, lousa, pontilhado) com a sombra interna.
/// A textura se repete sem fim e acompanha a câmera: só a posição do pincel muda a cada quadro,
/// nada é redesenhado.
/// </summary>
public sealed class SurfaceBackground : FrameworkElement
{
    public const double Radius = 16;

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(BoardModel), typeof(SurfaceBackground),
        new FrameworkPropertyMetadata(BoardModel.Cortica, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowGridProperty = DependencyProperty.Register(
        nameof(ShowGrid), typeof(bool), typeof(SurfaceBackground), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool ShowGrid { get => (bool)GetValue(ShowGridProperty); set => SetValue(ShowGridProperty, value); }

    const double GridTile = 32;
    readonly DrawingBrush _gridDark = GridBrush(Color.FromArgb(34, 0, 0, 0));
    readonly DrawingBrush _gridLight = GridBrush(Color.FromArgb(40, 255, 255, 255));

    static DrawingBrush GridBrush(Color c)
    {
        var g = new GeometryDrawing(null, new Pen(new SolidColorBrush(c), 1), new GeometryGroup
        {
            Children = { new LineGeometry(new Point(0, 0), new Point(GridTile, 0)), new LineGeometry(new Point(0, 0), new Point(0, GridTile)) },
        });
        return new DrawingBrush(g)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, GridTile, GridTile),
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };
    }

    static readonly Brush WhiteBase = ColorMath.Brush(Palette.C(0xF9FAFB));
    static readonly Brush ChalkBase = ColorMath.Brush(Palette.C(0x2C4838));
    static readonly Brush DotBase = ColorMath.Brush(Palette.C(0xFBFBF8));

    static readonly Dictionary<BoardModel, ShadowLayer[]> Insets = new()
    {
        [BoardModel.Cortica] = ShadowSpec.Parse("inset 0 4 16 0 #733C1E00, inset 0 0 0 1 #4D462300"),
        [BoardModel.Branco] = ShadowSpec.Parse("inset 0 2 10 0 #1F1E283C, inset 0 0 0 1 #141E283C"),
        [BoardModel.Lousa] = ShadowSpec.Parse("inset 0 4 18 0 #80000000"),
        [BoardModel.Pontilhado] = ShadowSpec.Parse("inset 0 2 10 0 #1F28281E, inset 0 0 0 1 #1428281E"),
    };

    // Cópias que podem mudar (as originais são congeladas e compartilhadas).
    readonly ImageBrush _cork = BoardSurfaces.CorkBrush.Clone();
    readonly DrawingBrush _dots = BoardSurfaces.DotsBrush.Clone();

    public SurfaceBackground() => IsHitTestVisible = false;

    public BoardModel Model { get => (BoardModel)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }

    /// <summary>
    /// Posiciona a textura: (ox, oy) é onde a origem do mundo aparece na tela.
    /// Muda só o Viewport do pincel (barato, sem redesenhar).
    /// </summary>
    public void SetCamera(double ox, double oy, double zoom)
    {
        Place(_cork, BoardSurfaces.CorkTile, ox, oy, zoom);
        Place(_dots, BoardSurfaces.DotTile, ox, oy, zoom);
        Place(_gridDark, GridTile, ox, oy, zoom);
        Place(_gridLight, GridTile, ox, oy, zoom);
    }

    static void Place(TileBrush b, double baseSize, double ox, double oy, double zoom)
    {
        double s = BoardSurfaces.TileOnScreen(baseSize, zoom, out _);
        var v = new Rect(Mod(ox, s), Mod(oy, s), s, s);
        if (b.Viewport != v) b.Viewport = v;
    }

    static double Mod(double a, double m) => a - Math.Floor(a / m) * m - m;

    protected override Size MeasureOverride(Size availableSize) => default;

    protected override void OnRender(DrawingContext dc)
    {
        var box = new Rect(RenderSize);
        if (box.Width <= 0 || box.Height <= 0) return;
        var clip = new RectangleGeometry(box, Radius, Radius);
        clip.Freeze();
        dc.PushClip(clip);

        switch (Model)
        {
            case BoardModel.Branco:
                dc.DrawRectangle(WhiteBase, null, box);
                dc.DrawRectangle(BoardSurfaces.CssGradient(165, box,
                    (Color.FromArgb(242, 255, 255, 255), 0), (Color.FromArgb(153, 232, 236, 242), .55), (Color.FromArgb(230, 255, 255, 255), 1)), null, box);
                break;

            case BoardModel.Lousa:
                dc.DrawRectangle(ChalkBase, null, box);
                Smudge(dc, box, .22, .30, .45, .32, 18); // manchas de giz
                Smudge(dc, box, .78, .72, .38, .28, 15);
                break;

            case BoardModel.Pontilhado:
                dc.DrawRectangle(DotBase, null, box);
                dc.DrawRectangle(_dots, null, box);
                break;

            default:
                dc.DrawRectangle(_cork, null, box);
                break;
        }
        if (ShowGrid) dc.DrawRectangle(Model == BoardModel.Lousa ? _gridLight : _gridDark, null, box);
        dc.Pop();

        ShadowCache.Draw(dc, box, Radius, Insets[Model]);
    }

    static void Smudge(DrawingContext dc, Rect area, double cx, double cy, double rx, double ry, byte alpha)
    {
        var c = new Point(area.X + area.Width * cx, area.Y + area.Height * cy);
        var b = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = c,
            GradientOrigin = c,
            RadiusX = area.Width * rx,
            RadiusY = area.Height * ry,
        };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, 255, 255, 255), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        b.Freeze();
        dc.DrawEllipse(b, null, c, b.RadiusX, b.RadiusY);
    }
}
