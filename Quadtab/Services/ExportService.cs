using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Quadtab.Services;

/// <summary>Aqui usamos a classe ExportService para exportar uma imagem do quadro como PNG ou como PDF (uma página do tamanho da imagem).</summary>
public static class ExportService
{
    /// <summary>Grava o PNG (sem canal alfa nas imagens grandes de exportação).</summary>
    public static void SavePng(BitmapSource image, string path)
    {
        var enc = new PngBitmapEncoder();
        // Exportação: sem transparência (o fundo do quadro cobre tudo), arquivo menor. Miniaturas mantêm o alfa.
        BitmapSource frame = image.PixelWidth * image.PixelHeight > 1_000_000 ? new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0) : image;
        enc.Frames.Add(BitmapFrame.Create(frame));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>
    /// PDF com a página numa imagem só, em JPEG de alta qualidade. Os leitores de PDF decodificam JPEG muito rápido
    /// (o PDF abre liso) e, sendo uma imagem só, não há emendas: cortada em blocos, o leitor arredondava a posição
    /// de cada bloco para o pixel da tela e as letras na divisa saíam cortadas.
    /// </summary>
    public static void SavePdf(BitmapSource image, string path)
    {
        var rgb = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
        var enc = new JpegBitmapEncoder { QualityLevel = 95 };
        enc.Frames.Add(BitmapFrame.Create(rgb));
        byte[] jpeg;
        using (var ms = new MemoryStream()) { enc.Save(ms); jpeg = ms.ToArray(); }
        int pw = rgb.PixelWidth, ph = rgb.PixelHeight;

        // Página com no máximo 1190 pt no lado maior (≈ A3), mantendo a proporção; a imagem continua com todos os pixels.
        double k = Math.Min(1, 1190.0 / Math.Max(pw, ph));
        static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        double w = pw * k, h = ph * k;
        var content = Encoding.ASCII.GetBytes($"q {N(w)} 0 0 {N(h)} 0 0 cm /Im0 Do Q");

        using var fs = File.Create(path);
        using var outp = new BufferedStream(fs, 1 << 20);
        long pos = 0;
        var offsets = new List<long>();
        void Bytes(byte[] b) { outp.Write(b); pos += b.Length; }
        void Ascii(string s) => Bytes(Encoding.Latin1.GetBytes(s));

        // ---------- escreve o PDF à mão (formato mínimo) ----------
        // Objetos: 1 catálogo, 2 páginas, 3 página, 4 imagem, 5 desenho.
        Ascii("%PDF-1.4\n%âãÏÓ\n");
        offsets.Add(pos); Ascii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets.Add(pos); Ascii("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        offsets.Add(pos);
        Ascii($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(w)} {N(h)}] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>\nendobj\n");
        offsets.Add(pos);
        Ascii($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {pw} /Height {ph} /ColorSpace /DeviceRGB /BitsPerComponent 8 " +
              $"/Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
        Bytes(jpeg);
        Ascii("\nendstream\nendobj\n");
        offsets.Add(pos);
        Ascii($"5 0 obj\n<< /Length {content.Length} >>\nstream\n");
        Bytes(content);
        Ascii("\nendstream\nendobj\n");

        long xref = pos;
        var sb = new StringBuilder();
        sb.Append($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append($"{o:0000000000} 00000 n \n");
        sb.Append($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Ascii(sb.ToString());
    }
}
