using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Quadtab.Rendering;
using Quadtab.Services;

namespace Quadtab.Controls;

/// <summary>
/// Editor de páginas (.md do cofre) ao lado do quadro: modo Editar (texto com autocompletar de [[ ]])
/// e modo Ler (Markdown desenhado, links clicáveis, caixas de seleção, tags e "Mencionado em").
/// </summary>
public sealed partial class PageEditor : Grid
{
    [GeneratedRegex(@"\*\*(.+?)\*\*|(?<![\*\w])\*(?!\s)(.+?)\*(?!\*)|`([^`]+)`|\[\[([^\[\]|]+)(?:\|([^\[\]]+))?\]\]|\[([^\]]+)\]\((https?://[^)\s]+)\)|(?<![\w#&])#([\p{L}\p{N}_\-/]+)|(https?://[^\s)]+)")]
    private static partial Regex Inline();

    [GeneratedRegex(@"\[\[([^\]\n]*)$")] private static partial Regex OpenLink();

    readonly TextBox _title = new() { FontSize = 26, FontWeight = FontWeights.ExtraBold, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0) };
    readonly TextBox _editor = new()
    {
        AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
        FontSize = 15, Padding = new Thickness(0, 4, 0, 40), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    readonly FlowDocumentScrollViewer _reader = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsToolBarVisible = false, Focusable = false };
    readonly Border _suggest = new() { CornerRadius = new CornerRadius(12), Padding = new Thickness(4), Visibility = Visibility.Collapsed, MinWidth = 220 };
    readonly StackPanel _suggestList = new();
    readonly Canvas _overlay = new();
    readonly Button _modeButton;
    readonly Button _expandButton;
    readonly DispatcherTimer _save;
    List<string> _candidates = [];
    int _pick;
    bool _loading;

    public PageEditor()
    {
        Glass = UiKit.Glass(BuildContent(out _modeButton, out _expandButton), 28, new Thickness(28, 16, 18, 16));
        Children.Add(Glass);
        _save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _save.Tick += (_, _) => Flush();
        Visibility = Visibility.Collapsed;
    }

    public GlassPanel Glass { get; }

    /// <summary>Página aberta agora (caminho completo).</summary>
    public string? Path { get; private set; }

    public bool Expanded { get; private set; }

    public event Action? CloseRequested;
    public event Action? ExpandChanged;

    bool Editing => _editor.Visibility == Visibility.Visible;

    FrameworkElement BuildContent(out Button modeButton, out Button expandButton)
    {
        var dock = new DockPanel();

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(bar, Dock.Top);
        var close = UiKit.IconButton("IcClose", "Fecha a nota (ela já fica salva)", () => CloseRequested?.Invoke(), 34, 14);
        DockPanel.SetDock(close, Dock.Right);
        bar.Children.Add(close);
        expandButton = UiKit.IconButton("IcExpand", "Abre a nota na área toda", () => { Expanded = !Expanded; UpdateExpand(); ExpandChanged?.Invoke(); }, 34, 16);
        DockPanel.SetDock(expandButton, Dock.Right);
        bar.Children.Add(expandButton);
        modeButton = UiKit.Pill("Ler", () => SetMode(!Editing), "IcPage");
        DockPanel.SetDock(modeButton, Dock.Right);
        modeButton.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(modeButton);
        var crumb = UiKit.Label("Nota", 12.5);
        bar.Children.Add(crumb);
        dock.Children.Add(bar);

        _title.SetResourceReference(TextBox.ForegroundProperty, "InkBrush");
        _title.SetResourceReference(TextBox.CaretBrushProperty, "InkBrush");
        _title.FontFamily = (FontFamily)Application.Current.FindResource("Inter");
        _title.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitTitle(); _editor.Focus(); e.Handled = true; } };
        _title.LostKeyboardFocus += (_, _) => CommitTitle();
        DockPanel.SetDock(_title, Dock.Top);
        _title.Margin = new Thickness(0, 0, 0, 10);
        dock.Children.Add(_title);

        _editor.SetResourceReference(TextBox.ForegroundProperty, "InkBrush");
        _editor.SetResourceReference(TextBox.CaretBrushProperty, "AccentBrush");
        _editor.SetResourceReference(TextBox.SelectionBrushProperty, "AccentBrush");
        _editor.FontFamily = (FontFamily)Application.Current.FindResource("Inter");
        _editor.TextChanged += (_, _) => { if (!_loading) { _save.Stop(); _save.Start(); } UpdateSuggest(); };
        _editor.SelectionChanged += (_, _) => UpdateSuggest();
        _editor.PreviewKeyDown += OnEditorKey;
        Behaviors.ListTyping.Attach(_editor, bullets: false); // texto puro: aqui a bolinha continua sendo "- "
        _editor.PreviewMouseLeftButtonDown += OnEditorClick;
        _editor.LostKeyboardFocus += (_, _) => HideSuggest();

        _reader.Document = new FlowDocument();
        _reader.PreviewMouseDoubleClick += (_, e) => { SetMode(true); e.Handled = true; };

        _suggest.SetResourceReference(Border.BackgroundProperty, "Glass2Brush");
        _suggest.Effect = null;
        _suggest.BorderThickness = new Thickness(1);
        _suggest.SetResourceReference(Border.BorderBrushProperty, "SepBrush");
        _suggest.Child = _suggestList;
        _overlay.Children.Add(_suggest);

        var body = new Grid();
        body.Children.Add(_editor);
        body.Children.Add(_reader);
        body.Children.Add(_overlay);
        dock.Children.Add(body);
        return dock;
    }

    // ---------- abrir, salvar, renomear ----------

    public void Open(string path)
    {
        Flush();
        Path = path;
        _loading = true;
        _title.Text = System.IO.Path.GetFileNameWithoutExtension(path);
        _editor.Text = Markdown.StripTitle(StorageService.ReadPage(path), _title.Text);
        _loading = false;
        Visibility = Visibility.Visible;
        SetMode(_editor.Text.Trim().Length == 0 || _editor.Text.Contains("Escreva aqui"));
    }

    public void Close()
    {
        Flush();
        Path = null;
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Grava agora (antes de fechar, trocar de página ou sair).</summary>
    public void Flush()
    {
        _save.Stop();
        if (Path is null || !File.Exists(Path)) return;
        var md = $"# {_title.Text.Trim()}\n\n{_editor.Text.Replace("\r", "")}\n";
        if (md == StorageService.ReadPage(Path).Replace("\r", "")) return;
        StorageService.WritePage(Path, md);
        // A folha no quadro mostra a prévia do texto novo.
        foreach (var it in App.ViewModel.Items.Where(i => i.FileFullPath is { } f && string.Equals(f, Path, StringComparison.OrdinalIgnoreCase)))
            if (it.Text != _editor.Text) it.Text = _editor.Text.Replace("\r", "");
        App.Library.Refresh();
    }

    void CommitTitle()
    {
        if (Path is null) return;
        var name = _title.Text.Trim();
        if (name.Length == 0 || name == System.IO.Path.GetFileNameWithoutExtension(Path)) { _title.Text = System.IO.Path.GetFileNameWithoutExtension(Path); return; }
        Flush();
        App.RenamePage(Path, name);
    }

    /// <summary>Chamado depois de renomear (por aqui ou pela lateral).</summary>
    public void Renamed(string oldPath, string newPath)
    {
        if (!string.Equals(Path, oldPath, StringComparison.OrdinalIgnoreCase)) return;
        Path = newPath;
        _title.Text = System.IO.Path.GetFileNameWithoutExtension(newPath);
        if (!Editing) Render();
    }

    void UpdateExpand()
    {
        _expandButton.Content = new Icon { Data = UiKit.G(Expanded ? "IcCollapse" : "IcExpand"), Size = 16 };
        _expandButton.ToolTip = Expanded ? "Divide a área com o quadro de novo" : "Abre a nota na área toda";
    }

    void SetMode(bool edit)
    {
        _editor.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
        _reader.Visibility = edit ? Visibility.Collapsed : Visibility.Visible;
        _modeButton.ToolTip = edit ? "Mostra a nota formatada · Esc" : "Edita o texto da nota · 2 cliques";
        _modeButton.Content = UiKit.Row(new Icon { Data = UiKit.G(edit ? "IcPage" : "IcPencil"), Size = 15 },
            new TextBlock { Text = edit ? "Ler" : "Editar", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        HideSuggest();
        if (edit) Dispatcher.BeginInvoke(() => { _editor.Focus(); }, DispatcherPriority.Input);
        else { Flush(); Render(); }
    }

    // ---------- Ctrl + clique num [[link]] no modo Editar ----------

    void OnEditorClick(object sender, MouseButtonEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        int idx = _editor.GetCharacterIndexFromPoint(e.GetPosition(_editor), snapToText: true);
        if (idx < 0) return;
        foreach (Match m in Markdown.WikiLink().Matches(_editor.Text))
            if (idx >= m.Index && idx <= m.Index + m.Length)
            {
                FollowLink(m.Groups[1].Value.Trim());
                e.Handled = true;
                return;
            }
    }

    /// <summary>Abre a página (ou o quadro, com "Quadro: Nome"). Página que não existe é criada.</summary>
    void FollowLink(string target)
    {
        Flush();
        if (target.StartsWith("Quadro:", StringComparison.OrdinalIgnoreCase))
        {
            var name = target[7..].Trim();
            var board = StorageService.BoardFiles().FirstOrDefault(f => string.Equals(System.IO.Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase));
            if (board is not null) App.OpenBoard(board);
            return;
        }
        var page = StorageService.FindPage(target) ?? StorageService.CreatePage(target, $"# {target}\n\n");
        App.Library.Refresh();
        App.OpenPage(page);
    }

    // ---------- autocompletar [[ ]] ----------

    void UpdateSuggest()
    {
        if (!Editing || !_editor.IsKeyboardFocused || _editor.SelectionLength > 0) { HideSuggest(); return; }
        int caret = _editor.CaretIndex;
        int lineStart = _editor.Text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
        if (caret < lineStart) { HideSuggest(); return; }
        var m = OpenLink().Match(_editor.Text[lineStart..caret]);
        if (!m.Success) { HideSuggest(); return; }
        var q = ViewModels.BoardItemViewModel.Normalize(m.Groups[1].Value);
        var pages = StorageService.PageFiles().Select(f => System.IO.Path.GetFileNameWithoutExtension(f));
        var boards = StorageService.BoardFiles().Select(f => "Quadro: " + System.IO.Path.GetFileNameWithoutExtension(f));
        _candidates = pages.Concat(boards)
            .Where(n => !string.Equals(n, _title.Text, StringComparison.OrdinalIgnoreCase) && ViewModels.BoardItemViewModel.Normalize(n).Contains(q))
            .OrderBy(n => ViewModels.BoardItemViewModel.Normalize(n).StartsWith(q, StringComparison.Ordinal) ? 0 : 1).ThenBy(n => n).Take(8).ToList();
        if (_candidates.Count == 0) { HideSuggest(); return; }
        _pick = Math.Clamp(_pick, 0, _candidates.Count - 1);
        _suggestList.Children.Clear();
        for (int i = 0; i < _candidates.Count; i++)
        {
            var name = _candidates[i];
            var row = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6), Cursor = Cursors.Hand };
            if (i == _pick) row.SetResourceReference(Border.BackgroundProperty, "Accent14Brush");
            else row.Background = Brushes.Transparent;
            row.Child = UiKit.Row(new Icon { Data = UiKit.G(name.StartsWith("Quadro: ", StringComparison.Ordinal) ? "IcBoard" : "IcPage"), Size = 15, Margin = new Thickness(0, 0, 8, 0) },
                new TextBlock { Text = name, FontSize = 14 });
            row.MouseLeftButtonDown += (_, e) => { Accept(name); e.Handled = true; };
            _suggestList.Children.Add(row);
        }
        var r = _editor.GetRectFromCharacterIndex(caret);
        var p = _editor.TranslatePoint(r.BottomLeft, _overlay);
        Canvas.SetLeft(_suggest, Math.Max(0, p.X - 8));
        Canvas.SetTop(_suggest, p.Y + 4);
        _suggest.Visibility = Visibility.Visible;
    }

    void HideSuggest()
    {
        _suggest.Visibility = Visibility.Collapsed;
        _pick = 0;
    }

    void Accept(string name)
    {
        int caret = _editor.CaretIndex;
        int open = _editor.Text.LastIndexOf("[[", caret, StringComparison.Ordinal);
        if (open < 0) return;
        bool closed = _editor.Text.Length >= caret + 2 && _editor.Text.Substring(caret, 2) == "]]";
        var insert = name + (closed ? "" : "]]");
        _editor.Text = _editor.Text[..(open + 2)] + insert + _editor.Text[caret..];
        _editor.CaretIndex = open + 2 + name.Length + 2;
        HideSuggest();
    }

    void OnEditorKey(object sender, KeyEventArgs e)
    {
        if (_suggest.Visibility == Visibility.Visible)
        {
            switch (e.Key)
            {
                case Key.Down: _pick = (_pick + 1) % _candidates.Count; UpdateSuggest(); e.Handled = true; return;
                case Key.Up: _pick = (_pick + _candidates.Count - 1) % _candidates.Count; UpdateSuggest(); e.Handled = true; return;
                case Key.Enter or Key.Tab: Accept(_candidates[_pick]); e.Handled = true; return;
                case Key.Escape: HideSuggest(); e.Handled = true; return;
            }
        }
        if (e.Key == Key.Escape) { SetMode(false); e.Handled = true; }
    }

    // ---------- modo Ler: Markdown desenhado ----------

    void Render()
    {
        var doc = new FlowDocument { PagePadding = new Thickness(0, 4, 4, 40), FontFamily = (FontFamily)Application.Current.FindResource("Inter"), FontSize = 15 };
        doc.SetResourceReference(FlowDocument.ForegroundProperty, "InkBrush");
        var lines = _editor.Text.Replace("\r", "").Split('\n');
        bool code = false;
        Paragraph? codeBlock = null;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            int lineIndex = i;
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                code = !code;
                if (code)
                {
                    codeBlock = new Paragraph { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13.5, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 4, 0, 8) };
                    codeBlock.SetResourceReference(Paragraph.BackgroundProperty, "FillBrush");
                    doc.Blocks.Add(codeBlock);
                }
                continue;
            }
            if (code) { codeBlock!.Inlines.Add(new Run((codeBlock.Inlines.Count > 0 ? "\n" : "") + line)); continue; }

            var trimmed = line.TrimStart();
            var h = Markdown.Heading().Match(line);
            Paragraph p;
            if (h.Success)
            {
                int level = h.Groups[1].Length;
                p = new Paragraph { FontSize = level switch { 1 => 24, 2 => 20, 3 => 17, _ => 15.5 }, FontWeight = FontWeights.Bold, Margin = new Thickness(0, level == 1 ? 14 : 10, 0, 4) };
                AddInlines(p, h.Groups[2].Value);
            }
            else if (trimmed.Length == 0)
            {
                continue;
            }
            else if (trimmed is "---" or "***")
            {
                var sep = new BlockUIContainer(new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6) });
                ((Border)sep.Child).SetResourceReference(Border.BackgroundProperty, "SepBrush");
                doc.Blocks.Add(sep);
                continue;
            }
            else if (Regex.Match(trimmed, @"^[-*] \[( |x|X)\] (.*)$") is { Success: true } task)
            {
                bool done = task.Groups[1].Value != " ";
                p = new Paragraph { Margin = new Thickness(Indent(line), 2, 0, 2) };
                var box = new CheckBox { IsChecked = done, Margin = new Thickness(0, 0, 8, -3), Cursor = Cursors.Hand };
                box.Click += (_, _) => ToggleTask(lineIndex);
                p.Inlines.Add(new InlineUIContainer(box) { BaselineAlignment = BaselineAlignment.Center });
                var span = new Span();
                AddInlines(span, task.Groups[2].Value);
                if (done) { span.TextDecorations = TextDecorations.Strikethrough; span.SetResourceReference(TextElement.ForegroundProperty, "Ink3Brush"); }
                p.Inlines.Add(span);
            }
            else if (Regex.Match(trimmed, @"^([-*+]|\d+[.)]) (.*)$") is { Success: true } bullet)
            {
                p = new Paragraph { Margin = new Thickness(Indent(line) + 4, 2, 0, 2) };
                var mark = char.IsDigit(bullet.Groups[1].Value[0]) ? bullet.Groups[1].Value + "  " : "•  ";
                p.Inlines.Add(new Run(mark) { FontWeight = FontWeights.Bold });
                AddInlines(p, bullet.Groups[2].Value);
            }
            else if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                p = new Paragraph { Margin = new Thickness(0, 4, 0, 4), Padding = new Thickness(12, 2, 0, 2), BorderThickness = new Thickness(3, 0, 0, 0), FontStyle = FontStyles.Italic };
                p.SetResourceReference(Paragraph.BorderBrushProperty, "AccentBrush");
                AddInlines(p, trimmed[2..]);
            }
            else
            {
                p = new Paragraph { Margin = new Thickness(0, 2, 0, 6), LineHeight = 24 };
                AddInlines(p, line);
            }
            doc.Blocks.Add(p);
        }

        // Tags e "Mencionado em" (páginas que linkam para esta)
        var tags = Markdown.Tags(_editor.Text).ToList();
        var backlinks = Path is null ? [] : StorageService.PageFiles()
            .Where(f => !string.Equals(f, Path, StringComparison.OrdinalIgnoreCase) &&
                        Markdown.Links(StorageService.ReadPage(f)).Any(l => string.Equals(l, _title.Text, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (tags.Count > 0 || backlinks.Count > 0)
        {
            var sep = new BlockUIContainer(new Border { Height = 1, Margin = new Thickness(0, 18, 0, 6) });
            ((Border)sep.Child).SetResourceReference(Border.BackgroundProperty, "SepBrush");
            doc.Blocks.Add(sep);
        }
        if (tags.Count > 0)
        {
            var p = new Paragraph { Margin = new Thickness(0, 4, 0, 4) };
            foreach (var t in tags) p.Inlines.Add(TagRun(t));
            doc.Blocks.Add(p);
        }
        if (backlinks.Count > 0)
        {
            var head = new Paragraph(new Run("Mencionado em")) { FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) };
            head.SetResourceReference(TextElement.ForegroundProperty, "Ink3Brush");
            doc.Blocks.Add(head);
            foreach (var b in backlinks)
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(b);
                var link = new Hyperlink(new Run(name)) { TextDecorations = null, FontWeight = FontWeights.SemiBold };
                link.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
                link.Click += (_, _) => App.OpenPage(b);
                doc.Blocks.Add(new Paragraph(link) { Margin = new Thickness(0, 2, 0, 2) });
            }
        }
        _reader.Document = doc;
    }

    static double Indent(string line) => (line.Length - line.TrimStart().Length) * 6;

    Inline TagRun(string tag)
    {
        var r = new Run(" #" + tag + " ") { FontSize = 13, FontWeight = FontWeights.SemiBold };
        r.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
        r.SetResourceReference(TextElement.BackgroundProperty, "Accent14Brush");
        return new Span(r) { };
    }

    void AddInlines(Span target, string text) => AddTo(target.Inlines, text);
    void AddInlines(Paragraph target, string text) => AddTo(target.Inlines, text);

    void AddTo(InlineCollection inlines, string text)
    {
        int pos = 0;
        foreach (Match m in Inline().Matches(text))
        {
            if (m.Index > pos) inlines.Add(new Run(text[pos..m.Index]));
            pos = m.Index + m.Length;
            if (m.Groups[1].Success) { var s = new Bold(); AddTo(s.Inlines, m.Groups[1].Value); inlines.Add(s); }
            else if (m.Groups[2].Success) { var s = new Italic(); AddTo(s.Inlines, m.Groups[2].Value); inlines.Add(s); }
            else if (m.Groups[3].Success)
            {
                var r = new Run(m.Groups[3].Value) { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13.5 };
                r.SetResourceReference(TextElement.BackgroundProperty, "FillBrush");
                inlines.Add(r);
            }
            else if (m.Groups[4].Success)
            {
                var target = m.Groups[4].Value.Trim();
                var label = m.Groups[5].Success ? m.Groups[5].Value : target;
                bool board = target.StartsWith("Quadro:", StringComparison.OrdinalIgnoreCase);
                bool exists = board
                    ? StorageService.BoardFiles().Any(f => string.Equals(System.IO.Path.GetFileNameWithoutExtension(f), target[7..].Trim(), StringComparison.OrdinalIgnoreCase))
                    : StorageService.FindPage(target) is not null;
                var link = new Hyperlink(new Run(label)) { TextDecorations = null, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand };
                link.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
                if (!exists)
                {
                    link.Foreground = new SolidColorBrush(Color.FromArgb(120, 0x0A, 0x84, 0xFF)); // link para página que ainda não existe
                    link.ToolTip = "Nota ainda não existe: clique para criar";
                }
                link.Click += (_, _) => FollowLink(target);
                inlines.Add(link);
            }
            else if (m.Groups[6].Success) inlines.Add(External(m.Groups[6].Value, m.Groups[7].Value));
            else if (m.Groups[8].Success) inlines.Add(TagRun(m.Groups[8].Value));
            else if (m.Groups[9].Success) inlines.Add(External(m.Groups[9].Value, m.Groups[9].Value));
        }
        if (pos < text.Length) inlines.Add(new Run(text[pos..]));
    }

    static Hyperlink External(string label, string url)
    {
        var link = new Hyperlink(new Run(label)) { Cursor = Cursors.Hand };
        link.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
        link.Click += (_, _) => ItemViewBase.OpenExternal(url);
        return link;
    }

    void ToggleTask(int lineIndex)
    {
        var lines = _editor.Text.Replace("\r", "").Split('\n');
        if (lineIndex >= lines.Length) return;
        var l = lines[lineIndex];
        lines[lineIndex] = l.Contains("[ ]") ? ReplaceFirst(l, "[ ]", "[x]") : Regex.Replace(l, @"\[(x|X)\]", "[ ]");
        _editor.Text = string.Join("\n", lines);
        Flush();
        Render();
    }

    static string ReplaceFirst(string s, string a, string b)
    {
        int i = s.IndexOf(a, StringComparison.Ordinal);
        return i < 0 ? s : s[..i] + b + s[(i + a.Length)..];
    }
}
