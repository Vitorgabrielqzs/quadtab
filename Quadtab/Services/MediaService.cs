using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;
using Quadtab.Models;
using Quadtab.ViewModels;

namespace Quadtab.Services;

/// <summary>
/// Imagens, vídeos, arquivos e links no quadro: soltar arquivos do Windows, colar (Ctrl+V),
/// prévia da 1ª página dos PDFs e cartão de prévia dos links. Tudo é copiado para Anexos\ no cofre.
/// </summary>
public static partial class MediaService
{
    static readonly string[] ImageExt = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".heic"];
    static readonly string[] VideoExt = [".mp4", ".mov", ".wmv", ".avi", ".mkv", ".webm", ".m4v"];
    static readonly HttpClient Http = CreateHttp();

    static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Quadtab/1.0");
        return h;
    }

    public static bool IsImage(string path) => ImageExt.Contains(Path.GetExtension(path).ToLowerInvariant());
    public static bool IsVideo(string path) => VideoExt.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Arquivos soltos no quadro (ou colados): cada um vira imagem, vídeo, página ou cartão de arquivo.</summary>
    public static void AddFiles(MainViewModel vm, IEnumerable<string> files, Point at)
    {
        var offset = new Vector();
        var added = new List<BoardItemViewModel>();
        foreach (var f in files.Where(File.Exists))
        {
            var p = at + offset;
            var it = AddFile(vm, f, p);
            if (it is not null) added.Add(it);
            offset += new Vector(36, 36);
        }
        if (added.Count > 1) vm.SelectMany(added, add: false);
    }

    static BoardItemViewModel? AddFile(MainViewModel vm, string file, Point at)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();
        if (ext == ".md")
        {
            var page = StorageService.CreatePage(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file));
            var size = BoardItemViewModel.DefaultSize(ItemKind.Page);
            return vm.AddItem(new BoardItem
            {
                Tipo = "pagina", Cor = "branco", X = Math.Round(at.X - size.Width / 2), Y = Math.Round(at.Y - size.Height / 2),
                Fixacao = "fita", Arquivo = StorageService.Rel(page), Titulo = Path.GetFileNameWithoutExtension(page),
            });
        }
        var rel = StorageService.ImportAttachment(file);
        if (IsImage(file)) return AddImageItem(vm, rel, at);
        if (IsVideo(file))
            return vm.AddItem(new BoardItem { Tipo = "video", X = Math.Round(at.X - 200), Y = Math.Round(at.Y - 112), Largura = 400, Altura = 225, Arquivo = rel, Fixacao = "nenhuma" });

        var item = vm.AddItem(new BoardItem { Tipo = "arquivo", X = Math.Round(at.X - 110), Y = Math.Round(at.Y - 140), Arquivo = rel, Fixacao = "nenhuma" });
        if (ext == ".pdf") _ = MakePdfPreviewAsync(item);
        return item;
    }

    static BoardItemViewModel AddImageItem(MainViewModel vm, string rel, Point at)
    {
        // Tamanho real da imagem, limitado a 420 no lado maior.
        double w = 320, h = 240;
        try
        {
            var dec = BitmapDecoder.Create(new Uri(StorageService.Full(rel)), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var f = dec.Frames[0];
            double k = Math.Min(1, 420 / Math.Max(f.PixelWidth, f.PixelHeight));
            w = Math.Round(f.PixelWidth * k);
            h = Math.Round(f.PixelHeight * k);
        }
        catch { }
        return vm.AddItem(new BoardItem
        {
            Tipo = "imagem", X = Math.Round(at.X - w / 2), Y = Math.Round(at.Y - h / 2), Largura = w, Altura = h,
            Arquivo = rel, Fixacao = "nenhuma", Raio = 6,
        });
    }

    /// <summary>Imagem vinda da área de transferência (print, cópia de um navegador...).</summary>
    public static BoardItemViewModel AddBitmap(MainViewModel vm, BitmapSource image, Point at)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(image));
        using var ms = new MemoryStream();
        enc.Save(ms);
        var rel = StorageService.SaveAttachment(ms.ToArray(), $"imagem {DateTime.Now:yyyy-MM-dd HHmmss}.png");
        return AddImageItem(vm, rel, at);
    }

    /// <summary>Cartão de link: aparece na hora e a prévia (título e imagem) chega depois.</summary>
    public static BoardItemViewModel AddLink(MainViewModel vm, string url, Point at)
    {
        var uri = new Uri(url);
        var item = vm.AddItem(new BoardItem
        {
            Tipo = "link", X = Math.Round(at.X - 150), Y = Math.Round(at.Y - 60), Url = url,
            Site = uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host, Fixacao = "nenhuma",
        });
        _ = FetchPreviewAsync(item, uri);
        return item;
    }

    public static bool IsUrl(string text, out string url)
    {
        url = text.Trim();
        if (url.Contains(' ') || url.Contains('\n')) return false;
        if (url.StartsWith("www.")) url = "https://" + url;
        return Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https");
    }

    /// <summary>Ctrl+V: itens do Quadtab, imagem, arquivos, link ou texto (vira caixa de texto).</summary>
    public static void Paste(MainViewModel vm, Point at)
    {
        try
        {
            if (Clipboard.ContainsData(MainViewModel.ClipFormat))
            {
                vm.PasteItems(at);
                return;
            }
            if (Clipboard.ContainsFileDropList())
            {
                AddFiles(vm, Clipboard.GetFileDropList().Cast<string>().ToList(), at);
                return;
            }
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } img)
            {
                AddBitmap(vm, img, at);
                return;
            }
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (IsUrl(text, out var url)) AddLink(vm, url, at);
                else if (text.Trim().Length > 0)
                {
                    var size = BoardItemViewModel.DefaultSize(ItemKind.TextBox);
                    vm.AddItem(new BoardItem
                    {
                        Tipo = "caixa", X = Math.Round(at.X - size.Width / 2), Y = Math.Round(at.Y - size.Height / 2),
                        Texto = text.Trim(), Fixacao = "nenhuma", Estilo = BoardItemViewModel.DefaultStyle(ItemKind.TextBox),
                    });
                }
            }
        }
        catch { } // área de transferência ocupada por outro programa
    }

    // ---------- prévia de PDF (Windows.Data.Pdf) ----------

    static async Task MakePdfPreviewAsync(BoardItemViewModel item)
    {
        if (item.FileFullPath is not { } path) return;
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            var doc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
            if (doc.PageCount == 0) return;
            using var page = doc.GetPage(0);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = 600 });
            using var net = stream.AsStreamForRead();
            using var ms = new MemoryStream();
            await net.CopyToAsync(ms);
            var saved = StorageService.SaveAttachment(ms.ToArray(), Path.GetFileNameWithoutExtension(path) + " (prévia).png");
            item.PreviewImage = saved;
        }
        catch { } // PDF protegido ou corrompido: fica o ícone
    }

    // ---------- prévia de link (og:title / og:image) ----------

    [GeneratedRegex("<meta[^>]+(?:property|name)=[\"'](og:title|og:image|twitter:image|og:site_name)[\"'][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTag();

    [GeneratedRegex("content=[\"']([^\"']*)[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex ContentAttr();

    [GeneratedRegex("<title[^>]*>([^<]*)</title>", RegexOptions.IgnoreCase)]
    private static partial Regex TitleTag();

    static async Task FetchPreviewAsync(BoardItemViewModel item, Uri uri)
    {
        try
        {
            using var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode || resp.Content.Headers.ContentType?.MediaType is not "text/html") return;
            var html = await resp.Content.ReadAsStringAsync();
            if (html.Length > 600_000) html = html[..600_000];
            string? title = null, image = null, site = null;
            foreach (Match m in MetaTag().Matches(html))
            {
                var content = ContentAttr().Match(m.Value) is { Success: true } c ? System.Net.WebUtility.HtmlDecode(c.Groups[1].Value) : null;
                switch (m.Groups[1].Value.ToLowerInvariant())
                {
                    case "og:title": title ??= content; break;
                    case "og:image" or "twitter:image": image ??= content; break;
                    case "og:site_name": site ??= content; break;
                }
            }
            title ??= TitleTag().Match(html) is { Success: true } t ? System.Net.WebUtility.HtmlDecode(t.Groups[1].Value.Trim()) : null;
            if (!string.IsNullOrWhiteSpace(title)) item.Title = title.Trim();
            if (!string.IsNullOrWhiteSpace(site)) item.Site = site.Trim();
            if (image is not null && Uri.TryCreate(uri, image, out var imgUri) && imgUri.Scheme is "http" or "https")
            {
                using var ir = await Http.GetAsync(imgUri);
                var type = ir.Content.Headers.ContentType?.MediaType ?? "";
                if (!ir.IsSuccessStatusCode || !type.StartsWith("image/")) return;
                var bytes = await ir.Content.ReadAsByteArrayAsync();
                if (bytes.Length > 8_000_000) return;
                var ext = type switch { "image/png" => ".png", "image/gif" => ".gif", "image/webp" => ".webp", _ => ".jpg" };
                var saved = StorageService.SaveAttachment(bytes, $"link {uri.Host}{ext}");
                item.PreviewImage = saved;
            }
        }
        catch { } // sem internet ou site bloqueou: o cartão fica só com o endereço
    }
}
