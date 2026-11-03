using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Quadtab.Services;

/// <summary>Exporta uma imagem do quadro como PNG ou como PDF (uma página do tamanho da imagem).</summary>
public static class ExportService
{
    public static void SavePng(BitmapSource image, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(image));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>
    /// PDF mínimo: uma página com a imagem sem perda (Flate com os filtros do PNG, então fica do tamanho de um PNG),
    /// em pontos (1/72 pol.). Sem JPEG: as letras ficam tão nítidas quanto na imagem.
    /// </summary>
    public static void SavePdf(BitmapSource image, string path)
    {
        // Sem transparência no PDF (o fundo do quadro já cobre tudo).
        var rgb = new FormatConvertedBitmap(image, PixelFormats.Rgb24, null, 0);
        int pw = rgb.PixelWidth, ph = rgb.PixelHeight;
        byte[] pixels = Deflate(rgb);

        // Página com no máximo 1190 pt no lado maior (≈ A3), mantendo a proporção; a imagem continua com todos os pixels.
        double k = Math.Min(1, 1190.0 / Math.Max(pw, ph));
        double w = Math.Round(pw * k, 2), h = Math.Round(ph * k, 2);
        string W(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        var content = Encoding.ASCII.GetBytes($"q {W(w)} 0 0 {W(h)} 0 0 cm /Im0 Do Q");
        using var outp = new MemoryStream();
        var offsets = new List<long>();
        void Ascii(string s) { var b = Encoding.Latin1.GetBytes(s); outp.Write(b); }
        void Obj(string body)
        {
            offsets.Add(outp.Position);
            Ascii($"{offsets.Count} 0 obj\n{body}\nendobj\n");
        }

        Ascii("%PDF-1.4\n%âãÏÓ\n");
        Obj("<< /Type /Catalog /Pages 2 0 R >>");
        Obj("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Obj($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {W(w)} {W(h)}] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>");
        offsets.Add(outp.Position);
        Ascii($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {pw} /Height {ph} /ColorSpace /DeviceRGB /BitsPerComponent 8 " +
              $"/Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors 3 /BitsPerComponent 8 /Columns {pw} >> /Length {pixels.Length} >>\nstream\n");
        outp.Write(pixels);
        Ascii("\nendstream\nendobj\n");
        offsets.Add(outp.Position);
        Ascii($"5 0 obj\n<< /Length {content.Length} >>\nstream\n");
        outp.Write(content);
        Ascii("\nendstream\nendobj\n");

        long xref = outp.Position;
        var sb = new StringBuilder();
        sb.Append($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append($"{o:0000000000} 00000 n \n");
        sb.Append($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Ascii(sb.ToString());
        File.WriteAllBytes(path, outp.ToArray());
    }

    /// <summary>
    /// Pixels RGB comprimidos como no PNG: cada linha ganha o filtro que deixa os números menores (nenhum, Sub, Up,
    /// Média ou Paeth) e tudo passa pelo zlib. Lê a imagem em faixas, para não dobrar a memória numa imagem grande.
    /// </summary>
    static byte[] Deflate(BitmapSource rgb)
    {
        int w = rgb.PixelWidth, h = rgb.PixelHeight, stride = w * 3;
        const int Band = 256;
        var band = new byte[stride * Math.Min(Band, h)];
        var prev = new byte[stride];
        var cur = new byte[stride];
        var filtered = new byte[5][];
        for (int f = 0; f < 5; f++) filtered[f] = new byte[stride];

        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (int y0 = 0; y0 < h; y0 += Band)
            {
                int rows = Math.Min(Band, h - y0);
                rgb.CopyPixels(new Int32Rect(0, y0, w, rows), band, stride, 0);
                for (int r = 0; r < rows; r++)
                {
                    Buffer.BlockCopy(band, r * stride, cur, 0, stride);
                    int best = 0;
                    long bestSum = long.MaxValue;
                    for (int f = 0; f < 5; f++)
                    {
                        var o = filtered[f];
                        long sum = 0;
                        for (int i = 0; i < stride; i++)
                        {
                            int a = i >= 3 ? cur[i - 3] : 0, b = prev[i], c = i >= 3 ? prev[i - 3] : 0;
                            int p = f switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) >> 1, _ => Paeth(a, b, c) };
                            byte v = (byte)(cur[i] - p);
                            o[i] = v;
                            sum += v < 128 ? v : 256 - v;
                        }
                        if (sum < bestSum) { bestSum = sum; best = f; }
                    }
                    z.WriteByte((byte)best);
                    z.Write(filtered[best]);
                    (prev, cur) = (cur, prev);
                }
            }
        }
        return ms.ToArray();
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
