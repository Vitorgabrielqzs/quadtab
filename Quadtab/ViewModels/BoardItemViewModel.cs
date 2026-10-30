using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Quadtab.Models;
using Quadtab.Rendering;
using Quadtab.Services;

namespace Quadtab.ViewModels;

public enum EditField { None, Title, Text }

/// <summary>Um item do quadro na tela: dados salvos + estado da interface (seleção, edição, busca).</summary>
public partial class BoardItemViewModel : ObservableObject
{
    static readonly string[] Months = ["JAN", "FEV", "MAR", "ABR", "MAI", "JUN", "JUL", "AGO", "SET", "OUT", "NOV", "DEZ"];
    static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public BoardItemViewModel(BoardItem m)
    {
        Id = m.Id;
        Kind = BoardItem.KindFromJson(m.Tipo);
        Created = DateTime.TryParse(m.Criado, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.Today;
        Load(m);
    }

    /// <summary>Copia os dados salvos para este item (usado também por desfazer/refazer).</summary>
    public void Load(BoardItem m)
    {
        Rotation = m.Rotacao;
        ColorKey = Palette.Get(m.Cor).Key;
        X = m.X;
        Y = m.Y;
        Z = m.Z;
        W = m.Largura;
        H = m.Altura;
        Fastening = Kind is ItemKind.PostIt or ItemKind.Page or ItemKind.Block ? BoardItem.FasteningFromJson(m.Fixacao) : Fastening.None;
        Lined = Kind == ItemKind.PostIt && m.Pautado;
        Locked = m.Bloqueado;
        Group = m.Grupo;
        Description = m.Descricao ?? "";
        Title = m.Titulo ?? "";
        Text = m.Texto ?? "";
        Style = m.Estilo?.Clone() ?? DefaultStyle(Kind);
        ShapeKey = m.Forma ?? "retangulo-arredondado";
        Line = m.Linha;
        Strokes = m.Tracos;
        FilePath = m.Arquivo;
        Url = m.Url;
        Site = m.Site;
        PreviewImage = m.Imagem;
        Crop = m.Recorte is { Length: 4 } c ? c : [0, 0, 0, 0];
        Radius = m.Raio;
        Table = m.Tabela ?? (Kind == ItemKind.Table ? [["", "", ""], ["", "", ""], ["", "", ""]] : null);
    }

    public int Id { get; }
    public ItemKind Kind { get; }
    public DateTime Created { get; }

    // ---------- dados salvos ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Color), nameof(PaperBrush), nameof(InkBrush), nameof(PaperColor), nameof(InkColor), nameof(ListDotBrush), nameof(BlockFill))]
    public partial string ColorKey { get; set; }

    [ObservableProperty] public partial double X { get; set; }
    [ObservableProperty] public partial double Y { get; set; }
    [ObservableProperty] public partial int Z { get; set; }
    [ObservableProperty] public partial double Rotation { get; set; }

    /// <summary>Tamanho escolhido pelas alças (null = padrão do tipo).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ItemWidth), nameof(ItemMinHeight), nameof(ItemHeight), nameof(DrawScaleX))]
    public partial double? W { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ItemMinHeight), nameof(ItemHeight), nameof(DrawScaleY))]
    public partial double? H { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPin), nameof(HasTape))]
    public partial Fastening Fastening { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlainNote), nameof(IsLinedNote), nameof(DisplayName))]
    public partial bool Lined { get; set; }

    [ObservableProperty] public partial bool Locked { get; set; }
    [ObservableProperty] public partial int? Group { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tooltip))]
    public partial string Description { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    public partial string Title { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName), nameof(UpperText), nameof(PreviewText), nameof(Headings), nameof(HasHeadings))]
    public partial string Text { get; set; }

    /// <summary>Página aberta na lista "No quadro" (mostra os títulos internos).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutlineArrow))]
    public partial bool OutlineExpanded { get; set; }

    public double OutlineArrow => OutlineExpanded ? 90 : 0;
    public List<string> Headings => Kind == ItemKind.Page
        ? Markdown.Headings(Text).Select(h => new string(' ', (h.Level - 1) * 3) + h.Text).ToList() : [];
    public bool HasHeadings => Kind == ItemKind.Page && Headings.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FillBrush), nameof(StrokeBrush), nameof(StrokeWidth), nameof(ItemOpacity), nameof(TextBrush),
        nameof(FontFamilyValue), nameof(FontSizeValue), nameof(FontWeightValue), nameof(FontStyleValue), nameof(TextAlign), nameof(StyledText))]
    public partial ItemStyle Style { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShapeGeometry))]
    public partial string ShapeKey { get; set; }

    [ObservableProperty] public partial LineData? Line { get; set; }
    [ObservableProperty] public partial List<InkStroke>? Strokes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName), nameof(FileExtension), nameof(FileFullPath), nameof(DisplayName))]
    public partial string? FilePath { get; set; }

    [ObservableProperty] public partial string? Url { get; set; }
    [ObservableProperty] public partial string? Site { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewImageFull), nameof(HasPreviewImage))]
    public partial string? PreviewImage { get; set; }

    [ObservableProperty] public partial double[] Crop { get; set; }
    [ObservableProperty] public partial double Radius { get; set; }
    [ObservableProperty] public partial List<List<string>>? Table { get; set; }

    // ---------- estado da interface (não é salvo) ----------

    [ObservableProperty] public partial bool IsSelected { get; set; }
    [ObservableProperty] public partial bool IsDimmed { get; set; }

    /// <summary>Resultado destacado da busca no quadro (Ctrl+F).</summary>
    [ObservableProperty] public partial bool IsSearchHit { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingTitle), nameof(IsEditingText))]
    public partial EditField Editing { get; set; }

    /// <summary>Tamanho medido na tela (em unidades do mundo). Usado pelo índice espacial e pelas alças.</summary>
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>Caixa no mundo, com folga para rotação, sombra e alfinete.</summary>
    public Rect Bounds
    {
        get
        {
            var s = MeasuredSize;
            return new Rect(X - 24, Y - 24, s.Width + 48, s.Height + 48);
        }
    }

    /// <summary>Caixa exata no mundo (sem folga).</summary>
    public Rect Box => new(new Point(X, Y), MeasuredSize);

    public Size MeasuredSize => new(Width > 0 ? Width : ItemWidth, Height > 0 ? Height : (H ?? DefaultSize(Kind).Height));

    // ---------- valores derivados, usados pelas telas ----------

    public NoteColor Color => Palette.Get(ColorKey);
    public Color PaperColor => Color.Paper;
    public Color InkColor => Color.Ink;
    public Brush PaperBrush => Color.PaperBrush;
    public Brush InkBrush => Color.InkBrush;
    public Brush ListDotBrush => Kind switch
    {
        ItemKind.Text => Color.InkBrush,
        ItemKind.Shape or ItemKind.TextBox or ItemKind.Line or ItemKind.Drawing => FillBrush is SolidColorBrush { Color.A: > 0 } f ? f : StrokeBrush,
        _ => Color.PaperBrush,
    };
    public Brush BlockFill => Color.BlockFill;
    public Color PinColor => Palette.PinFor(Id);

    public double ItemWidth => W ?? DefaultSize(Kind).Width;
    public double ItemMinHeight => H ?? DefaultSize(Kind).Height;
    public double ItemHeight => H ?? DefaultSize(Kind).Height;

    public bool HasPin => Fastening == Fastening.Pin;
    public bool HasTape => Fastening == Fastening.Tape;
    public bool IsPlainNote => Kind == ItemKind.PostIt && !Lined;
    public bool IsLinedNote => Kind == ItemKind.PostIt && Lined;
    public bool IsEditingTitle => Editing == EditField.Title;
    public bool IsEditingText => Editing == EditField.Text;
    public bool HasTitle => Kind is ItemKind.Page or ItemKind.Block || Lined;

    public string UpperText => Text.ToUpper(PtBr);
    public string Number => "#" + (Id % 100).ToString("00");
    public string DateLabel => $"{Created.Day} {Months[Created.Month - 1]}";
    public object? Tooltip => string.IsNullOrWhiteSpace(Description) ? null : Description;

    /// <summary>Começo do texto da página (o que aparece na folha de caderno).</summary>
    public string PreviewText => Markdown.ToPlainPreview(Text, 14);

    // estilo livre
    public Brush FillBrush => BrushFromHex(Style.Preenchimento, Colors.Transparent);
    public Brush StrokeBrush => BrushFromHex(Style.Borda, Colors.Transparent);
    public double StrokeWidth => Style.Espessura;
    public double ItemOpacity => Style.Opacidade;
    public Brush TextBrush => BrushFromHex(Style.CorTexto, System.Windows.Media.Color.FromRgb(0x1D, 0x21, 0x30));
    public FontFamily FontFamilyValue => (FontFamily)Application.Current.FindResource(Style.Fonte == "Nunito" ? "Nunito" : "Inter");
    public double FontSizeValue => Style.Tamanho;
    public FontWeight FontWeightValue => Style.Negrito ? FontWeights.Bold : FontWeights.Normal;
    public FontStyle FontStyleValue => Style.Italico ? FontStyles.Italic : FontStyles.Normal;
    public TextAlignment TextAlign => Style.Alinhamento switch { "centro" => TextAlignment.Center, "direita" => TextAlignment.Right, _ => TextAlignment.Left };

    /// <summary>Texto da caixa de texto com marcadores de lista, quando ligados.</summary>
    public string StyledText => Style.Lista ? string.Join("\n", Text.Split('\n').Select(l => "•  " + l)) : Text;

    public Geometry ShapeGeometry => ShapeLibrary.Get(ShapeKey).Geometry;

    // desenho: escala entre o tamanho original dos traços e o tamanho atual
    public double DrawScaleX => Kind == ItemKind.Drawing && W is { } w && InkBase.Width > 0 ? w / InkBase.Width : 1;
    public double DrawScaleY => Kind == ItemKind.Drawing && H is { } h && InkBase.Height > 0 ? h / InkBase.Height : 1;
    public Size InkBase { get; set; }

    // arquivos
    public string? FileFullPath => FilePath is null ? null : StorageService.Full(FilePath);
    public string FileName => FilePath is null ? "" : Path.GetFileName(FilePath);
    public string FileExtension => FilePath is null ? "" : Path.GetExtension(FilePath).TrimStart('.').ToUpperInvariant();
    public string? PreviewImageFull => PreviewImage is null ? null : StorageService.Full(PreviewImage);
    public bool HasPreviewImage => PreviewImage is not null && File.Exists(StorageService.Full(PreviewImage));

    public string TypeLabel => Kind switch
    {
        ItemKind.Page => "Página",
        ItemKind.Block => "Bloco",
        ItemKind.Text => "Texto",
        ItemKind.TextBox => "Caixa de texto",
        ItemKind.Shape => "Forma",
        ItemKind.Line => "Linha",
        ItemKind.Drawing => "Desenho",
        ItemKind.Image => "Imagem",
        ItemKind.Video => "Vídeo",
        ItemKind.File => "Arquivo",
        ItemKind.Link => "Link",
        ItemKind.Table => "Tabela",
        _ => "Post-it",
    };

    /// <summary>Nome na lista "No quadro".</summary>
    public string DisplayName
    {
        get
        {
            var s = Kind switch
            {
                ItemKind.Page or ItemKind.Block => Title,
                ItemKind.PostIt when Lined => Title,
                ItemKind.Image or ItemKind.Video or ItemKind.File => FileName,
                ItemKind.Link => string.IsNullOrWhiteSpace(Title) ? Url ?? "" : Title,
                ItemKind.Shape => string.IsNullOrWhiteSpace(Text) ? ShapeLibrary.Get(ShapeKey).Name : Text,
                ItemKind.Line => "Linha",
                ItemKind.Drawing => "Desenho",
                ItemKind.Table => Table is { Count: > 0 } t ? string.Join(" · ", t[0].Where(c => c.Length > 0)) : "Tabela",
                _ => Text,
            };
            return string.IsNullOrWhiteSpace(s) ? TypeLabel : s.ReplaceLineEndings(" ").Trim();
        }
    }

    /// <summary>Tamanho padrão de cada tipo de item.</summary>
    public static Size DefaultSize(ItemKind kind) => kind switch
    {
        ItemKind.Page => new Size(236, 300),
        ItemKind.Block => new Size(290, 170),
        ItemKind.Text => new Size(240, 60),
        ItemKind.TextBox => new Size(260, 80),
        ItemKind.Shape => new Size(160, 160),
        ItemKind.Line => new Size(200, 4),
        ItemKind.Drawing => new Size(100, 100),
        ItemKind.Image => new Size(320, 240),
        ItemKind.Video => new Size(400, 225),
        ItemKind.File => new Size(240, 300),
        ItemKind.Link => new Size(320, 120),
        ItemKind.Table => new Size(360, 120),
        _ => new Size(172, 172),
    };

    public static ItemStyle DefaultStyle(ItemKind kind) => kind switch
    {
        ItemKind.Shape => new ItemStyle { Preenchimento = "#FFA6D3FF", Borda = "#FF1B5DA8", Espessura = 2, Tamanho = 16, Alinhamento = "centro", CorTexto = "#FF1D2130" },
        ItemKind.Line => new ItemStyle { Borda = "#FF1D2130", Espessura = 2.5 },
        ItemKind.TextBox => new ItemStyle { Tamanho = 20, CorTexto = "#FF1D2130" },
        ItemKind.Table => new ItemStyle { Preenchimento = "#F2FFFFFF", Borda = "#331D2130", Espessura = 1, Tamanho = 14, CorTexto = "#FF1D2130" },
        ItemKind.Text => new ItemStyle { Tamanho = 50, Fonte = "Nunito" },
        _ => new ItemStyle(),
    };

    static Brush BrushFromHex(string? hex, Color fallback)
    {
        if (string.IsNullOrEmpty(hex)) return ShadowCache.BrushOf(fallback);
        try { return ShadowCache.BrushOf((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return ShadowCache.BrushOf(fallback); }
    }

    public bool Matches(string normalizedQuery) =>
        normalizedQuery.Length == 0 ||
        Normalize(Title + " " + Text + " " + Description + " " + (Url ?? "") + " " + FileName + " " +
                  (Table is null ? "" : string.Join(" ", Table.SelectMany(r => r)))).Contains(normalizedQuery, StringComparison.Ordinal);

    /// <summary>Minúsculas e sem acento: "Gratidão" encontra "gratidao".</summary>
    public static string Normalize(string s)
    {
        var d = s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(d.Length);
        foreach (var ch in d)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString();
    }

    /// <summary>Avisa a tela que a linha ou o desenho mudou por dentro (mesmo objeto).</summary>
    public void Touch(string property) => OnPropertyChanged(property);

    public BoardItem ToModel() => new()
    {
        Id = Id,
        Tipo = BoardItem.KindToJson(Kind),
        Cor = ColorKey,
        X = Math.Round(X, 1),
        Y = Math.Round(Y, 1),
        Z = Z,
        Rotacao = Math.Round(Rotation, 2),
        Largura = W is { } w ? Math.Round(w, 1) : null,
        Altura = H is { } h ? Math.Round(h, 1) : null,
        Fixacao = BoardItem.FasteningToJson(Fastening),
        Pautado = Lined,
        Bloqueado = Locked,
        Grupo = Group,
        Descricao = string.IsNullOrWhiteSpace(Description) ? null : Description,
        Titulo = Title,
        Texto = Text,
        Criado = Created.ToString("yyyy-MM-dd"),
        Estilo = Kind is ItemKind.PostIt or ItemKind.Page or ItemKind.Block ? null : Style.Clone(),
        Forma = Kind == ItemKind.Shape ? ShapeKey : null,
        Linha = Line,
        Tracos = Strokes,
        Arquivo = FilePath,
        Url = Url,
        Site = Site,
        Imagem = PreviewImage,
        Recorte = Crop.Any(c => c != 0) ? Crop : null,
        Raio = Radius,
        Tabela = Table,
    };
}
