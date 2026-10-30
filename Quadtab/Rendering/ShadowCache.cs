using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Quadtab.Rendering;

/// <summary>Uma camada de sombra no formato do CSS: x y desfoque espalhamento cor [inset].</summary>
public readonly record struct ShadowLayer(double X, double Y, double Blur, double Spread, Color Color, bool Inset);

public static class ShadowSpec
{
    static readonly Dictionary<string, ShadowLayer[]> Cache = new();

    /// <summary>Lê "0 14 36 0 #1A1E243C, inset 0 2 1 0 #99FFFFFF" (cores em #AARRGGBB).</summary>
    public static ShadowLayer[] Parse(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return [];
        if (Cache.TryGetValue(spec, out var cached)) return cached;

        var list = new List<ShadowLayer>();
        var n = new double[4];
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            bool inset = false;
            var color = Colors.Black;
            Array.Clear(n);
            int count = 0;
            foreach (var t in part.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (t == "inset") inset = true;
                else if (t[0] == '#') color = (Color)ColorConverter.ConvertFromString(t);
                else if (count < 4) n[count++] = double.Parse(t, CultureInfo.InvariantCulture);
            }
            list.Add(new ShadowLayer(n[0], n[1], n[2], n[3], color, inset));
        }
        var arr = list.ToArray();
        Cache[spec] = arr;
        return arr;
    }
}

/// <summary>
/// Sombras suaves sem Effect do WPF (que pesa muito): o desfoque é calculado uma única vez,
/// guardado em pequenos bitmaps e reaproveitado por todos os elementos com a mesma sombra.
/// Formas grandes usam "9 fatias" (cantos + bordas esticadas), então redimensionar não custa nada.
/// </summary>
public static class ShadowCache
{
    sealed class Slices
    {
        public required BitmapSource[] Pieces; // TL, T, TR, L, R, BL, B, BR
        public int Corner;
    }

    static readonly Dictionary<(int, int, uint, bool), Slices> SliceCache = new();
    static readonly Dictionary<(int, int, int, int, uint, bool), (BitmapSource Bmp, int Pad)> ExactCache = new();
    static readonly Dictionary<uint, SolidColorBrush> BrushCache = new();
    const int MaxExact = 128;

    public static void Draw(DrawingContext dc, Rect box, double radius, ShadowLayer[] layers, bool hollow = false)
    {
        // Como no CSS: a primeira sombra da lista fica por cima.
        for (int i = layers.Length - 1; i >= 0; i--)
        {
            if (layers[i].Inset) DrawInset(dc, box, radius, layers[i]);
            else DrawOuter(dc, box, radius, layers[i], hollow);
        }
    }

    public static SolidColorBrush BrushOf(Color c)
    {
        uint k = Key(c);
        if (!BrushCache.TryGetValue(k, out var b))
        {
            b = new SolidColorBrush(c);
            b.Freeze();
            BrushCache[k] = b;
        }
        return b;
    }

    static void DrawOuter(DrawingContext dc, Rect box, double radius, in ShadowLayer L, bool hollow)
    {
        var shape = new Rect(box.X + L.X - L.Spread, box.Y + L.Y - L.Spread,
                             box.Width + 2 * L.Spread, box.Height + 2 * L.Spread);
        if (shape.Width <= 0 || shape.Height <= 0) return;
        double r = radius > 0 ? Math.Max(0, radius + L.Spread) : 0;
        r = Math.Min(r, Math.Min(shape.Width, shape.Height) / 2);
        double sigma = L.Blur / 2;

        if (hollow)
        {
            // Não desenha embaixo da própria caixa (importante sob o vidro translúcido).
            var outer = Rect.Union(box, shape);
            outer.Inflate(3 * sigma + 4, 3 * sigma + 4);
            double br = Math.Min(radius, Math.Min(box.Width, box.Height) / 2);
            dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(outer), new RectangleGeometry(box, br, br)));
        }

        if (sigma < 0.25)
        {
            dc.DrawRoundedRectangle(BrushOf(L.Color), null, shape, r, r);
        }
        else
        {
            int P = (int)Math.Ceiling(3 * sigma);
            int I = (int)Math.Ceiling(r) + P;
            if (shape.Width >= 2 * I && shape.Height >= 2 * I)
            {
                var d = new Rect(shape.X - P, shape.Y - P, shape.Width + 2 * P, shape.Height + 2 * P);
                var g = AliasedGroup(out var gc);
                DrawNine(gc, GetSlices(r, sigma, L.Color, false), d, L.Color, center: true);
                gc.Close();
                dc.DrawDrawing(g);
            }
            else
            {
                var (bmp, pad) = GetExact(shape.Width, shape.Height, r, sigma, L.Color, false);
                dc.DrawImage(bmp, new Rect(shape.X - pad, shape.Y - pad, bmp.PixelWidth, bmp.PixelHeight));
            }
        }

        if (hollow) dc.Pop();
    }

    static void DrawInset(DrawingContext dc, Rect box, double radius, in ShadowLayer L)
    {
        var hole = new Rect(box.X + L.X + L.Spread, box.Y + L.Y + L.Spread,
                            Math.Max(0, box.Width - 2 * L.Spread), Math.Max(0, box.Height - 2 * L.Spread));
        double br = Math.Min(radius, Math.Min(box.Width, box.Height) / 2);
        double rh = Math.Min(Math.Max(0, radius - L.Spread), Math.Min(hole.Width, hole.Height) / 2);
        var brush = BrushOf(L.Color);
        double sigma = L.Blur / 2;

        dc.PushClip(new RectangleGeometry(box, br, br));
        var big = Rect.Union(box, hole);
        big.Inflate(Math.Abs(L.X) + Math.Abs(L.Y) + 3 * sigma + 4, Math.Abs(L.X) + Math.Abs(L.Y) + 3 * sigma + 4);

        if (hole.Width <= 0 || hole.Height <= 0)
        {
            dc.DrawRectangle(brush, null, box);
        }
        else if (sigma < 0.25)
        {
            dc.DrawGeometry(brush, null, new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(big), new RectangleGeometry(hole, rh, rh)));
        }
        else
        {
            int P = (int)Math.Ceiling(3 * sigma);
            int I = (int)Math.Ceiling(rh) + P;
            var g = AliasedGroup(out var gc);
            Rect d;
            if (hole.Width >= 2 * I && hole.Height >= 2 * I)
            {
                d = new Rect(hole.X - P, hole.Y - P, hole.Width + 2 * P, hole.Height + 2 * P);
                DrawNine(gc, GetSlices(rh, sigma, L.Color, true), d, L.Color, center: false);
            }
            else
            {
                var (bmp, pad) = GetExact(hole.Width, hole.Height, rh, sigma, L.Color, true);
                d = new Rect(hole.X - pad, hole.Y - pad, bmp.PixelWidth, bmp.PixelHeight);
                gc.DrawImage(bmp, d);
            }
            // Fora da área calculada a sombra interna é cor sólida.
            gc.DrawGeometry(brush, null, new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(Rect.Union(big, d)), new RectangleGeometry(d)));
            gc.Close();
            dc.DrawDrawing(g);
        }
        dc.Pop();
    }

    static DrawingGroup AliasedGroup(out DrawingContext gc)
    {
        var g = new DrawingGroup();
        RenderOptions.SetEdgeMode(g, EdgeMode.Aliased); // fatias encostadas sem linhas finas entre elas
        gc = g.Open();
        return g;
    }

    static void DrawNine(DrawingContext gc, Slices s, Rect d, Color color, bool center)
    {
        double c = Math.Min(s.Corner, Math.Min(d.Width, d.Height) / 2);
        double x0 = d.X, x1 = d.X + c, x2 = d.Right - c, x3 = d.Right;
        double y0 = d.Y, y1 = d.Y + c, y2 = d.Bottom - c, y3 = d.Bottom;
        var p = s.Pieces;
        gc.DrawImage(p[0], R(x0, y0, x1, y1));
        gc.DrawImage(p[2], R(x2, y0, x3, y1));
        gc.DrawImage(p[5], R(x0, y2, x1, y3));
        gc.DrawImage(p[7], R(x2, y2, x3, y3));
        if (x2 > x1)
        {
            gc.DrawImage(p[1], R(x1, y0, x2, y1));
            gc.DrawImage(p[6], R(x1, y2, x2, y3));
        }
        if (y2 > y1)
        {
            gc.DrawImage(p[3], R(x0, y1, x1, y2));
            gc.DrawImage(p[4], R(x2, y1, x3, y2));
        }
        if (center && x2 > x1 && y2 > y1) gc.DrawRectangle(BrushOf(color), null, R(x1, y1, x2, y2));
    }

    static Rect R(double x0, double y0, double x1, double y1) => new(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));

    // ---------- geração dos bitmaps ----------

    static Slices GetSlices(double r, double sigma, Color color, bool inset)
    {
        var key = ((int)Math.Round(r * 2), (int)Math.Round(sigma * 4), Key(color), inset);
        if (SliceCache.TryGetValue(key, out var s)) return s;

        r = key.Item1 / 2.0;
        sigma = key.Item2 / 4.0;
        int P = (int)Math.Ceiling(3 * sigma);
        int I = (int)Math.Ceiling(r) + P;
        int side = 2 * I + 2;           // forma com 2 px retos no meio
        int n = side + 2 * P;
        int c = P + I;
        var field = Field(n, n, P, P, side, side, r, sigma, inset);
        var bmp = ToBitmap(field, n, n, color);

        BitmapSource Crop(int x, int y, int w, int h)
        {
            var cb = new CroppedBitmap(bmp, new Int32Rect(x, y, w, h));
            cb.Freeze();
            return cb;
        }
        int m = n - 2 * c; // = 2
        s = new Slices
        {
            Corner = c,
            Pieces =
            [
                Crop(0, 0, c, c), Crop(c, 0, m, c), Crop(n - c, 0, c, c),
                Crop(0, c, c, m), Crop(n - c, c, c, m),
                Crop(0, n - c, c, c), Crop(c, n - c, m, c), Crop(n - c, n - c, c, c),
            ],
        };
        SliceCache[key] = s;
        return s;
    }

    static (BitmapSource, int) GetExact(double w, double h, double r, double sigma, Color color, bool inset)
    {
        var key = ((int)Math.Round(w * 2), (int)Math.Round(h * 2), (int)Math.Round(r * 2), (int)Math.Round(sigma * 4), Key(color), inset);
        if (ExactCache.TryGetValue(key, out var e)) return e;

        w = key.Item1 / 2.0; h = key.Item2 / 2.0; r = key.Item3 / 2.0; sigma = key.Item4 / 4.0;
        int P = (int)Math.Ceiling(3 * sigma);
        int cw = (int)Math.Ceiling(w) + 2 * P, ch = (int)Math.Ceiling(h) + 2 * P;
        var field = Field(cw, ch, P, P, w, h, r, sigma, inset);
        e = (ToBitmap(field, cw, ch, color), P);
        if (ExactCache.Count >= MaxExact) ExactCache.Clear(); // cada tamanho novo de item criaria mais um bitmap
        ExactCache[key] = e;
        return e;
    }

    /// <summary>Cobertura (com antisserrilhado) de um retângulo arredondado, depois desfocada.</summary>
    static float[] Field(int cw, int ch, double sx, double sy, double sw, double sh, double r, double sigma, bool inset)
    {
        var a = new float[cw * ch];
        double cx = sx + sw / 2, cy = sy + sh / 2, hx = sw / 2 - r, hy = sh / 2 - r;
        for (int y = 0; y < ch; y++)
        {
            double py = Math.Abs(y + 0.5 - cy) - hy;
            for (int x = 0; x < cw; x++)
            {
                double px = Math.Abs(x + 0.5 - cx) - hx;
                double ox = Math.Max(px, 0), oy = Math.Max(py, 0);
                double d = Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(px, py), 0) - r;
                double cov = Math.Clamp(0.5 - d, 0, 1);
                a[y * cw + x] = (float)(inset ? 1 - cov : cov);
            }
        }
        GaussBlur(a, cw, ch, sigma);
        return a;
    }

    /// <summary>Desfoque gaussiano aproximado por 3 desfoques de caixa (rápido e independente do raio).</summary>
    public static void GaussBlur(float[] a, int w, int h, double sigma, bool wrap = false)
    {
        var tmp = new float[a.Length];
        foreach (int r in Boxes(sigma))
        {
            if (r <= 0) continue;
            BoxH(a, tmp, w, h, r, wrap);
            BoxV(tmp, a, w, h, r, wrap);
        }
    }

    static int[] Boxes(double sigma)
    {
        const int n = 3;
        double wIdeal = Math.Sqrt(12 * sigma * sigma / n + 1);
        int wl = (int)Math.Floor(wIdeal);
        if (wl % 2 == 0) wl--;
        int wu = wl + 2;
        double mIdeal = (12 * sigma * sigma - n * wl * wl - 4 * n * wl - 3 * n) / (-4.0 * wl - 4);
        int m = (int)Math.Round(mIdeal);
        var res = new int[n];
        for (int i = 0; i < n; i++) res[i] = ((i < m ? wl : wu) - 1) / 2;
        return res;
    }

    static int Idx(int i, int n, bool wrap) => wrap ? ((i % n) + n) % n : Math.Clamp(i, 0, n - 1);

    static void BoxH(float[] src, float[] dst, int w, int h, int r, bool wrap)
    {
        float inv = 1f / (2 * r + 1);
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            float acc = 0;
            for (int k = -r; k <= r; k++) acc += src[row + Idx(k, w, wrap)];
            for (int x = 0; x < w; x++)
            {
                dst[row + x] = acc * inv;
                acc += src[row + Idx(x + r + 1, w, wrap)] - src[row + Idx(x - r, w, wrap)];
            }
        }
    }

    static void BoxV(float[] src, float[] dst, int w, int h, int r, bool wrap)
    {
        float inv = 1f / (2 * r + 1);
        for (int x = 0; x < w; x++)
        {
            float acc = 0;
            for (int k = -r; k <= r; k++) acc += src[Idx(k, h, wrap) * w + x];
            for (int y = 0; y < h; y++)
            {
                dst[y * w + x] = acc * inv;
                acc += src[Idx(y + r + 1, h, wrap) * w + x] - src[Idx(y - r, h, wrap) * w + x];
            }
        }
    }

    static BitmapSource ToBitmap(float[] field, int w, int h, Color c)
    {
        var px = new byte[w * h * 4];
        double ca = c.A / 255.0;
        for (int i = 0; i < field.Length; i++)
        {
            double a = Math.Clamp(field[i], 0, 1) * ca;
            int o = i * 4;
            px[o] = (byte)Math.Round(c.B * a);
            px[o + 1] = (byte)Math.Round(c.G * a);
            px[o + 2] = (byte)Math.Round(c.R * a);
            px[o + 3] = (byte)Math.Round(255 * a);
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4);
        bmp.Freeze();
        return bmp;
    }

    static uint Key(Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
}
