using System.Globalization;
using System.IO;
using System.Text;
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

    /// <summary>PDF mínimo: uma página com a imagem em JPEG (qualidade 92), em pontos (1/72 pol.).</summary>
    public static void SavePdf(BitmapSource image, string path)
    {
        // Sem transparência no PDF: a imagem vai sobre branco.
        var flat = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
        var enc = new JpegBitmapEncoder { QualityLevel = 92 };
        enc.Frames.Add(BitmapFrame.Create(flat));
        byte[] jpeg;
        using (var ms = new MemoryStream()) { enc.Save(ms); jpeg = ms.ToArray(); }

        int pw = image.PixelWidth, ph = image.PixelHeight;
        // Página com no máximo 1190 pt no lado maior (≈ A3), mantendo a proporção.
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
        Ascii($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {pw} /Height {ph} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
        outp.Write(jpeg);
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
}
