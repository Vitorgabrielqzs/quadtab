using System.Windows;
using System.Windows.Media;
using Quadtab.Models;
using Quadtab.Rendering;
using Quadtab.Services;

namespace Quadtab.Controls;

/// <summary>
/// Borda do quadro no estilo do Canvas do Obsidian: um painel liso com cantos levemente arredondados e um
/// fio de 1 px (sem moldura de madeira nem sombras).
/// </summary>
public sealed class FrameBackground : FrameworkElement
{
    public const double Radius = 16;

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(BoardModel), typeof(FrameBackground), new FrameworkPropertyMetadata(BoardModel.Cortica));

    public FrameBackground()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => ThemeService.Changed += InvalidateVisual;
        Unloaded += (_, _) => ThemeService.Changed -= InvalidateVisual;
    }

    /// <summary>Mantido só por compatibilidade com quadros antigos: o visual é o mesmo para todos.</summary>
    public BoardModel Model { get => (BoardModel)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => default;

    protected override void OnRender(DrawingContext dc)
    {
        var box = new Rect(RenderSize);
        if (box.Width <= 1 || box.Height <= 1) return;
        var fill = (Brush)FindResource("CanvasBrush");
        var edge = new Pen((Brush)FindResource("CanvasEdgeBrush"), 1);
        if (edge.CanFreeze) edge.Freeze(); // durante a troca de tema o pincel está animando
        box.Inflate(-0.5, -0.5);
        dc.DrawRoundedRectangle(fill, edge, box, Radius, Radius);
    }
}

/// <summary>
/// Superfície do quadro infinito no estilo do Canvas do Obsidian: fundo liso com pontinhos (e grade
/// opcional). A textura se repete sem fim e acompanha a câmera: só a posição do pincel muda a cada
/// quadro, nada é redesenhado.
/// </summary>
public sealed class SurfaceBackground : FrameworkElement
{
    public const double Radius = 15;
    const double DotTile = 22;
    const double GridTile = 32;

    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(BoardModel), typeof(SurfaceBackground), new FrameworkPropertyMetadata(BoardModel.Cortica));

    public static readonly DependencyProperty ShowGridProperty = DependencyProperty.Register(
        nameof(ShowGrid), typeof(bool), typeof(SurfaceBackground), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool ShowGrid { get => (bool)GetValue(ShowGridProperty); set => SetValue(ShowGridProperty, value); }

    /// <summary>Mantido só por compatibilidade com quadros antigos.</summary>
    public BoardModel Model { get => (BoardModel)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }

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
        Place(_dots, DotTile, ox, oy, zoom);
        Place(_grid, GridTile, ox, oy, zoom);
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
        dc.DrawRectangle((Brush)FindResource("CanvasBrush"), null, box);
        dc.DrawRectangle(ShowGrid ? _grid : _dots, null, box);
        dc.Pop();
    }
}
