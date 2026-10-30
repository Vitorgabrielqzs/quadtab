using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Quadtab.Models;

namespace Quadtab.Rendering;

/// <summary>
/// Texturas e pincéis do quadro, criados uma vez e compartilhados (congelados = mais rápidos no WPF).
/// Também guarda o modelo e o zoom atuais, que alguns itens precisam conhecer.
/// </summary>
public static class BoardSurfaces
{
    public const double CorkTile = 384, GrainTile = 256, DotTile = 26;

    static BitmapSource? _cork, _grain, _blurredCork, _glassCork;
    static ImageBrush? _corkBrush, _grainBrush;
    static DrawingBrush? _dots;
    static Color? _corkAverage;

    public static BitmapSource Cork => _cork ??= Load("Assets/cork.png");
    public static BitmapSource Grain => _grain ??= Load("Assets/paper-grain.png");

    public static ImageBrush CorkBrush => _corkBrush ??= Tile(Cork, CorkTile);
    public static ImageBrush GrainBrush => _grainBrush ??= Tile(Grain, GrainTile);

    /// <summary>Pontos #C2C2B8 de raio 1.3 a cada 26 px (modelo Pontilhado).</summary>
    public static DrawingBrush DotsBrush => _dots ??= CreateDots();

    /// <summary>Cortiça já desfocada e saturada: é o "fundo" visto através dos blocos de vidro.</summary>
    public static BitmapSource BlurredCork => _blurredCork ??= CreateBlurredCork(12, 1.7);

    /// <summary>Cortiça como aparece através dos painéis de vidro: blur 26 e saturação 190%.</summary>
    public static BitmapSource GlassCork => _glassCork ??= CreateBlurredCork(26, 1.9);

    public static Color CorkAverage { get { _ = BlurredCork; return _corkAverage!.Value; } }

    // ---- estado atual do quadro ----
    public static BoardModel Model { get; private set; }

    /// <summary>Tamanho da peça de cortiça em unidades do mundo (muda só ao trocar de "nível" de zoom).</summary>
    public static double CorkWorldTile { get; private set; } = CorkTile;

    /// <summary>Avisa quando o modelo ou o nível da textura muda (não a cada quadro de zoom).</summary>
    public static event Action? Changed;

    public static void Update(BoardModel model, double zoom)
    {
        TileOnScreen(CorkTile, zoom, out double world);
        if (model == Model && world == CorkWorldTile) return;
        Model = model;
        CorkWorldTile = world;
        Changed?.Invoke();
    }

    /// <summary>
    /// A textura acompanha o zoom, mas em "níveis" (como a grade do Freeform): quando fica pequena demais
    /// ela dobra de tamanho, e vice-versa. Assim nunca vira chuvisco nem fica borrada.
    /// </summary>
    public static double TileOnScreen(double baseSize, double zoom, out double worldTile)
    {
        double s = baseSize * zoom;
        while (s < baseSize * 0.67) s *= 2;
        while (s >= baseSize * 1.34) s /= 2;
        worldTile = Math.Round(s / zoom, 6);
        return s;
    }

    /// <summary>Cor média da superfície (base do vidro quando a borda do desfoque sai do quadro).</summary>
    public static Color AverageColor(BoardModel m) => m switch
    {
        BoardModel.Branco => Color.FromRgb(0xF3, 0xF5, 0xF8),
        BoardModel.Lousa => Color.FromRgb(0x2E, 0x4A, 0x3A),
        BoardModel.Pontilhado => Color.FromRgb(0xF7, 0xF7, 0xF3),
        BoardModel.Liso => Colors.White,
        _ => CorkAverage,
    };

    /// <summary>Fundo desfocado para um bloco de vidro (cortiça: textura desfocada alinhada ao quadro).</summary>
    public static Brush BlurredSurfaceFor(BoardModel m, double itemX, double itemY)
    {
        if (m != BoardModel.Cortica)
            return ShadowCache.BrushOf(ColorMath.Saturate(AverageColor(m), 1.7));
        double t = CorkWorldTile; // a textura está presa à origem do mundo
        var b = new ImageBrush(BlurredCork)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(-itemX, -itemY, t, t),
            Stretch = Stretch.Fill,
        };
        b.Freeze();
        return b;
    }

    // ---------- criação ----------

    static BitmapSource Load(string path)
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.UriSource = new Uri("pack://application:,,,/" + path);
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.EndInit();
        img.Freeze();
        return img;
    }

    static ImageBrush Tile(ImageSource src, double size)
    {
        var b = new ImageBrush(src)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, size, size),
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetCachingHint(b, CachingHint.Cache);
        b.Freeze();
        return b;
    }

    static DrawingBrush CreateDots()
    {
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, DotTile, DotTile));
            dc.DrawEllipse(ColorMath.Brush(Color.FromRgb(0xC2, 0xC2, 0xB8)), null, new Point(13, 13), 1.45, 1.45);
        }
        var b = new DrawingBrush(g)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, DotTile, DotTile),
            Viewbox = new Rect(0, 0, DotTile, DotTile),
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetCachingHint(b, CachingHint.Cache);
        b.Freeze();
        return b;
    }

    static BitmapSource CreateBlurredCork(double sigma, double saturation)
    {
        var src = new FormatConvertedBitmap(Cork, PixelFormats.Bgra32, null, 0);
        int w = src.PixelWidth, h = src.PixelHeight, n = w * h;
        var px = new byte[n * 4];
        src.CopyPixels(px, w * 4, 0);

        var ch = new float[3][];
        double sr = 0, sg = 0, sb = 0;
        for (int c = 0; c < 3; c++)
        {
            ch[c] = new float[n];
            for (int i = 0; i < n; i++) ch[c][i] = px[i * 4 + c];
            // A textura repete sem emenda, então o desfoque "dá a volta" nas bordas.
            ShadowCache.GaussBlur(ch[c], w, h, sigma, wrap: true);
        }
        var outPx = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            sb += px[i * 4]; sg += px[i * 4 + 1]; sr += px[i * 4 + 2];
            var col = ColorMath.Saturate(Color.FromRgb(B(ch[2][i]), B(ch[1][i]), B(ch[0][i])), saturation);
            outPx[i * 4] = col.B; outPx[i * 4 + 1] = col.G; outPx[i * 4 + 2] = col.R; outPx[i * 4 + 3] = 255;
        }
        _corkAverage = Color.FromRgb((byte)(sr / n), (byte)(sg / n), (byte)(sb / n));

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, outPx, w * 4);
        bmp.Freeze();
        return bmp;
    }

    static byte B(float v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    /// <summary>Pontos de um linear-gradient do CSS com ângulo em graus, para uma área de tamanho s.</summary>
    public static (Point Start, Point End) CssAngle(double deg, double x, double y, double w, double h)
    {
        double a = deg * Math.PI / 180;
        double dx = Math.Sin(a), dy = -Math.Cos(a);
        double len = Math.Abs(w * dx) + Math.Abs(h * dy);
        var c = new Point(x + w / 2, y + h / 2);
        return (new Point(c.X - dx * len / 2, c.Y - dy * len / 2), new Point(c.X + dx * len / 2, c.Y + dy * len / 2));
    }

    public static LinearGradientBrush CssGradient(double deg, Rect area, params (Color Color, double Offset)[] stops)
    {
        var (s, e) = CssAngle(deg, area.X, area.Y, area.Width, area.Height);
        var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = s, EndPoint = e };
        foreach (var (c, o) in stops) b.GradientStops.Add(new GradientStop(c, o));
        b.Freeze();
        return b;
    }
}
