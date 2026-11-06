using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Quadtab.Behaviors;
using Quadtab.Models;
using Quadtab.Rendering;
using Quadtab.Services;
using Quadtab.ViewModels;

namespace Quadtab.Controls;

/// <summary>
/// Aba "Designs" (abaixo de "Novo quadro"): galeria de vidro com os modelos prontos de mapas mentais.
/// Clicar escolhe; "Importar design como novo quadro" (ou 2 cliques no cartão) cria o quadro e abre.
/// </summary>
public sealed class DesignsPanel : Grid
{
    const double CardWidth = 176, PreviewHeight = 112;

    readonly UniformGrid _grid = new() { Columns = 5 };
    readonly List<Border> _cards = [];
    readonly TextBlock _chosen = UiKit.Label("", 13);
    int _pick;

    public DesignsPanel()
    {
        Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
        Visibility = Visibility.Collapsed;
        Focusable = true;
        FocusVisualStyle = null;
        MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == this) Hide(); };
        PreviewKeyDown += OnKey;

        var stack = new StackPanel();

        // Cabeçalho: título, frase e ✕
        var head = new DockPanel { Margin = new Thickness(8, 4, 0, 14) };
        var close = UiKit.IconButton("IcClose", "Fecha os designs · Esc", Hide, 32, 15);
        DockPanel.SetDock(close, Dock.Right);
        close.VerticalAlignment = VerticalAlignment.Top;
        head.Children.Add(close);
        var titles = new StackPanel();
        var title = new TextBlock { Text = "Designs", FontSize = 20, FontWeight = FontWeights.Bold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        titles.Children.Add(title);
        var sub = UiKit.Label("Mapas mentais prontos. Escolha um e importe como quadro novo: os textos são exemplos para trocar.", 13.5, weight: FontWeights.Medium);
        sub.SetResourceReference(TextBlock.ForegroundProperty, "Ink2Brush");
        sub.Margin = new Thickness(0, 3, 0, 0);
        titles.Children.Add(sub);
        head.Children.Add(titles);
        stack.Children.Add(head);

        for (int i = 0; i < DesignLibrary.All.Length; i++) _grid.Children.Add(Card(DesignLibrary.All[i], i));
        stack.Children.Add(_grid);

        // Rodapé: o escolhido e o botão de importar
        var foot = new DockPanel { Margin = new Thickness(8, 14, 0, 2) };
        var import = new Button
        {
            Style = (Style)Application.Current.FindResource("SoftButton"), Height = 42, Padding = new Thickness(20, 0, 20, 0),
            Foreground = Brushes.White, FontSize = 14.5, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Arrow,
            ToolTip = "Cria um quadro novo com o design escolhido e abre · Enter",
            Content = UiKit.Row(new Icon { Data = UiKit.G("IcPlus"), Size = 16, Foreground = Brushes.White, Margin = new Thickness(0, 0, 8, 0) },
                new TextBlock { Text = "Importar design como novo quadro", VerticalAlignment = VerticalAlignment.Center }),
        };
        import.SetResourceReference(BackgroundProperty, "AccentBrush");
        import.SetResourceReference(UI.HoverBackgroundProperty, "AccentBrush");
        UI.SetRadius(import, new CornerRadius(21));
        Press.SetIsEnabled(import, true);
        import.Click += (_, _) => Import();
        DockPanel.SetDock(import, Dock.Right);
        foot.Children.Add(import);
        _chosen.VerticalAlignment = VerticalAlignment.Center;
        _chosen.TextTrimming = TextTrimming.CharacterEllipsis;
        foot.Children.Add(_chosen);
        stack.Children.Add(foot);

        Glass = UiKit.Glass(stack, 30, new Thickness(18, 16, 18, 18));
        Glass.Width = CardWidth * 5 + 36 + 5 * 12;
        Glass.VerticalAlignment = VerticalAlignment.Center;
        Glass.HorizontalAlignment = HorizontalAlignment.Center;
        Children.Add(Glass);
        Select(0);
    }

    public GlassPanel Glass { get; }

    public bool IsOpen => Visibility == Visibility.Visible;

    public void Show()
    {
        Visibility = Visibility.Visible;
        Glass.Opacity = 0;
        var scale = new ScaleTransform(0.96, 0.96);
        Glass.RenderTransformOrigin = new Point(0.5, 0.5);
        Glass.RenderTransform = scale;
        Anim.To(Glass, OpacityProperty, 1, 0.2, Anim.Ease);
        Anim.To(scale, ScaleTransform.ScaleXProperty, 1, 0.45, Anim.Panel);
        Anim.To(scale, ScaleTransform.ScaleYProperty, 1, 0.45, Anim.Panel);
        Dispatcher.BeginInvoke(() => { Focus(); Glass.RefreshBackdrop(); }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void Hide() => Visibility = Visibility.Collapsed;

    void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Hide(); break;
            case Key.Enter: Import(); break;
            case Key.Right: Select((_pick + 1) % _cards.Count); break;
            case Key.Left: Select((_pick + _cards.Count - 1) % _cards.Count); break;
            case Key.Down: Select(Math.Min(_cards.Count - 1, _pick + 5)); break;
            case Key.Up: Select(Math.Max(0, _pick - 5)); break;
            default: return;
        }
        e.Handled = true;
    }

    void Select(int i)
    {
        _pick = i;
        for (int k = 0; k < _cards.Count; k++)
        {
            if (k == i) _cards[k].SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            else _cards[k].BorderBrush = Brushes.Transparent;
            if (k == i) _cards[k].SetResourceReference(Border.BackgroundProperty, "FillBrush");
            else _cards[k].Background = Brushes.Transparent;
        }
        _chosen.Text = "Escolhido: " + DesignLibrary.All[i].Name;
    }

    void Import()
    {
        var d = DesignLibrary.All[_pick];
        Hide();
        try
        {
            var path = StorageService.CreateBoard("", d.Name, d.Build());
            App.OpenBoard(path);
        }
        catch (Exception ex) { App.Log(ex); }
    }

    Border Card(Design d, int index)
    {
        var stack = new StackPanel();
        var preview = new Border
        {
            Height = PreviewHeight, CornerRadius = new CornerRadius(14), ClipToBounds = true,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0x3C, 0x42, 0x5A)), BorderThickness = new Thickness(1),
            Child = new Image { Source = Preview(d.Build()), Stretch = Stretch.Uniform },
        };
        preview.Background = SurfaceOf(d.Build().Modelo);
        stack.Children.Add(preview);
        var name = new TextBlock { Text = d.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 9, 0, 2), TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        stack.Children.Add(name);
        var desc = new TextBlock { Text = d.Description, FontSize = 12, TextWrapping = TextWrapping.Wrap, Height = 48, Margin = new Thickness(2, 0, 0, 0), TextTrimming = TextTrimming.WordEllipsis };
        desc.SetResourceReference(TextBlock.ForegroundProperty, "Ink3Brush");
        stack.Children.Add(desc);

        var card = new Border
        {
            Width = CardWidth + 12, Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(20),
            BorderThickness = new Thickness(2.5), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent, Child = stack,
            ToolTip = d.Description + " · 2 cliques importam",
        };
        card.MouseLeftButtonDown += (_, e) =>
        {
            Select(index);
            if (e.ClickCount == 2) Import();
            e.Handled = true;
        };
        _cards.Add(card);
        return card;
    }

    // ---------- miniatura: desenha o quadro do design em pequeno ----------

    static Brush SurfaceOf(string model) => new SolidColorBrush(model switch
    {
        "escuro" => Color.FromRgb(0x0F, 0x10, 0x13),
        "lousa" => Color.FromRgb(0x2C, 0x48, 0x38),
        "cortica" => Color.FromRgb(0xC4, 0x9A, 0x6C),
        "branco" => Color.FromRgb(0xF9, 0xFA, 0xFB),
        "pontilhado" => Color.FromRgb(0xFB, 0xFB, 0xF8),
        _ => Color.FromRgb(0xFF, 0xFF, 0xFF),
    });

    static Color Hex(string? hex, Color fallback)
    {
        if (string.IsNullOrEmpty(hex)) return fallback;
        try { return (Color)ColorConverter.ConvertFromString(hex); } catch { return fallback; }
    }

    static Rect BoxOf(BoardItem it)
    {
        var def = BoardItemViewModel.DefaultSize(BoardItem.KindFromJson(it.Tipo));
        if (it.Tipo == "texto")
        {
            double size = it.Estilo?.Tamanho ?? 50;
            return new Rect(it.X, it.Y, it.Texto.Length * size * 0.5 + 10, size * 1.2);
        }
        return new Rect(it.X, it.Y, it.Largura ?? def.Width, it.Altura ?? def.Height);
    }

    /// <summary>Desenho simplificado do quadro: formas, post-its, blocos, linhas e os títulos como faixas.</summary>
    static ImageSource Preview(Board board)
    {
        bool dark = board.Modelo is "escuro" or "lousa";
        var items = board.Itens.OrderBy(i => i.Z).ToList();
        var bounds = Rect.Empty;
        foreach (var it in items)
            if (it.Linha is { } l) { bounds.Union(new Point(l.De.X, l.De.Y)); bounds.Union(new Point(l.Para.X, l.Para.Y)); }
            else bounds.Union(BoxOf(it));
        bounds.Inflate(bounds.Width * 0.06, bounds.Height * 0.08);

        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, bounds); // fixa o enquadramento
            double unit = Math.Max(bounds.Width / CardWidth, bounds.Height / PreviewHeight); // 1 px da miniatura em unidades do quadro
            foreach (var it in items)
            {
                var r = BoxOf(it);
                switch (it.Tipo)
                {
                    case "linha" when it.Linha is { } l:
                        var lc = Hex(it.Estilo?.Borda, Color.FromRgb(0x8A, 0x8F, 0x98));
                        var pen = new Pen(new SolidColorBrush(lc), Math.Max(it.Estilo?.Espessura ?? 2.5, unit * 1.2));
                        if (l.Traco == "barbante") pen.DashStyle = new DashStyle([2, 1], 0);
                        dc.DrawLine(pen, new Point(l.De.X, l.De.Y), new Point(l.Para.X, l.Para.Y));
                        break;
                    case "forma":
                        var geo = ShapeLibrary.Get(it.Forma ?? "retangulo").Geometry.Clone();
                        geo.Transform = new MatrixTransform(r.Width / 100, 0, 0, r.Height / 100, r.X, r.Y);
                        var fill = Hex(it.Estilo?.Preenchimento, Colors.Transparent);
                        var stroke = Hex(it.Estilo?.Borda, Colors.Transparent);
                        dc.DrawGeometry(new SolidColorBrush(fill), stroke.A == 0 ? null : new Pen(new SolidColorBrush(stroke), Math.Max(it.Estilo?.Espessura ?? 2, unit * 0.8)), geo);
                        if (!string.IsNullOrWhiteSpace(it.Texto))
                        {
                            // o texto da forma vira uma faixa fina no meio
                            var ink = Hex(it.Estilo?.CorTexto, Color.FromRgb(0x1D, 0x21, 0x30));
                            double w = Math.Min(r.Width * 0.6, it.Texto.Split('\n').Max(s => s.Length) * (it.Estilo?.Tamanho ?? 16) * 0.55);
                            double h = Math.Max(unit * 2, (it.Estilo?.Tamanho ?? 16) * 0.45);
                            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xB0, ink.R, ink.G, ink.B)), null,
                                new Rect(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h), h / 2, h / 2);
                        }
                        break;
                    case "texto":
                        var c = Palette.Get(it.Cor);
                        var tc = it.Cor == "branco" ? (dark ? Colors.White : Color.FromRgb(0x1F, 0x22, 0x29)) : dark ? c.Paper : c.Ink;
                        double th = r.Height * 0.55;
                        dc.DrawRoundedRectangle(new SolidColorBrush(tc), null, new Rect(r.X, r.Y + r.Height * 0.2, r.Width, th), th / 2, th / 2);
                        break;
                    default:
                        var paper = Palette.Get(it.Cor).Paper;
                        if (it.Tipo == "bloco") paper = Color.FromArgb(0xE0, paper.R, paper.G, paper.B);
                        double radius = it.Tipo is "bloco" or "caixa" ? 18 : 3;
                        dc.DrawRoundedRectangle(new SolidColorBrush(paper), new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), unit * 0.6), r, radius, radius);
                        break;
                }
            }
        }
        g.Freeze();
        var img = new DrawingImage(g);
        img.Freeze();
        return img;
    }
}
