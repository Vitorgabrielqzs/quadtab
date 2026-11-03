using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Quadtab.Behaviors;

namespace Quadtab.Controls;

/// <summary>
/// Campo de edição do texto dos itens, já formatado (negrito, itálico, sublinhado, riscado, código, cor e fundo),
/// sem mostrar nenhuma marca de código. O texto do item continua sendo Markdown: este campo lê o Markdown
/// (<see cref="Markdown"/>), mostra formatado e devolve Markdown a cada mudança. Os índices (SelectionStart,
/// CaretIndex…) contam letras do texto limpo, com "\n" nas quebras de linha, como num TextBox.
/// </summary>
public sealed class RichEditBox : RichTextBox
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown), typeof(string), typeof(RichEditBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((RichEditBox)d).OnMarkdown((string?)e.NewValue ?? "")));

    public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(
        nameof(TextAlignment), typeof(TextAlignment), typeof(RichEditBox),
        new PropertyMetadata(TextAlignment.Left, (d, e) => ((RichEditBox)d)._p.TextAlignment = (TextAlignment)e.NewValue));

    /// <summary>false = uma linha só, que cresce para o lado (texto solto).</summary>
    public static readonly DependencyProperty WrapProperty = DependencyProperty.Register(
        nameof(Wrap), typeof(bool), typeof(RichEditBox), new PropertyMetadata(true, (d, _) => ((RichEditBox)d).Fit()));

    public string Markdown { get => (string)GetValue(MarkdownProperty); set => SetValue(MarkdownProperty, value); }
    public TextAlignment TextAlignment { get => (TextAlignment)GetValue(TextAlignmentProperty); set => SetValue(TextAlignmentProperty, value); }
    public bool Wrap { get => (bool)GetValue(WrapProperty); set => SetValue(WrapProperty, value); }

    Paragraph _p = new() { Margin = new Thickness(0) }; // o parágrafo único do documento (ver Adopt)
    bool _busy;      // carregando ou corrigindo: não devolve ao Markdown nem reage
    string _last = ""; // o Markdown que este campo mesmo produziu

    public RichEditBox()
    {
        // O padrão do documento é justificado (palavras esticadas, com buracos enormes): começa à esquerda.
        _p.TextAlignment = TextAlignment.Left;
        Document = new FlowDocument(_p) { PagePadding = new Thickness(0), TextAlignment = TextAlignment.Left };
        Style = (Style)Application.Current.FindResource("RichEdit");
        AcceptsReturn = true;
        SpellCheck.IsEnabled = false;
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;

        // O documento não herda a fonte do campo sozinho: liga um a um.
        void Link(DependencyProperty docProp, DependencyProperty boxProp) =>
            Document.SetBinding(docProp, new Binding { Path = new PropertyPath(boxProp), Source = this });
        Link(TextElement.FontFamilyProperty, FontFamilyProperty);
        Link(TextElement.FontSizeProperty, FontSizeProperty);
        Link(TextElement.FontWeightProperty, FontWeightProperty);
        Link(TextElement.FontStyleProperty, FontStyleProperty);
        Link(TextElement.ForegroundProperty, ForegroundProperty);

        TextChanged += OnTextChanged;
        PreviewKeyDown += OnKeys;
        DataObject.AddPastingHandler(this, OnPaste);
        RichEdit.Attach(this);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // O peso da fonte (e o tamanho) chegam por binding depois do texto: o negrito depende deles.
        if (!_busy && IsLoaded && e.Property == FontWeightProperty) Load(Markdown);
        else if (e.Property == FontSizeProperty || e.Property == FontFamilyProperty) Fit();
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Loaded += (_, _) => Load(Markdown);
    }

    // ---------- Markdown <-> documento ----------

    void OnMarkdown(string md)
    {
        if (_busy || md == _last) return;
        Load(md);
    }

    /// <summary>
    /// Trocar o texto todo selecionado (digitar, apagar, colar) faz o RichTextBox jogar fora o parágrafo e criar
    /// outro (ou nenhum): o campo passa a usar o que estiver no documento, senão leria o antigo, vazio, e o texto sumia.
    /// </summary>
    void Adopt()
    {
        if (Document.Blocks.FirstBlock == _p) return;
        if (Document.Blocks.FirstBlock is Paragraph first)
        {
            first.Margin = new Thickness(0);
            first.TextAlignment = TextAlignment;
            _p = first;
        }
        else
        {
            _p.Inlines.Clear();
            if (Document.Blocks.FirstBlock is { } block) Document.Blocks.InsertBefore(block, _p);
            else Document.Blocks.Add(_p);
        }
    }

    void Load(string md)
    {
        _busy = true;
        try
        {
            Adopt();
            _p.Inlines.Clear();
            var parsed = RichText.Parse(ListText.ToDisplay(md));
            foreach (var seg in parsed.Segs)
            {
                var lines = parsed.Text.Substring(seg.Start, seg.Length).Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (i > 0) _p.Inlines.Add(new LineBreak());
                    if (lines[i].Length == 0) continue;
                    var run = new Run(lines[i]);
                    Apply(run, seg.Fmt);
                    _p.Inlines.Add(run);
                }
            }
            _last = md;
        }
        finally { _busy = false; }
        Fit();
    }

    void Apply(Run r, Fmt f)
    {
        if (f.Bold) r.FontWeight = RichText.BoldOf(FontWeight);
        if (f.Italic) r.FontStyle = FontStyles.Italic;
        if (f.Underline || f.Strike)
        {
            var d = new TextDecorationCollection();
            if (f.Underline) d.Add(TextDecorations.Underline[0]);
            if (f.Strike) d.Add(TextDecorations.Strikethrough[0]);
            r.TextDecorations = d;
        }
        if (f.Code)
        {
            r.FontFamily = RichText.CodeFont;
            r.Background = RichText.CodeBack;
        }
        if (RichText.BrushOf(f.Color) is { } ink) r.Foreground = ink;
        if (RichText.BrushOf(f.Back) is { } back) r.Background = back;
    }

    static IEnumerable<Inline> Leaves(InlineCollection inlines)
    {
        foreach (var i in inlines)
        {
            if (i is Span s) foreach (var l in Leaves(s.Inlines)) yield return l;
            else yield return i;
        }
    }

    static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    static bool HasDecoration(TextDecorationCollection? d, TextDecorationLocation loc)
    {
        if (d is null) return false;
        foreach (var x in d) if (x.Location == loc) return true;
        return false;
    }

    Fmt FormatOf(Run r)
    {
        bool bold = r.FontWeight.ToOpenTypeWeight() > FontWeight.ToOpenTypeWeight();
        bool italic = r.FontStyle == FontStyles.Italic && FontStyle != FontStyles.Italic;
        bool code = r.FontFamily.Source == RichText.CodeFont.Source && FontFamily.Source != RichText.CodeFont.Source;
        string? color = r.Foreground is SolidColorBrush rb && !(Foreground is SolidColorBrush bb && bb.Color == rb.Color) ? Hex(rb.Color) : null;
        string? back = r.Background is SolidColorBrush sb && sb.Color.A > 0 && sb.Color != ((SolidColorBrush)RichText.CodeBack).Color ? Hex(sb.Color) : null;
        return new Fmt(bold, italic, HasDecoration(r.TextDecorations, TextDecorationLocation.Underline),
            HasDecoration(r.TextDecorations, TextDecorationLocation.Strikethrough), code, color, back);
    }

    /// <summary>Pedaços do texto em ordem, cada um com a sua formatação; "\n" é uma quebra de linha.</summary>
    List<(string Text, Fmt Fmt)> Tokens()
    {
        var list = new List<(string, Fmt)>();
        Guarded(Adopt);
        foreach (var leaf in Leaves(_p.Inlines))
        {
            if (leaf is Run r) { if (r.Text.Length > 0) list.Add((r.Text, FormatOf(r))); }
            else if (leaf is LineBreak) list.Add(("\n", default));
        }
        return list;
    }

    /// <summary>Texto sem formatação, com as bolinhas "•" (os índices do campo contam sobre ele).</summary>
    public string PlainText => string.Concat(Tokens().Select(t => t.Text));

    string ToMarkdown()
    {
        var sb = new StringBuilder();
        var tokens = Tokens();
        for (int i = 0; i < tokens.Count; i++)
        {
            var (text, f) = tokens[i];
            if (text == "\n") { sb.Append('\n'); continue; }
            while (i + 1 < tokens.Count && tokens[i + 1].Text != "\n" && tokens[i + 1].Fmt == f) text += tokens[++i].Text;
            Emit(sb, text, f);
        }
        return ListText.ToMarkdown(sb.ToString());
    }

    /// <summary>Marcas em volta do trecho; espaços das pontas ficam de fora (o Markdown não aceita marca colada em espaço).</summary>
    static void Emit(StringBuilder sb, string text, Fmt f)
    {
        string core = text.Trim(' ', '\t');
        if (f.IsPlain || core.Length == 0) { sb.Append(text); return; }
        int lead = text.Length - text.TrimStart(' ', '\t').Length;
        sb.Append(text, 0, lead);
        if (f.Color is not null) sb.Append($"<span style=\"color:{f.Color}\">");
        if (f.Back is not null) sb.Append($"<mark style=\"background:{f.Back}\">");
        if (f.Bold) sb.Append("**");
        if (f.Italic) sb.Append('*');
        if (f.Strike) sb.Append("~~");
        if (f.Underline) sb.Append("<u>");
        if (f.Code) sb.Append('`');
        sb.Append(core);
        if (f.Code) sb.Append('`');
        if (f.Underline) sb.Append("</u>");
        if (f.Strike) sb.Append("~~");
        if (f.Italic) sb.Append('*');
        if (f.Bold) sb.Append("**");
        if (f.Back is not null) sb.Append("</mark>");
        if (f.Color is not null) sb.Append("</span>");
        sb.Append(text, lead + core.Length, text.Length - lead - core.Length);
    }

    /// <summary>Guarda o que está no campo de volta no Markdown do item.</summary>
    void Sync()
    {
        string md = ToMarkdown();
        if (md == _last) return;
        _last = md;
        SetCurrentValue(MarkdownProperty, md);
    }

    void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_busy) return;
        Normalize();
        // Acabou de digitar "- " no começo da linha: vira bolinha.
        if (IsKeyboardFocused && Selection.IsEmpty)
        {
            int caret = CaretIndex;
            if (ListText.TypedBullet(PlainText, caret) is { } dash)
            {
                Guarded(() => { ReplaceRange(dash, 1, ListText.Bullet.ToString()); Select(caret, 0); });
            }
        }
        Sync();
        Fit();
    }

    /// <summary>Um único parágrafo, com quebras de linha: junta os que sobrarem (colar, arrastar).</summary>
    void Normalize()
    {
        Guarded(Adopt);
        if (Document.Blocks.Count <= 1) return;
        Guarded(() =>
        {
            foreach (var block in Document.Blocks.ToList())
            {
                if (block == _p) continue;
                if (block is Paragraph other)
                {
                    _p.Inlines.Add(new LineBreak());
                    foreach (var inline in other.Inlines.ToList()) { other.Inlines.Remove(inline); _p.Inlines.Add(inline); }
                }
                Document.Blocks.Remove(block);
            }
        });
    }

    void Guarded(Action a)
    {
        bool was = _busy;
        _busy = true;
        try { a(); }
        finally { _busy = was; }
    }

    // ---------- larguras (texto solto, uma linha) ----------

    void Fit()
    {
        if (Wrap) { if (!double.IsNaN(Document.PageWidth)) Document.PageWidth = double.NaN; return; }
        double widest = 0;
        var tf = new Typeface(FontFamily, FontStyle, FontWeight, FontStretches.Normal);
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var line in PlainText.Split('\n'))
        {
            var ft = new FormattedText(line.Length == 0 ? " " : line, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, FontSize, Brushes.Black, ppd);
            widest = Math.Max(widest, ft.WidthIncludingTrailingWhitespace);
        }
        Document.PageWidth = Math.Max(MinWidth, widest + 30);
    }

    // ---------- índices (letras do texto limpo) ----------

    TextPointer PointerAt(int index)
    {
        int pos = 0;
        foreach (var leaf in Leaves(_p.Inlines))
        {
            if (leaf is Run r)
            {
                int len = r.Text.Length;
                if (index <= pos + len) return r.ContentStart.GetPositionAtOffset(index - pos) ?? r.ContentEnd;
                pos += len;
            }
            else
            {
                if (index == pos) return leaf.ElementStart;
                pos++;
                if (index == pos) return leaf.ElementEnd;
            }
        }
        return _p.ContentEnd;
    }

    int IndexOf(TextPointer p)
    {
        int pos = 0;
        foreach (var leaf in Leaves(_p.Inlines))
        {
            if (leaf is Run r)
            {
                int len = r.Text.Length;
                if (p.CompareTo(r.ContentEnd) <= 0)
                    return p.CompareTo(r.ContentStart) <= 0 ? pos : pos + Math.Clamp(r.ContentStart.GetOffsetToPosition(p), 0, len);
                pos += len;
            }
            else
            {
                if (p.CompareTo(leaf.ElementStart) <= 0) return pos;
                pos++;
                if (p.CompareTo(leaf.ElementEnd) <= 0) return pos;
            }
        }
        return pos;
    }

    public int SelectionStart => IndexOf(Selection.Start);
    public int SelectionLength => IndexOf(Selection.End) - IndexOf(Selection.Start);
    public int CaretIndex => IndexOf(CaretPosition);
    public bool HasSelection => !Selection.IsEmpty;

    public void Select(int start, int length)
    {
        var a = PointerAt(start).GetInsertionPosition(LogicalDirection.Forward);
        var b = PointerAt(start + length).GetInsertionPosition(LogicalDirection.Forward);
        Selection.Select(a, b);
    }

    /// <summary>Troca letras do texto limpo (a formatação em volta continua).</summary>
    public void ReplaceRange(int start, int length, string text)
    {
        Select(start, length);
        InsertPlain(text);
    }

    /// <summary>Escreve no lugar da seleção, com a formatação de onde o cursor está.</summary>
    public void InsertPlain(string text)
    {
        if (!Selection.IsEmpty) Selection.Text = "";
        var parts = text.Split('\n');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) CaretPosition = CaretPosition.InsertLineBreak();
            if (parts[i].Length == 0) continue;
            Selection.Text = parts[i];
            var end = Selection.End;
            Selection.Select(end, end);
            CaretPosition = end;
        }
    }

    /// <summary>Troca o texto limpo inteiro mexendo só no pedaço que mudou (o resto não perde a formatação).</summary>
    void ApplyPlain(string text, int caret)
    {
        string old = PlainText;
        int pre = 0;
        while (pre < old.Length && pre < text.Length && old[pre] == text[pre]) pre++;
        int suf = 0;
        while (suf < old.Length - pre && suf < text.Length - pre && old[^(suf + 1)] == text[^(suf + 1)]) suf++;
        ReplaceRange(pre, old.Length - pre - suf, text.Substring(pre, text.Length - pre - suf));
        Select(caret, 0);
    }

    // ---------- teclado e colar ----------

    void OnKeys(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.B: ToggleBold(); break;
                case Key.I: ToggleItalic(); break;
                case Key.U: ToggleUnderline(); break;
                case Key.L or Key.E or Key.R or Key.J or Key.Space: break; // alinhamentos e "limpar" do editor: não usados
                default: return;
            }
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter && shift && EditBox.GetMode(this) != "line")
        {
            // Numa lista abre o próximo item; fora dela, quebra a linha.
            if (ListText.NextItem(PlainText, SelectionStart, SelectionLength) is { } next) ApplyPlain(next.Text, next.Caret);
            else InsertPlain("\n");
            e.Handled = true;
        }
        else if (e.Key == Key.Back && Selection.IsEmpty && ListText.Backspace(PlainText, CaretIndex) is { } back)
        {
            ApplyPlain(back.Text, back.Caret);
            e.Handled = true;
        }
    }

    void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        e.CancelCommand();
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text || text.Length == 0) return;
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        if (EditBox.GetMode(this) == "line") text = text.Replace('\n', ' ');
        InsertMarkdown(text);
    }

    /// <summary>Insere texto em Markdown (listas com - ou *, **negrito**, *itálico*…) já formatado, no lugar da seleção.</summary>
    public void InsertMarkdown(string md)
    {
        Guarded(() =>
        {
            if (!Selection.IsEmpty) Selection.Text = "";
            Adopt();
            var parsed = RichText.Parse(ListText.ToDisplay(md));
            var pos = CaretPosition.Paragraph == _p ? CaretPosition.GetInsertionPosition(LogicalDirection.Forward) : _p.ContentEnd;
            foreach (var seg in parsed.Segs)
            {
                var lines = parsed.Text.Substring(seg.Start, seg.Length).Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (i > 0) { var lb = new LineBreak(pos); pos = lb.ElementEnd; }
                    if (lines[i].Length == 0) continue;
                    var run = new Run(lines[i], pos);
                    Apply(run, seg.Fmt);
                    pos = run.ElementEnd;
                }
            }
            CaretPosition = pos;
            Selection.Select(pos, pos);
        });
        Sync();
        Fit();
    }

    // ---------- formatação da seleção ----------

    List<TextRange> Pieces()
    {
        var list = new List<TextRange>();
        if (Selection.IsEmpty) return list;
        foreach (var leaf in Leaves(_p.Inlines))
        {
            if (leaf is not Run r) continue;
            var a = r.ContentStart.CompareTo(Selection.Start) < 0 ? Selection.Start : r.ContentStart;
            var b = r.ContentEnd.CompareTo(Selection.End) > 0 ? Selection.End : r.ContentEnd;
            if (a.CompareTo(b) < 0) list.Add(new TextRange(a, b));
        }
        return list;
    }

    public void ToggleBold()
    {
        if (Selection.IsEmpty) return;
        bool all = Selection.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight w && w.ToOpenTypeWeight() > FontWeight.ToOpenTypeWeight();
        Selection.ApplyPropertyValue(TextElement.FontWeightProperty, all ? FontWeight : RichText.BoldOf(FontWeight));
    }

    public void ToggleItalic()
    {
        if (Selection.IsEmpty) return;
        bool all = Selection.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle s && s == FontStyles.Italic;
        Selection.ApplyPropertyValue(TextElement.FontStyleProperty, all ? FontStyle : FontStyles.Italic);
    }

    public void ToggleUnderline() => ToggleDecoration(TextDecorationLocation.Underline);
    public void ToggleStrike() => ToggleDecoration(TextDecorationLocation.Strikethrough);

    void ToggleDecoration(TextDecorationLocation loc)
    {
        var pieces = Pieces();
        if (pieces.Count == 0) return;
        bool all = pieces.All(p => HasDecoration(p.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection, loc));
        foreach (var piece in pieces)
        {
            var next = new TextDecorationCollection();
            if (piece.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection cur)
                foreach (var d in cur) if (d.Location != loc) next.Add(d);
            if (!all) next.Add(loc == TextDecorationLocation.Underline ? TextDecorations.Underline[0] : TextDecorations.Strikethrough[0]);
            piece.ApplyPropertyValue(Inline.TextDecorationsProperty, next);
        }
    }

    public void ToggleCode()
    {
        if (Selection.IsEmpty) return;
        bool all = Selection.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily f && f.Source == RichText.CodeFont.Source;
        Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, all ? FontFamily : RichText.CodeFont);
        Selection.ApplyPropertyValue(TextElement.BackgroundProperty, all ? Brushes.Transparent : RichText.CodeBack);
    }

    /// <summary>Cor do texto (null = a cor de sempre).</summary>
    public void SetColor(string? hex)
    {
        if (Selection.IsEmpty) return;
        Selection.ApplyPropertyValue(TextElement.ForegroundProperty, RichText.BrushOf(hex) ?? Foreground);
    }

    /// <summary>Cor do fundo do texto (null = sem fundo).</summary>
    public void SetBack(string? hex)
    {
        if (Selection.IsEmpty) return;
        Selection.ApplyPropertyValue(TextElement.BackgroundProperty, RichText.BrushOf(hex) ?? Brushes.Transparent);
    }

    public void ClearFormat()
    {
        if (Selection.IsEmpty) return;
        Selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeight);
        Selection.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyle);
        Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, FontFamily);
        Selection.ApplyPropertyValue(TextElement.ForegroundProperty, Foreground);
        Selection.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
        Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, new TextDecorationCollection());
    }

    /// <summary>Onde a seleção começa e termina (coordenadas deste campo), para posicionar a barra.</summary>
    public (Rect Start, Rect End) SelectionRects() =>
        (Selection.Start.GetCharacterRect(LogicalDirection.Forward), Selection.End.GetCharacterRect(LogicalDirection.Backward));
}
