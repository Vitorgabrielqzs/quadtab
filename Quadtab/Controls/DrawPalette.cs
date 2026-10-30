using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Quadtab.Rendering;

namespace Quadtab.Controls;

/// <summary>Paleta flutuante de desenho (caneta, lápis, marcador, borracha, laço) e de linhas.</summary>
public sealed class DrawPalette : ContentControl
{
    static readonly Color[] Inks =
    [
        Color.FromRgb(0x1D, 0x21, 0x30), Color.FromRgb(0xFF, 0x45, 0x3A), Color.FromRgb(0xFF, 0x9F, 0x0A),
        Color.FromRgb(0x30, 0xD1, 0x58), Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0xBF, 0x5A, 0xF2), Colors.White,
    ];

    readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    BoardView? _board;

    public DrawPalette()
    {
        Glass = UiKit.Glass(_row, 28, new Thickness(8, 6, 8, 6));
        Glass.Height = 56;
        Content = Glass;
        Visibility = Visibility.Collapsed;
    }

    public GlassPanel Glass { get; }

    public BoardView? Board
    {
        get => _board;
        set
        {
            _board = value;
            if (value is not null) value.ToolChanged += Rebuild;
        }
    }

    void Set(BoardTool t)
    {
        _board!.Tool = t;
    }

    void Rebuild()
    {
        if (_board is null) return;
        var tool = _board.Tool;
        Visibility = tool == BoardTool.Select ? Visibility.Collapsed : Visibility.Visible;
        _row.Children.Clear();
        if (tool == BoardTool.Select) return;

        if (tool == BoardTool.Line)
        {
            _row.Children.Add(Text("Linha:"));
            foreach (var (key, name) in new[] { ("reta", "Reta"), ("curva", "Curva"), ("angulo", "Ângulo") })
                _row.Children.Add(Chip(name, DrawSettings.LineType == key, () => { DrawSettings.LineType = key; Rebuild(); }));
            _row.Children.Add(Chip("Com seta", DrawSettings.ArrowEnd, () => { DrawSettings.ArrowEnd = !DrawSettings.ArrowEnd; Rebuild(); }));
            _row.Children.Add(Text("Arraste no quadro (de um item a outro para ligar)"));
        }
        else
        {
            foreach (var (t, icon, tip) in new[]
            {
                (BoardTool.Pen, "IcPen", "Desenha com caneta"), (BoardTool.Pencil, "IcPencil", "Desenha com lápis, traço mais fino"), (BoardTool.Marker, "IcMarker", "Pinta com marca-texto transparente"),
                (BoardTool.Eraser, "IcEraser", "Apaga os traços em que passar"), (BoardTool.Lasso, "IcLasso", "Seleciona itens desenhando um laço em volta"),
            })
            {
                var b = UiKit.Chip("", tool == t, () => Set(t), icon, tip);
                b.Height = 40;
                b.Width = 40;
                b.Padding = new Thickness(0);
                b.Margin = new Thickness(0, 0, 4, 0);
                Behaviors.UI.SetRadius(b, new CornerRadius(20));
                _row.Children.Add(b);
            }
            _row.Children.Add(Sep());
            foreach (var c in Inks)
            {
                var color = c;
                var dot = UiKit.Swatch(ShadowCache.BrushOf(c), DrawSettings.Color == c, "Desenha com esta cor", () => { DrawSettings.Color = color; Rebuild(); }, 24);
                dot.VerticalAlignment = VerticalAlignment.Center;
                _row.Children.Add(dot);
            }
            _row.Children.Add(Sep());
            foreach (var w in new[] { 2.0, 4, 8 })
            {
                double width = w;
                var b = UiKit.Chip("", DrawSettings.Thickness == w, () => { DrawSettings.Thickness = width; Rebuild(); }, null, w switch { 2 => "Traço fino", 4 => "Traço médio", _ => "Traço grosso" });
                b.Content = new Border { Width = 18, Height = w + 1, CornerRadius = new CornerRadius(4), Background = ShadowCache.BrushOf(DrawSettings.Color == Colors.White ? Colors.Gray : DrawSettings.Color) };
                b.Width = 40;
                b.Height = 40;
                b.Padding = new Thickness(0);
                b.Margin = new Thickness(0, 0, 4, 0);
                Behaviors.UI.SetRadius(b, new CornerRadius(20));
                _row.Children.Add(b);
            }
        }
        _row.Children.Add(Sep());
        var done = UiKit.Pill("Pronto", () => Set(BoardTool.Select), "IcCheckSmall");
        done.Height = 36;
        Behaviors.UI.SetRadius(done, new CornerRadius(18));
        done.VerticalAlignment = VerticalAlignment.Center;
        _row.Children.Add(done);
    }

    static System.Windows.Controls.Primitives.ToggleButton Chip(string text, bool on, Action click)
    {
        var c = UiKit.Chip(text, on, click);
        c.Margin = new Thickness(0, 0, 6, 0);
        c.VerticalAlignment = VerticalAlignment.Center;
        return c;
    }

    static TextBlock Text(string s)
    {
        var t = UiKit.Label(s, 13);
        t.Margin = new Thickness(8, 0, 8, 0);
        return t;
    }

    static Border Sep()
    {
        var b = new Border { Width = 1, Height = 24, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BackgroundProperty, "SepBrush");
        return b;
    }
}
