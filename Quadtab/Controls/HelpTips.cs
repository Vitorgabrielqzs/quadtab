using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Quadtab.Rendering;

namespace Quadtab.Controls;

/// <summary>
/// Balão de ajuda: com o mouse parado 3 s em cima de um botão, aparece uma frase dizendo para que ele
/// serve (o texto vem do ToolTip do botão; o atalho, depois de " · ", fica apagado). Vidro, Inter 13,
/// ao lado do botão e sempre dentro da janela. Some ao sair do botão ou ao clicar.
/// As descrições dos itens do quadro usam o mesmo balão, mais rápido (0,8 s).
/// </summary>
public static partial class HelpTips
{
    [GeneratedRegex(@"^(Ctrl|Shift|Alt|F\d+|Esc|Enter|Delete|Tab|Espaço|Setas|←|→|↑|↓|2 cliques|Botão)")]
    private static partial Regex Shortcut();

    static Canvas? _layer;
    static GlassPanel? _balloon;
    static TextBlock? _text;
    static readonly ScaleTransform Scale = new(0.94, 0.94);
    static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromSeconds(3) };
    static FrameworkElement? _target;
    static Point _rest;

    /// <summary>Liga os balões na janela. layer = camada de cima (Canvas) onde o balão é desenhado.</summary>
    public static void Attach(Window window, Canvas layer)
    {
        _layer = layer;
        _text = new TextBlock { FontSize = 13, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
        _text.SetResourceReference(TextBlock.ForegroundProperty, "InkBrush");
        _text.FontFamily = (FontFamily)Application.Current.FindResource("Inter");
        _balloon = UiKit.Glass(_text, 12, new Thickness(11, 7, 11, 8));
        _balloon.IsHitTestVisible = false;
        _balloon.Visibility = Visibility.Collapsed;
        _balloon.RenderTransform = Scale;
        layer.Children.Add(_balloon);

        // O ToolTip padrão do Windows não aparece: o texto é usado só pelo balão.
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.ToolTipOpeningEvent,
            new ToolTipEventHandler((_, e) => e.Handled = true));

        Timer.Tick += (_, _) => { Timer.Stop(); Show(); };
        window.PreviewMouseMove += (_, e) => OnMove(e);
        window.PreviewMouseDown += (_, _) => { Hide(); _target = null; Timer.Stop(); };
        window.PreviewMouseWheel += (_, _) => Hide();
        window.MouseLeave += (_, _) => { Hide(); _target = null; Timer.Stop(); };
        window.Deactivated += (_, _) => { Hide(); Timer.Stop(); };
    }

    static void OnMove(MouseEventArgs e)
    {
        var target = Find(e.OriginalSource as DependencyObject);
        var p = e.GetPosition(_layer);
        if (target != _target)
        {
            Hide();
            _target = target;
            _rest = p;
            Timer.Stop();
            if (target is null) return;
            Timer.Interval = TimeSpan.FromSeconds(target is BoardItemContainer ? 0.8 : 3);
            Timer.Start();
            return;
        }
        // "Parado": um movimento maior que 4 px recomeça a contagem (o balão já aberto continua).
        if (target is not null && _balloon!.Visibility != Visibility.Visible && (p - _rest).Length > 4)
        {
            _rest = p;
            Timer.Stop();
            Timer.Start();
        }
    }

    /// <summary>O botão (ou elemento com texto de ajuda) mais próximo debaixo do mouse.</summary>
    static FrameworkElement? Find(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is FrameworkElement fe && fe.ToolTip is string { Length: > 0 }) return fe;
            if (d is ButtonBase) return null; // botão sem texto: nada a mostrar
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    static void Show(bool force = false)
    {
        if (_target is not { ToolTip: string tip } || (!force && !_target.IsMouseOver) || _layer is null || _balloon is null || _text is null) return;
        _text.Inlines.Clear();
        int cut = tip.LastIndexOf(" · ", StringComparison.Ordinal);
        if (cut > 0 && Shortcut().IsMatch(tip[(cut + 3)..]))
        {
            _text.Inlines.Add(new Run(tip[..cut]));
            var key = new Run(" · " + tip[(cut + 3)..]);
            key.SetResourceReference(TextElement.ForegroundProperty, "Ink3Brush");
            _text.Inlines.Add(key);
        }
        else _text.Inlines.Add(new Run(tip));

        _balloon.Visibility = Visibility.Visible;
        _balloon.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _balloon.DesiredSize;
        var r = new Rect(_target.TranslatePoint(default, _layer), _target.RenderSize);
        double w = _layer.ActualWidth, h = _layer.ActualHeight;
        Point at;
        // Barra de cima: abaixo do botão. No resto: ao lado (direita, ou esquerda se não couber).
        if (r.Top < 110) at = new Point(r.Left + r.Width / 2 - size.Width / 2, r.Bottom + 8);
        else if (r.Right + 10 + size.Width <= w - 8) at = new Point(r.Right + 10, r.Top + r.Height / 2 - size.Height / 2);
        else at = new Point(r.Left - 10 - size.Width, r.Top + r.Height / 2 - size.Height / 2);
        at.X = Math.Clamp(at.X, 8, Math.Max(8, w - size.Width - 8));
        at.Y = Math.Clamp(at.Y, 8, Math.Max(8, h - size.Height - 8));
        Canvas.SetLeft(_balloon, at.X);
        Canvas.SetTop(_balloon, at.Y);
        _balloon.RenderTransformOrigin = new Point(0.5, 0.5);
        Anim.Set(_balloon, UIElement.OpacityProperty, 0.0);
        Anim.Set(Scale, ScaleTransform.ScaleXProperty, 0.94);
        Anim.Set(Scale, ScaleTransform.ScaleYProperty, 0.94);
        Anim.To(_balloon, UIElement.OpacityProperty, 1, 0.18, Anim.Ease);
        Anim.To(Scale, ScaleTransform.ScaleXProperty, 1, 0.4, Anim.Pop);
        Anim.To(Scale, ScaleTransform.ScaleYProperty, 1, 0.4, Anim.Pop);
    }

    public static void Hide()
    {
        if (_balloon is { Visibility: Visibility.Visible }) _balloon.Visibility = Visibility.Collapsed;
    }
}
