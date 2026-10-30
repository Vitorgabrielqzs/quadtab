using System.Windows;
using System.Windows.Media;
using Quadtab.Models;
using Quadtab.Rendering;
using Quadtab.Services;

namespace Quadtab.Controls;

/// <summary>
/// Moldura do quadro, uma diferente para cada modelo: Liso = faixa suave azulada (clara ou grafite,
/// segue o tema); Cortiça = madeira clara; Branco = alumínio; Lousa = madeira escura; Pontilhado = papel.
/// Na tela cheia (FullBleed) a moldura continua, com os cantos arredondados, até a borda da tela (sem sombra de fora).
/// </summary>
public sealed class FrameBackground : FrameworkElement
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(BoardModel), typeof(FrameBackground), new FrameworkPropertyMetadata(BoardModel.Liso, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FullBleedProperty = DependencyProperty.Register(
        nameof(FullBleed), typeof(bool), typeof(FrameBackground), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    static readonly ShadowLayer[] Outer = ShadowSpec.Parse("0 34 60 -24 #8050320F, 0 8 22 0 #2950320F");
    static readonly ShadowLayer[] Inner = ShadowSpec.Parse("inset 0 2 1 0 #99FFFFFF, inset 0 -2 2 0 #405A370F");
    static readonly ShadowLayer[] LisoShadow = ShadowSpec.Parse("0 18 40 -18 #401E243C, 0 2 8 0 #141E243C");
    static readonly ShadowLayer[] LisoShadowDark = ShadowSpec.Parse("0 18 40 -18 #99000000, 0 2 8 0 #4D000000");

    public FrameBackground()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => ThemeService.Changed += InvalidateVisual;
        Unloaded += (_, _) => ThemeService.Changed -= InvalidateVisual;
    }

    public BoardModel Model { get => (BoardModel)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public bool FullBleed { get => (bool)GetValue(FullBleedProperty); set => SetValue(FullBleedProperty, value); }

    /// <summary>Margem entre a moldura e a superfície (o "batente" da moldura).</summary>
    public static double Inset(BoardModel m, bool fullBleed) => m == BoardModel.Liso ? 10 : 16;

    protected override Size MeasureOverride(Size availableSize) => default;

    protected override void OnRender(DrawingContext dc)
    {
        var box = new Rect(RenderSize);
        if (box.Width <= 1 || box.Height <= 1) return;
        // Cantos sempre arredondados, também na tela cheia (só a sombra de fora some).
        double radius = Model == BoardModel.Liso ? 26 : 32;
        if (Model == BoardModel.Liso)
        {
            // Faixa de 10 px com um degradê suave e um fio de luz por dentro (moldura própria do Liso).
            bool dark = ThemeService.IsDark;
            if (!FullBleed) ShadowCache.Draw(dc, box, radius, dark ? LisoShadowDark : LisoShadow);
            var band = dark
                ? new[] { (H(0x30333D), 0.0), (H(0x24262E), 0.55), (H(0x1B1D23), 1.0) }
                : new[] { (H(0xF6F8FC), 0.0), (H(0xE3E8F1), 0.55), (H(0xD2D9E6), 1.0) };
            dc.DrawRoundedRectangle(BoardSurfaces.CssGradient(160, box, band), null, box, radius, radius);
            var rim = new Pen(ColorMath.Brush(dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(200, 255, 255, 255)), 1);
            rim.Freeze();
            var inner = box;
            inner.Inflate(-0.5, -0.5);
            dc.DrawRoundedRectangle(null, rim, inner, Math.Max(0, radius - 0.5), Math.Max(0, radius - 0.5));
            return;
        }

        if (!FullBleed) ShadowCache.Draw(dc, box, radius, Outer);
        var (stops, stripeLight, stripeDark) = Model switch
        {
            BoardModel.Branco => (new[] { (H(0xF5F6F8), 0.0), (H(0xD2D6DD), .45), (H(0xEEF0F3), .70), (H(0xBBC1CA), 1.0) }, (Color?)null, (Color?)null),
            BoardModel.Lousa => (new[] { (H(0x9A6C43), 0.0), (H(0x6B4628), .45), (H(0x8A623B), .70), (H(0x54361D), 1.0) },
                                 Color.FromArgb(13, 255, 255, 255), Color.FromArgb(18, 0, 0, 0)),
            BoardModel.Pontilhado => (new[] { (H(0xF6F6F2), 0.0), (H(0xDCDCD4), .50), (H(0xEDEDE7), .75), (H(0xCFCFC6), 1.0) }, null, null),
            _ => (new[] { (H(0xEAD1A6), 0.0), (H(0xD3AD75), .36), (H(0xE3C597), .62), (H(0xC0965C), 1.0) },
                  Color.FromArgb(15, 255, 255, 255), Color.FromArgb(13, 90, 50, 10)),
        };

        dc.DrawRoundedRectangle(BoardSurfaces.CssGradient(160, box, stops), null, box, radius, radius);
        if (stripeLight is { } l && stripeDark is { } d)
            dc.DrawRoundedRectangle(Grain(l, d), null, box, radius, radius); // veios verticais finos
        if (!FullBleed) ShadowCache.Draw(dc, box, radius, Inner);
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
/// Superfície do quadro infinito: liso com pontinhos (segue o tema), cortiça, branco, lousa ou pontilhado.
/// A textura se repete sem fim e acompanha a câmera: só a posição do pincel muda a cada quadro.
/// </summary>
public sealed class SurfaceBackground : FrameworkElement
{
    const double DotTile = 22;
    const double GridTile = 32;

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(BoardModel), typeof(SurfaceBackground), new FrameworkPropertyMetadata(BoardModel.Liso, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowGridProperty = DependencyProperty.Register(
        nameof(ShowGrid), typeof(bool), typeof(SurfaceBackground), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FullBleedProperty = DependencyProperty.Register(
        nameof(FullBleed), typeof(bool), typeof(SurfaceBackground), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public BoardModel Model { get => (BoardModel)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public bool ShowGrid { get => (bool)GetValue(ShowGridProperty); set => SetValue(ShowGridProperty, value); }
    public bool FullBleed { get => (bool)GetValue(FullBleedProperty); set => SetValue(FullBleedProperty, value); }

    /// <summary>Raio dos cantos da superfície (0 na tela cheia).</summary>
    public static double CornerRadius(BoardModel m, bool fullBleed) => 16;

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
    readonly DrawingBrush _paperDots = BoardSurfaces.DotsBrush.Clone();
    readonly DrawingBrush _gridDark = GridBrush(Color.FromArgb(34, 0, 0, 0));
    readonly DrawingBrush _gridLight = GridBrush(Color.FromArgb(40, 255, 255, 255));
    DrawingBrush _dots = DotBrush(Colors.Gray);
    DrawingBrush _grid = GridBrush(Colors.Gray);
    (double X, double Y, double Zoom) _camera = (0, 0, 1);

    public SurfaceBackground()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => { ThemeService.Changed += OnTheme; OnTheme(); };
        Unloaded += (_, _) => ThemeService.Changed -= OnTheme;
    }

    void OnTheme()
    {
        var dot = (Color)FindResource("CanvasDotColor");
        _dots = DotBrush(dot);
        _grid = GridBrush(Color.FromArgb((byte)(dot.A * 0.6), dot.R, dot.G, dot.B));
        SetCamera(_camera.X, _camera.Y, _camera.Zoom);
        InvalidateVisual();
    }

    static DrawingBrush DotBrush(Color c) => Tile(new GeometryDrawing(new SolidColorBrush(c), null,
        new EllipseGeometry(new Point(DotTile / 2, DotTile / 2), 1.15, 1.15)), DotTile);

    static DrawingBrush GridBrush(Color c) => Tile(new GeometryDrawing(null, new Pen(new SolidColorBrush(c), 1), new GeometryGroup
    {
        Children = { new LineGeometry(new Point(0, 0), new Point(GridTile, 0)), new LineGeometry(new Point(0, 0), new Point(0, GridTile)) },
    }), GridTile);

    static DrawingBrush Tile(Drawing d, double size) => new(d)
    {
        TileMode = TileMode.Tile,
        ViewportUnits = BrushMappingMode.Absolute,
        Viewbox = new Rect(0, 0, size, size),
        ViewboxUnits = BrushMappingMode.Absolute,
        Stretch = Stretch.Fill,
    };

    /// <summary>
    /// Posiciona a textura: (ox, oy) é onde a origem do mundo aparece na tela.
    /// Muda só o Viewport do pincel (barato, sem redesenhar).
    /// </summary>
    public void SetCamera(double ox, double oy, double zoom)
    {
        _camera = (ox, oy, zoom);
        Place(_cork, BoardSurfaces.CorkTile, ox, oy, zoom);
        Place(_paperDots, BoardSurfaces.DotTile, ox, oy, zoom);
        Place(_dots, DotTile, ox, oy, zoom);
        Place(_grid, GridTile, ox, oy, zoom);
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
        double r = CornerRadius(Model, FullBleed);
        var clip = new RectangleGeometry(box, r, r);
        clip.Freeze();
        dc.PushClip(clip);

        if (Model == BoardModel.Liso)
        {
            dc.DrawRectangle((Brush)FindResource("CanvasBrush"), null, box);
            dc.DrawRectangle(ShowGrid ? _grid : _dots, null, box);
            dc.Pop();
            var edge = new Pen((Brush)FindResource("CanvasEdgeBrush"), 1);
            if (edge.CanFreeze) edge.Freeze(); // durante a troca de tema o pincel está animando
            var line = box;
            line.Inflate(-0.5, -0.5);
            dc.DrawRoundedRectangle(null, edge, line, r - 0.5, r - 0.5);
            return;
        }

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
                dc.DrawRectangle(_paperDots, null, box);
                break;

            default:
                dc.DrawRectangle(_cork, null, box);
                break;
        }
        if (ShowGrid) dc.DrawRectangle(Model == BoardModel.Lousa ? _gridLight : _gridDark, null, box);
        dc.Pop();
        ShadowCache.Draw(dc, box, r, Insets[Model]);
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
