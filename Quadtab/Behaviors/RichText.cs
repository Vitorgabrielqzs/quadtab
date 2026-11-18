using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Quadtab.Behaviors;

// Aqui fica a formatação do texto dos itens (Markdown/HTML simples): leitura das marcas e desenho num TextBlock.

/// <summary>Formatação de um trecho: negrito, itálico, sublinhado, riscado, código, cor do texto e cor do fundo.</summary>
public readonly record struct Fmt(bool Bold, bool Italic, bool Underline, bool Strike, bool Code, string? Color, string? Back)
{
    public bool IsPlain => this == default;
}

/// <summary>Um trecho do texto limpo com a mesma formatação.</summary>
public sealed record Seg(int Start, int Length, Fmt Fmt);

/// <summary>Texto sem as marcas e os trechos formatados dele (posições no texto sem marcas).</summary>
public sealed record Parsed(string Text, IReadOnlyList<Seg> Segs)
{
    public bool HasFormat => Segs.Any(s => !s.Fmt.IsPlain);
}

/// <summary>
/// Aqui usamos a classe RichText para ler a formatação dentro do texto dos itens, em Markdown/HTML simples (o mesmo que o Obsidian lê):
/// **negrito**, *itálico*, ~~riscado~~, `código`, &lt;u&gt;sublinhado&lt;/u&gt;,
/// &lt;span style="color:#RRGGBB"&gt;cor&lt;/span&gt; e &lt;mark style="background:#RRGGBB"&gt;fundo&lt;/mark&gt;.
/// Marca sem par na linha fica como texto comum. Na edição o texto aparece com as marcas; fora dela, formatado.
/// </summary>
public static partial class RichText
{
    [GeneratedRegex(@"\*\*|\*|~~|`|<u>|</u>|<span style=""color:(#[0-9A-Fa-f]{6})"">|</span>|<mark style=""background:(#[0-9A-Fa-f]{6})"">|</mark>")]
    private static partial Regex Token();

    // ---------- leitura das marcas ----------

    /// <summary>Separa o texto limpo dos trechos formatados.</summary>
    public static Parsed Parse(string md)
    {
        var sb = new StringBuilder(md.Length);
        var segs = new List<Seg>();
        bool bold = false, italic = false, strike = false, code = false;
        int under = 0;
        var colors = new Stack<string>();
        var backs = new Stack<string>();

        void Add(string s)
        {
            if (s.Length == 0) return;
            var f = new Fmt(bold, italic, under > 0, strike, code, colors.Count > 0 ? colors.Peek() : null, backs.Count > 0 ? backs.Peek() : null);
            int start = sb.Length;
            sb.Append(s);
            if (segs.Count > 0 && segs[^1].Fmt == f) segs[^1] = segs[^1] with { Length = segs[^1].Length + s.Length };
            else segs.Add(new Seg(start, s.Length, f));
        }

        int pos = 0;
        foreach (Match m in Token().Matches(md))
        {
            Add(md[pos..m.Index]);
            pos = m.Index + m.Length;
            string t = m.Value;
            bool used = true;
            if (code && t != "`") used = false;
            else if (t == "**") used = Toggle(ref bold, md, pos, "**");
            else if (t == "~~") used = Toggle(ref strike, md, pos, "~~");
            else if (t == "`") used = Toggle(ref code, md, pos, "`");
            else if (t == "*")
            {
                if (italic) italic = false;
                else if (pos < md.Length && !char.IsWhiteSpace(md[pos]) && HasCloser(md, pos, "*")) italic = true;
                else used = false;
            }
            else if (t == "<u>") under++;
            else if (t == "</u>") { if (under > 0) under--; else used = false; }
            else if (m.Groups[1].Success) colors.Push(m.Groups[1].Value);
            else if (m.Groups[2].Success) backs.Push(m.Groups[2].Value);
            else if (t == "</span>") { if (colors.Count > 0) colors.Pop(); else used = false; }
            else if (t == "</mark>") { if (backs.Count > 0) backs.Pop(); else used = false; }
            if (!used) Add(t);
        }
        Add(md[pos..]);
        return new Parsed(sb.ToString(), segs);
    }

    /// <summary>Fecha a marca se está aberta; abre só se houver outra igual adiante na mesma linha.</summary>
    static bool Toggle(ref bool on, string md, int pos, string mark)
    {
        if (on) { on = false; return true; }
        return on = HasCloser(md, pos, mark);
    }

    // Existe outra marca igual adiante, na mesma linha?
    static bool HasCloser(string md, int pos, string mark)
    {
        int end = md.IndexOf('\n', pos) is var eol and >= 0 ? eol : md.Length;
        return md.IndexOf(mark, pos, end - pos, StringComparison.Ordinal) > pos;
    }

    // ---------- TextBlock ----------

    /// <summary>Texto (Markdown) de um TextBlock, desenhado com a formatação.</summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(string), typeof(RichText), new PropertyMetadata(null, OnSource));

    public static string? GetSource(DependencyObject d) => (string?)d.GetValue(SourceProperty);
    public static void SetSource(DependencyObject d, string? v) => d.SetValue(SourceProperty, v);

    static void OnSource(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        Fill(tb, e.NewValue as string ?? "");
        // O peso da fonte pode chegar por binding depois do texto: refaz quando o elemento carregar.
        tb.Loaded -= Reload;
        tb.Loaded += Reload;
    }

    static void Reload(object sender, RoutedEventArgs e)
    {
        var tb = (TextBlock)sender;
        Fill(tb, GetSource(tb) ?? "");
    }

    /// <summary>Preenche o TextBlock com os trechos formatados (bolinhas de lista já trocadas).</summary>
    public static void Fill(TextBlock tb, string md)
    {
        var p = Parse(ListText.ToDisplay(md));
        tb.Inlines.Clear();
        foreach (var seg in p.Segs)
        {
            string[] lines = p.Text.Substring(seg.Start, seg.Length).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) tb.Inlines.Add(new LineBreak());
                if (lines[i].Length > 0) tb.Inlines.Add(MakeRun(tb, lines[i], seg.Fmt));
            }
        }
    }

    // Um pedaço de texto com a formatação aplicada.
    static Run MakeRun(TextBlock tb, string text, Fmt f)
    {
        var r = new Run(text);
        Style(r, f, tb.FontWeight);
        return r;
    }

    /// <summary>Aplica a formatação num trecho (TextBlock e campo de edição usam o mesmo).</summary>
    public static void Style(Inline r, Fmt f, FontWeight baseWeight)
    {
        if (f.Bold) r.FontWeight = BoldOf(baseWeight);
        if (f.Italic) r.FontStyle = FontStyles.Italic;
        if (Decorations(f) is { } d) r.TextDecorations = d;
        if (f.Code) { r.FontFamily = CodeFont; r.Background = CodeBack; }
        if (BrushOf(f.Color) is { } ink) r.Foreground = ink;
        if (BrushOf(f.Back) is { } back) r.Background = back;
    }

    /// <summary>Sublinhado e/ou riscado do trecho (null quando não tem nenhum).</summary>
    public static TextDecorationCollection? Decorations(Fmt f)
    {
        if (!f.Underline && !f.Strike) return null;
        var d = new TextDecorationCollection();
        if (f.Underline) d.Add(TextDecorations.Underline[0]);
        if (f.Strike) d.Add(TextDecorations.Strikethrough[0]);
        return d;
    }

    // Letra e fundo do `código`.
    public static readonly FontFamily CodeFont = new("Consolas");
    public static readonly Brush CodeBack = Rendering.ShadowCache.BrushOf(Color.FromArgb(34, 120, 120, 130));

    /// <summary>
    /// Negrito em cima do peso atual. Sempre maior que o peso de base (o editor reconhece o negrito por isso),
    /// mesmo quando o texto já é preto: a fonte desenha igual, mas a marca não se perde.
    /// </summary>
    public static FontWeight BoldOf(FontWeight baseWeight)
    {
        int w = baseWeight.ToOpenTypeWeight();
        return w >= 900 ? FontWeight.FromOpenTypeWeight(950) : w >= 600 ? FontWeights.Black : FontWeights.Bold;
    }

    /// <summary>"#RRGGBB" → pincel (null se vazio ou inválido).</summary>
    public static Brush? BrushOf(string? hex)
    {
        if (hex is null) return null;
        try { return Rendering.ShadowCache.BrushOf((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return null; }
    }
}
