using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Quadtab.Models;
using Quadtab.Rendering;
using Quadtab.ViewModels;

namespace Quadtab.Controls;

/// <summary>
/// Inspetor (painel à direita): cor, preenchimento, borda, texto, linha, imagem, rotação, descrição,
/// camadas, alinhar e agrupar. Vale para todos os itens selecionados.
/// </summary>
public sealed class Inspector : ContentControl
{
    static readonly (string Name, string Hex)[] Swatches =
    [
        ("Tira a cor (fica transparente)", ""), ("Branco", "#FFFFFFFF"), ("Grafite", "#FF1D2130"), ("Cinza", "#FF8E93A3"),
        ("Vermelho", "#FFFF453A"), ("Laranja", "#FFFF9F0A"), ("Amarelo", "#FFFFE27A"), ("Verde", "#FF30D158"),
        ("Menta", "#FF63D2B4"), ("Azul", "#FF0A84FF"), ("Roxo", "#FFBF5AF2"), ("Rosa", "#FFFF6B9A"),
    ];

    readonly StackPanel _body = new();
    MainViewModel? _vm;
    bool _pending;

    public Inspector()
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _body, Style = (Style)Application.Current.FindResource("OverlayScroll"), Focusable = false,
        };
        Glass = UiKit.Glass(scroll, 26, new Thickness(12, 12, 12, 12));
        Glass.Width = 284;
        Content = Glass;
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVm;
            _vm = e.NewValue as MainViewModel;
            if (_vm is not null) _vm.PropertyChanged += OnVm;
            Rebuild();
        };
    }

    public GlassPanel Glass { get; }

    /// <summary>O usuário quer o inspetor (botão da barra). Ele só aparece com algo selecionado.</summary>
    public bool Wanted
    {
        get => _wanted;
        set { _wanted = value; Rebuild(); }
    }
    bool _wanted = true;

    void OnVm(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.SelectionCount) or nameof(MainViewModel.Selected)) Later();
    }

    /// <summary>Junta várias mudanças de seleção numa só reconstrução.</summary>
    void Later()
    {
        if (_pending) return;
        _pending = true;
        Dispatcher.BeginInvoke(() => { _pending = false; Rebuild(); }, System.Windows.Threading.DispatcherPriority.Background);
    }

    List<BoardItemViewModel> Sel => _vm?.SelectedItems.ToList() ?? [];

    public void Rebuild()
    {
        _body.Children.Clear();
        var sel = Sel;
        if (_vm is null || sel.Count == 0 || !_wanted)
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        Visibility = Visibility.Visible;
        var first = sel[0];
        var kinds = sel.Select(i => i.Kind).Distinct().ToList();
        bool single = sel.Count == 1;

        // cabeçalho
        var head = new DockPanel { Margin = new Thickness(4, 0, 0, 2) };
        var close = UiKit.IconButton("IcClose", "Esconde o painel de propriedades", () => Wanted = false, 28, 13);
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(new TextBlock
        {
            Text = single ? first.TypeLabel : $"{sel.Count} itens", FontSize = 17, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
        });
        _body.Children.Add(head);

        // cor do papel (post-it, página, bloco, texto) ou cor rápida
        if (kinds.All(k => k is ItemKind.PostIt or ItemKind.Page or ItemKind.Block or ItemKind.Text or ItemKind.Shape or ItemKind.TextBox or ItemKind.Line or ItemKind.Table))
        {
            _body.Children.Add(UiKit.Section("Cor"));
            var dots = new WrapPanel();
            foreach (var c in Palette.Colors)
            {
                var key = c.Key;
                dots.Children.Add(UiKit.Swatch(c.PaperBrush, sel.All(i => i.ColorKey == key), c.Name, () =>
                {
                    foreach (var i in Sel) _vm.SetColor(i, key);
                    Rebuild();
                }));
            }
            _body.Children.Add(dots);
        }

        if (kinds.All(k => k is ItemKind.PostIt or ItemKind.Page or ItemKind.Block))
        {
            _body.Children.Add(UiKit.Section("Fixação"));
            var chips = new WrapPanel();
            foreach (var (name, style) in new[] { ("Alfinete", NoteStyle.Pin), ("Fita", NoteStyle.Tape), ("Solto", NoteStyle.None) })
                chips.Children.Add(UiKit.Chip(name, sel.All(i => i.Fastening == (Fastening)(int)style), () => { foreach (var i in Sel) _vm.SetStyle(i, style); Rebuild(); }));
            if (kinds.All(k => k == ItemKind.PostIt))
                chips.Children.Add(UiKit.Chip("Pautado", sel.All(i => i.Lined), () => { foreach (var i in Sel) _vm.SetStyle(i, NoteStyle.Lined); Rebuild(); }));
            _body.Children.Add(chips);
        }

        bool styled = kinds.All(k => k is ItemKind.Shape or ItemKind.TextBox or ItemKind.Table);
        if (styled)
        {
            _body.Children.Add(UiKit.Section("Preenchimento"));
            _body.Children.Add(SwatchRow(first.Style.Preenchimento, hex => _vm.EditStyle(s => s.Preenchimento = hex)));
        }
        if (styled || kinds.All(k => k == ItemKind.Line))
        {
            _body.Children.Add(UiKit.Section(kinds.All(k => k == ItemKind.Line) ? "Cor da linha" : "Borda"));
            _body.Children.Add(SwatchRow(first.Style.Borda, hex => _vm.EditStyle(s => s.Borda = hex)));
            _body.Children.Add(UiKit.Slider("Espessura", 0, 16, first.Style.Espessura, v => _vm.EditStyle(s => s.Espessura = v)));
        }
        if (styled || kinds.All(k => k is ItemKind.Image or ItemKind.Drawing))
            _body.Children.Add(UiKit.Slider("Opacidade", 10, 100, first.Style.Opacidade * 100, v => _vm.EditStyle(s => s.Opacidade = v / 100), "0'%'"));

        // texto
        if (kinds.All(k => k is ItemKind.TextBox or ItemKind.Shape or ItemKind.Table or ItemKind.Text))
            AddTextSection(first, kinds);

        // linha
        if (single && first.Line is { } line) AddLineSection(first, line);

        // desenho
        if (kinds.All(k => k == ItemKind.Drawing))
        {
            _body.Children.Add(UiKit.Section("Traço"));
            _body.Children.Add(SwatchRow(first.Strokes?.FirstOrDefault()?.Cor ?? "", hex =>
            {
                if (hex.Length == 0) return;
                _vm.Checkpoint(coalesce: true);
                foreach (var i in Sel) if (i.Strokes is { } st) { foreach (var s in st) s.Cor = hex; i.Touch(nameof(BoardItemViewModel.Strokes)); }
                _vm.CommitMove();
            }, allowNone: false));
        }

        // imagem
        if (single && first.Kind == ItemKind.Image)
        {
            _body.Children.Add(UiKit.Section("Imagem"));
            _body.Children.Add(UiKit.Slider("Cantos", 0, 120, first.Radius, v => { _vm.Checkpoint(coalesce: true); first.Radius = v; _vm.CommitMove(); }));
            string[] names = ["Recorte ←", "Recorte ↑", "Recorte →", "Recorte ↓"];
            for (int k = 0; k < 4; k++)
            {
                int idx = k;
                _body.Children.Add(UiKit.Slider(names[k], 0, 45, first.Crop[k] * 100, v =>
                {
                    _vm.Checkpoint(coalesce: true);
                    var c = (double[])first.Crop.Clone();
                    c[idx] = v / 100;
                    first.Crop = c;
                    _vm.CommitMove();
                }, "0'%'"));
            }
        }

        // posição
        if (single && first.Kind is not (ItemKind.Line or ItemKind.Text))
        {
            _body.Children.Add(UiKit.Section("Rotação"));
            _body.Children.Add(UiKit.Slider("Ângulo", -180, 180, first.Rotation, v => { _vm.Checkpoint(coalesce: true); first.Rotation = v; _vm.CommitMove(); }, "0'°'"));
        }

        // organizar
        _body.Children.Add(UiKit.Section("Organizar"));
        var arrange = new WrapPanel();
        arrange.Children.Add(UiKit.IconButton("IcArrowUp", "Sobe uma camada · Ctrl+]", () => { foreach (var i in Sel) _vm.Step(i, +1); }));
        arrange.Children.Add(UiKit.IconButton("IcArrowDown", "Desce uma camada · Ctrl+[", () => { foreach (var i in Sel) _vm.Step(i, -1); }));
        arrange.Children.Add(UiKit.IconButton("IcChevronUp", "Coloca na frente de todos · Ctrl+Shift+]", () => _vm.ToTop(Sel)));
        arrange.Children.Add(UiKit.IconButton("IcChevronDown", "Coloca atrás de todos · Ctrl+Shift+[", () => _vm.ToBottom(Sel)));
        arrange.Children.Add(UiKit.IconButton(first.Locked ? "IcUnlock" : "IcLock", first.Locked ? "Destrava o item para poder mover · Ctrl+L" : "Trava o item no lugar · Ctrl+L", () => { _vm.ToggleLock(first); Rebuild(); }));
        arrange.Children.Add(UiKit.IconButton("IcDuplicate", "Cria uma cópia ao lado · Ctrl+D", () => _vm.DuplicateMany(Sel, new Vector(30, 30))));
        arrange.Children.Add(UiKit.IconButton("IcTrash", "Exclui os itens selecionados · Delete", () => _vm.RemoveSelected()));
        _body.Children.Add(arrange);

        if (sel.Count >= 2)
        {
            _body.Children.Add(UiKit.Section("Alinhar"));
            var align = new WrapPanel();
            foreach (var (how, icon, tip) in new[]
            {
                ("esquerda", "IcAlignLeft", "Alinhar à esquerda"), ("centro", "IcAlignCenter", "Centralizar na horizontal"), ("direita", "IcAlignRight", "Alinhar à direita"),
                ("topo", "IcAlignTop", "Alinhar no topo"), ("meio", "IcAlignMiddle", "Centralizar na vertical"), ("base", "IcAlignBottom", "Alinhar na base"),
            })
                align.Children.Add(UiKit.IconButton(icon, tip, () => _vm.Align(how)));
            if (sel.Count >= 3)
            {
                align.Children.Add(UiKit.IconButton("IcDistributeH", "Deixa o mesmo espaço entre os itens, na horizontal", () => _vm.Distribute(true)));
                align.Children.Add(UiKit.IconButton("IcDistributeV", "Deixa o mesmo espaço entre os itens, na vertical", () => _vm.Distribute(false)));
            }
            _body.Children.Add(align);
        }
        var group = new WrapPanel { Margin = new Thickness(-3, 6, 0, 0) };
        if (sel.Count >= 2) group.Children.Add(UiKit.Pill("Agrupar", () => { _vm.GroupSelection(); Rebuild(); }, "IcGroup"));
        if (sel.Any(i => i.Group is not null)) group.Children.Add(UiKit.Pill("Desagrupar", () => { _vm.Ungroup(); Rebuild(); }, "IcGroup"));
        if (single) group.Children.Add(UiKit.Pill("Copiar estilo", () => { _vm.CopyStyle(first); Rebuild(); }, "IcBrush"));
        if (_vm.HasStyleClip) group.Children.Add(UiKit.Pill("Colar estilo", () => { _vm.PasteStyle(); Rebuild(); }, "IcBrush"));
        _body.Children.Add(group);

        // descrição (texto alternativo)
        if (single)
        {
            _body.Children.Add(UiKit.Section("Descrição (aparece ao passar o mouse)"));
            var box = new TextBox
            {
                Style = (Style)Application.Current.FindResource("SearchBox"), Text = first.Description, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                MinHeight = 44, FontSize = 13.5, VerticalContentAlignment = VerticalAlignment.Top,
            };
            box.TextChanged += (_, _) => _vm.SetDescription(first, box.Text);
            var field = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(2, 0, 2, 4), Child = box };
            field.SetResourceReference(Border.BackgroundProperty, "FillBrush");
            _body.Children.Add(field);
        }
    }

    void AddTextSection(BoardItemViewModel first, List<ItemKind> kinds)
    {
        _body.Children.Add(UiKit.Section("Texto"));
        var s = first.Style;
        var fonts = new WrapPanel();
        fonts.Children.Add(UiKit.Chip("Inter", s.Fonte != "Nunito", () => { _vm!.EditStyle(x => x.Fonte = "Inter"); Rebuild(); }));
        fonts.Children.Add(UiKit.Chip("Nunito", s.Fonte == "Nunito", () => { _vm!.EditStyle(x => x.Fonte = "Nunito"); Rebuild(); }));
        fonts.Children.Add(UiKit.Chip("", s.Negrito, () => { _vm!.EditStyle(x => x.Negrito = !x.Negrito); Rebuild(); }, "IcBold", "Deixa o texto em negrito"));
        fonts.Children.Add(UiKit.Chip("", s.Italico, () => { _vm!.EditStyle(x => x.Italico = !x.Italico); Rebuild(); }, "IcItalic", "Deixa o texto em itálico"));
        if (!kinds.Contains(ItemKind.Text))
            fonts.Children.Add(UiKit.Chip("", s.Lista, () => { _vm!.EditStyle(x => x.Lista = !x.Lista); Rebuild(); }, "IcList", "Transforma as linhas numa lista com marcadores"));
        _body.Children.Add(fonts);
        _body.Children.Add(UiKit.Slider("Tamanho", 8, kinds.Contains(ItemKind.Text) ? 200 : 96, s.Tamanho, v => _vm!.EditStyle(x => x.Tamanho = v)));
        if (!kinds.Contains(ItemKind.Text))
        {
            var align = new WrapPanel();
            foreach (var (key, icon, tip) in new[] { ("esquerda", "IcAlignLeft", "Alinha o texto à esquerda"), ("centro", "IcAlignCenter", "Centraliza o texto"), ("direita", "IcAlignRight", "Alinha o texto à direita") })
                align.Children.Add(UiKit.Chip("", s.Alinhamento == key, () => { _vm!.EditStyle(x => x.Alinhamento = key); Rebuild(); }, icon, tip));
            _body.Children.Add(align);
            _body.Children.Add(UiKit.Section("Cor do texto"));
            _body.Children.Add(SwatchRow(s.CorTexto, hex => _vm!.EditStyle(x => x.CorTexto = hex), allowNone: false));
        }
    }

    void AddLineSection(BoardItemViewModel item, LineData line)
    {
        _body.Children.Add(UiKit.Section("Linha"));
        void Change(Action<LineData> a)
        {
            _vm!.Checkpoint();
            a(line);
            item.Touch(nameof(BoardItemViewModel.Line));
            _vm.CommitMove();
            Rebuild();
        }
        var types = new WrapPanel();
        foreach (var (key, name) in new[] { ("reta", "Reta"), ("curva", "Curva"), ("angulo", "Ângulo") })
            types.Children.Add(UiKit.Chip(name, line.Tipo == key, () => Change(l => l.Tipo = key)));
        _body.Children.Add(types);
        foreach (var (label, end) in new[] { ("Início", line.De), ("Fim", line.Para) })
        {
            var row = new WrapPanel();
            var name = UiKit.Label(label, 13, dim: false, FontWeights.Medium);
            name.Width = 52;
            name.Margin = new Thickness(4, 0, 0, 6);
            row.Children.Add(name);
            foreach (var (key, txt) in new[] { ("nenhuma", "Nada"), ("seta", "Seta"), ("bolinha", "Bolinha") })
                row.Children.Add(UiKit.Chip(txt, end.Ponta == key, () => Change(_ => end.Ponta = key)));
            _body.Children.Add(row);
        }
    }

    static WrapPanel SwatchRow(string? current, Action<string> pick, bool allowNone = true)
    {
        var row = new WrapPanel();
        foreach (var (name, hex) in Swatches)
        {
            if (hex.Length == 0 && !allowNone) continue;
            Brush fill = hex.Length == 0 ? NoneBrush : ShadowCache.BrushOf((Color)ColorConverter.ConvertFromString(hex));
            bool on = string.Equals(current ?? "", hex, StringComparison.OrdinalIgnoreCase);
            row.Children.Add(UiKit.Swatch(fill, on, name, () => pick(hex), 22));
        }
        return row;
    }

    // "Sem cor": branco cortado por um risco vermelho
    static readonly Brush NoneBrush = CreateNone();

    static Brush CreateNone()
    {
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 10, 10))));
        g.Children.Add(new GeometryDrawing(null, new Pen(ShadowCache.BrushOf(Color.FromRgb(0xFF, 0x45, 0x3A)), 1.2), new LineGeometry(new Point(10, 0), new Point(0, 10))));
        var b = new DrawingBrush(g);
        b.Freeze();
        return b;
    }
}
