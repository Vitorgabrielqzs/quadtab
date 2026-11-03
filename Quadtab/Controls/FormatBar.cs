using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Quadtab.Behaviors;

namespace Quadtab.Controls;

/// <summary>
/// Barra de formatação (estilo Notion) que aparece em cima do texto selecionado num campo de edição:
/// cor do texto e do fundo, negrito, itálico, sublinhado, riscado, código e limpar formatação.
/// Os botões não recebem foco, então o campo de texto continua sendo editado.
/// </summary>
public sealed class FormatBar : ContentControl
{
    // Cores do texto e do fundo (mesma família do Notion).
    static readonly (string Name, string Hex)[] TextColors =
    [
        ("Cinza", "#9B9A97"), ("Marrom", "#64473A"), ("Laranja", "#D9730D"), ("Amarelo", "#DFAB01"), ("Verde", "#0F7B6C"),
        ("Azul", "#0B6E99"), ("Roxo", "#6940A5"), ("Rosa", "#AD1A72"), ("Vermelho", "#E03E3E"),
    ];

    static readonly (string Name, string Hex)[] BackColors =
    [
        ("Cinza", "#E3E2E0"), ("Marrom", "#EEE0DA"), ("Laranja", "#FADEC9"), ("Amarelo", "#FDECC8"), ("Verde", "#DBEDDB"),
        ("Azul", "#D3E5EF"), ("Roxo", "#E8DEEE"), ("Rosa", "#F5E0E9"), ("Vermelho", "#FFE2DD"),
    ];

    const double Cell = 38, CellHeight = 34;

    public static FormatBar? Instance { get; private set; }

    /// <summary>Camada de cima da janela, onde a barra é posicionada.</summary>
    public Canvas? Overlay { get; set; }

    readonly GlassPanel _glass;
    readonly StackPanel _flyout = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
    readonly Border _colorButtonFrame = new();
    RichEditBox? _owner;

    public FormatBar()
    {
        Instance = this;
        Focusable = false;
        IsTabStop = false;
        Visibility = Visibility.Collapsed;

        var grid = new Grid { Width = Cell * 5 };
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        for (int c = 0; c < 5; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());

        Place(grid, ColorButton(), 0, 0);
        Place(grid, Button("Negrito · Ctrl+B", Icon("IcBold"), () => Apply(tb => tb.ToggleBold())), 0, 1);
        Place(grid, Button("Itálico · Ctrl+I", Icon("IcItalic"), () => Apply(tb => tb.ToggleItalic())), 0, 2);
        Place(grid, Button("Sublinhado · Ctrl+U", Icon("M7,4 V11 A5,5 0 0 0 17,11 V4 M5,20 H19"), () => Apply(tb => tb.ToggleUnderline())), 0, 3);
        Place(grid, Button("Tirar a formatação", Icon("M4,6 V4 H15 V6 M9.5,4 V18 M7,18 H12 M15,12 L21,18 M21,12 L15,18"), () => Apply(tb => tb.ClearFormat())), 0, 4);
        Place(grid, Button("Riscado", Icon("M5,12 H19 M16.5,7 C15.5,5.5 13.8,5 12,5 C9.5,5 8,6.2 8,8 C8,9.8 9.5,10.5 12,12 M8,17 C9,18.5 10.5,19 12.5,19 C15,19 16.5,17.8 16.5,16 C16.5,14.5 15.5,13.5 14,12.8"), () => Apply(tb => tb.ToggleStrike())), 1, 0);
        Place(grid, Button("Código", Icon("M9,7 L4,12 L9,17 M15,7 L20,12 L15,17"), () => Apply(tb => tb.ToggleCode())), 1, 1);
        Place(grid, Button("Marca-texto amarelo", Icon("M5,20 H19 M8,15 L15,6 L18,9 L11,17 H8 Z"), () => Apply(tb => tb.SetBack(BackColors[3].Hex))), 1, 2);

        _flyout.Children.Add(Header("Cor do texto"));
        _flyout.Children.Add(Swatches(TextColors, back: false));
        _flyout.Children.Add(Header("Cor de fundo"));
        _flyout.Children.Add(Swatches(BackColors, back: true));

        var stack = new StackPanel();
        stack.Children.Add(grid);
        stack.Children.Add(_flyout);

        _glass = new GlassPanel { CornerRadius = 18, Padding = new Thickness(6), Child = stack, Focusable = false };
        _glass.SetResourceReference(GlassPanel.BackgroundProperty, "MenuGlassBrush");
        Content = _glass;
    }

    static void Place(Grid grid, UIElement el, int row, int col)
    {
        Grid.SetRow(el, row);
        Grid.SetColumn(el, col);
        grid.Children.Add(el);
    }

    static Path Icon(string dataOrKey)
    {
        var geometry = dataOrKey.StartsWith("Ic", StringComparison.Ordinal) && Application.Current.TryFindResource(dataOrKey) is Geometry g
            ? g : Geometry.Parse(dataOrKey);
        var p = new Path
        {
            Data = geometry, Width = 18, Height = 18, Stretch = Stretch.Uniform, StrokeThickness = 1.9,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
        p.SetResourceReference(Shape.StrokeProperty, "InkBrush");
        return p;
    }

    static TextBlock Header(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 4, 0, 4) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Ink3Brush");
        return t;
    }

    /// <summary>Botão que não pega o foco do teclado (o campo continua com o cursor).</summary>
    static Border Button(string tip, UIElement child, Action run)
    {
        var b = new Border { Width = Cell, Height = CellHeight, CornerRadius = new CornerRadius(9), Background = Brushes.Transparent, Child = child, ToolTip = tip, Focusable = false };
        b.MouseEnter += (_, _) => b.SetResourceReference(Border.BackgroundProperty, "Fill2Brush");
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonDown += (_, e) => { e.Handled = true; run(); };
        return b;
    }

    Border ColorButton()
    {
        var letter = new TextBlock { Text = "A", FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        letter.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        _colorButtonFrame.Width = 26;
        _colorButtonFrame.Height = 26;
        _colorButtonFrame.CornerRadius = new CornerRadius(8);
        _colorButtonFrame.BorderThickness = new Thickness(1.5);
        _colorButtonFrame.SetResourceReference(Border.BorderBrushProperty, "Ink3Brush");
        _colorButtonFrame.Child = letter;
        return Button("Cor do texto e do fundo", _colorButtonFrame, ToggleFlyout);
    }

    UIElement Swatches((string Name, string Hex)[] colors, bool back)
    {
        // Cinco por linha em qualquer escala da tela (com largura fixa, a quinta caía para a linha de baixo em 125%).
        var wrap = new System.Windows.Controls.Primitives.UniformGrid { Columns = 5, Width = Cell * 5 };
        wrap.Children.Add(Swatch("Padrão", null, back));
        foreach (var (name, hex) in colors) wrap.Children.Add(Swatch(name, hex, back));
        return wrap;
    }

    Border Swatch(string name, string? hex, bool back)
    {
        FrameworkElement content;
        if (back)
        {
            var dot = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), IsHitTestVisible = false };
            dot.SetResourceReference(Border.BorderBrushProperty, "Ink3Brush");
            if (hex is not null) dot.Background = RichText.BrushOf(hex);
            content = dot;
        }
        else
        {
            var a = new TextBlock { Text = "A", FontSize = 17, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            if (hex is null) a.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
            else a.Foreground = RichText.BrushOf(hex);
            content = a;
        }
        return Button(name, content, () =>
        {
            Apply(tb => { if (back) tb.SetBack(hex); else tb.SetColor(hex); });
            _flyout.Visibility = Visibility.Collapsed;
            Reposition();
        });
    }

    void ToggleFlyout()
    {
        _flyout.Visibility = _flyout.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        Reposition();
    }

    void Apply(Action<RichEditBox> op)
    {
        if (_owner is { HasSelection: true } tb) op(tb);
    }

    // ---------- mostrar e esconder ----------

    public void ShowFor(RichEditBox tb)
    {
        if (Overlay is null) return;
        bool first = Visibility != Visibility.Visible || _owner != tb;
        if (_owner != tb) _flyout.Visibility = Visibility.Collapsed;
        _owner = tb;
        Visibility = Visibility.Visible;
        Reposition();
        if (first) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
    }

    public void HideFor(RichEditBox tb)
    {
        if (_owner == tb) Hide();
    }

    public bool IsShownFor(RichEditBox tb) => _owner == tb && Visibility == Visibility.Visible;

    /// <summary>A seleção mudou com a barra aberta: acompanha.</summary>
    public void Follow() => Reposition();

    public void Hide()
    {
        _owner = null;
        _flyout.Visibility = Visibility.Collapsed;
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Em cima da seleção, centralizada; sem espaço em cima, vai para baixo. Nunca sai da janela.</summary>
    void Reposition()
    {
        if (_owner is not { } tb || Overlay is null) return;
        var (a, b) = tb.SelectionRects();
        if (a.IsEmpty || b.IsEmpty)
        {
            tb.UpdateLayout();
            (a, b) = tb.SelectionRects();
            if (a.IsEmpty || b.IsEmpty) { Hide(); return; }
        }

        double cx = Math.Abs(a.Top - b.Top) < 3 ? (a.Left + b.Right) / 2 : a.Left + 24;
        var map = tb.TransformToVisual(Overlay);
        var top = map.Transform(new Point(cx, a.Top));
        var bottom = map.Transform(new Point(cx, b.Bottom));

        UpdateLayout(); // o menu de cores abriu ou fechou: mede de novo antes de ler o tamanho
        var size = DesiredSize;
        double x = Math.Clamp(top.X - size.Width / 2, 8, Math.Max(8, Overlay.ActualWidth - size.Width - 8));
        double y = top.Y - size.Height - 10;
        if (y < 8) y = Math.Min(bottom.Y + 10, Math.Max(8, Overlay.ActualHeight - size.Height - 8));
        Canvas.SetLeft(this, x);
        Canvas.SetTop(this, y);
    }
}
