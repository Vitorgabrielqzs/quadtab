using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Quadtab.Rendering;

/// <summary>
/// Cursores próprios do app, todos no mesmo estilo: preenchimento branco, contorno preto arredondado e
/// uma sombra suave. São desenhados no tamanho exato da escala da tela (100%, 125%, 150%, 200%...),
/// por isso ficam nítidos; ao trocar de monitor com outra escala, são refeitos.
/// </summary>
public static class BoardCursors
{
    static readonly Dictionary<string, Cursor> Cache = new();
    static double _scale = 1;

    public static Cursor Arrow => Get("seta");
    // Sem mãozinhas: no quadro e em todo o app fica só a seta personalizada.
    public static Cursor Grab => Arrow;
    public static Cursor Grabbing => Arrow;
    public static Cursor IBeam => Get("texto");

    /// <summary>Escala da tela (1 = 100%). Mudou: os cursores são desenhados de novo.</summary>
    public static void SetScale(double scale)
    {
        scale = Math.Clamp(scale, 1, 4);
        if (Math.Abs(scale - _scale) < 0.01) return;
        _scale = scale;
        Cache.Clear();
    }

    /// <summary>Troca um cursor do Windows pelo equivalente do app (os outros ficam como estão).</summary>
    public static Cursor? Map(Cursor? c)
    {
        if (c is null || c == Cursors.Arrow || c == Cursors.Hand) return Arrow;
        if (c == Cursors.IBeam) return IBeam;
        if (c == Cursors.SizeNS) return Get("ns");
        if (c == Cursors.SizeWE) return Get("we");
        if (c == Cursors.SizeNWSE) return Get("nwse");
        if (c == Cursors.SizeNESW) return Get("nesw");
        if (c == Cursors.SizeAll) return Get("todos");
        return c;
    }

    static Cursor Get(string name)
    {
        if (Cache.TryGetValue(name, out var c)) return c;
        c = Create(name);
        Cache[name] = c;
        return c;
    }

    /// <summary>Folha com todos os cursores desenhados (conferência).</summary>
    internal static BitmapSource Preview(double scale)
    {
        var names = new[] { "seta", "mao", "mao-fechada", "texto", "ns", "we", "nwse", "nesw", "todos" };
        int size = (int)Math.Round(32 * scale);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xC8, 0xA0, 0x70)), null, new Rect(0, 0, size * names.Length + 8 * names.Length, size + 8));
            for (int i = 0; i < names.Length; i++) dc.DrawImage(Draw(names[i], size, out _), new Rect(4 + i * (size + 8), 4, size, size));
        }
        var bmp = new RenderTargetBitmap(size * names.Length + 8 * names.Length, size + 8, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        return bmp;
    }

    static Cursor Create(string name)
    {
        try
        {
            int size = (int)Math.Round(32 * _scale);
            var bmp = Draw(name, size, out var hot);
            return new Cursor(new MemoryStream(ToCur(bmp, (int)Math.Round(hot.X * size / 32), (int)Math.Round(hot.Y * size / 32))), true);
        }
        catch
        {
            return name switch { "mao" => Cursors.Hand, "mao-fechada" => Cursors.SizeAll, "texto" => Cursors.IBeam, _ => Cursors.Arrow };
        }
    }

    static BitmapSource Draw(string name, int size, out Point hotSpot)
    {
        {
            var (shape, hot, stroke) = name switch
            {
                "seta" => (ArrowShape(), new Point(4, 3), 1.6),
                "mao" => (OpenHand(), new Point(16, 16), 1.25),
                "mao-fechada" => (ClosedHand(), new Point(16, 16), 1.25),
                "texto" => (Beam(), new Point(16, 16), 1.2),
                "ns" => (DoubleArrow(90), new Point(16, 16), 1.2),
                "we" => (DoubleArrow(0), new Point(16, 16), 1.2),
                "nwse" => (DoubleArrow(45), new Point(16, 16), 1.2),
                "nesw" => (DoubleArrow(-45), new Point(16, 16), 1.2),
                _ => (Cross(), new Point(16, 16), 1.2),
            };

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(size / 32.0, size / 32.0));
                // sombra suave (três camadas deslocadas, sem efeito de desfoque)
                foreach (var (dx, dy, a) in new[] { (0.6, 1.4, 22), (0.9, 1.9, 16), (1.2, 2.5, 10) })
                {
                    dc.PushTransform(new TranslateTransform(dx, dy));
                    dc.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)a, 0, 0, 0)),
                        new Pen(new SolidColorBrush(Color.FromArgb((byte)a, 0, 0, 0)), stroke + 1.2) { LineJoin = PenLineJoin.Round }, shape);
                    dc.Pop();
                }
                var pen = new Pen(Brushes.Black, stroke) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawGeometry(Brushes.White, pen, shape);
                dc.Pop();
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            hotSpot = hot;
            return bmp;
        }
    }

    // Seta da imagem de referência: ponta no alto à esquerda, aba à direita, entalhe e ponta de baixo.
    static Geometry ArrowShape() => Geometry.Parse("M4,3 L24,9.4 L14.6,13.6 L10,23.4 Z");

    static Geometry R(double x0, double y0, double x1, double y1, double r, double angle = 0, double cx = 0, double cy = 0)
    {
        var g = new RectangleGeometry(new Rect(x0, y0, x1 - x0, y1 - y0), r, r);
        if (angle != 0) g.Transform = new RotateTransform(angle, cx, cy);
        return g;
    }

    static Geometry Union(params Geometry[] parts)
    {
        Geometry acc = parts[0];
        for (int i = 1; i < parts.Length; i++) acc = Geometry.Combine(acc, parts[i], GeometryCombineMode.Union, null);
        return acc;
    }

    static Geometry OpenHand() => Union(
        R(9, 14, 23, 26, 5),               // palma
        R(9.4, 6.5, 12.4, 17, 1.5),        // indicador
        R(12.7, 4.8, 15.7, 16, 1.5),       // médio
        R(16.0, 5.8, 19.0, 16, 1.5),       // anelar
        R(19.3, 8.5, 22.1, 17, 1.4),       // mindinho
        R(7.6, 12, 10.8, 23.5, 1.6, -35, 9.2, 23));    // polegar

    static Geometry ClosedHand() => Union(
        R(9, 12.5, 23, 25.5, 5),
        R(9.3, 10.2, 12.3, 15.5, 1.5),
        R(12.6, 9.6, 15.6, 15, 1.5),
        R(15.9, 10.0, 18.9, 15, 1.5),
        R(19.2, 11.2, 22.0, 16, 1.4),
        R(6.6, 14.2, 10.4, 19.4, 1.8, -20, 10, 18));

    // Barra de texto: haste com serifas.
    static Geometry Beam() => Union(R(14.8, 7, 17.2, 25, 0.6), R(11.5, 6, 20.5, 8.4, 1.2), R(11.5, 23.6, 20.5, 26, 1.2));

    // Seta dupla de redimensionar, girada.
    static Geometry DoubleArrow(double angle)
    {
        var g = Geometry.Parse("M5,16 L11,10 L11,14 L21,14 L21,10 L27,16 L21,22 L21,18 L11,18 L11,22 Z").Clone();
        g.Transform = new RotateTransform(angle, 16, 16);
        return g;
    }

    static Geometry Cross() => Union(DoubleArrow(0), DoubleArrow(90));

    /// <summary>Monta um arquivo .cur (bitmap 32 bits + máscara) com o ponto ativo informado.</summary>
    static byte[] ToCur(BitmapSource bmp, int hotX, int hotY)
    {
        int w = bmp.PixelWidth, h = bmp.PixelHeight;
        var px = new byte[w * h * 4];
        bmp.CopyPixels(px, w * 4, 0);

        // Desfaz o alfa pré-multiplicado.
        for (int i = 0; i < px.Length; i += 4)
        {
            int a = px[i + 3];
            if (a is > 0 and < 255)
                for (int c = 0; c < 3; c++) px[i + c] = (byte)Math.Min(255, px[i + c] * 255 / a);
        }

        int maskStride = ((w + 31) / 32) * 4;
        int imageSize = 40 + w * h * 4 + maskStride * h;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((short)0); bw.Write((short)2); bw.Write((short)1);           // cabeçalho: cursor, 1 imagem
        bw.Write((byte)(w >= 256 ? 0 : w)); bw.Write((byte)(h >= 256 ? 0 : h));
        bw.Write((byte)0); bw.Write((byte)0);
        bw.Write((short)hotX); bw.Write((short)hotY);
        bw.Write(imageSize); bw.Write(22);
        bw.Write(40); bw.Write(w); bw.Write(h * 2); bw.Write((short)1); bw.Write((short)32);  // BITMAPINFOHEADER
        bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
        for (int y = h - 1; y >= 0; y--) bw.Write(px, y * w * 4, w * 4);                  // de baixo para cima
        bw.Write(new byte[maskStride * h]);                                                   // máscara AND vazia
        bw.Flush();
        return ms.ToArray();
    }
}
