using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Quadtab.Behaviors;

namespace Quadtab.Controls;

/// <summary>Peças pequenas montadas em código (chips, botões de ícone, títulos) com os estilos do app.</summary>
public static class UiKit
{
    static Style S(string key) => (Style)Application.Current.FindResource(key);
    public static Geometry G(string key) => (Geometry)Application.Current.FindResource(key);

    public static TextBlock Label(string text, double size = 12.5, bool dim = true, FontWeight? weight = null)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        if (dim) t.SetResourceReference(TextBlock.ForegroundProperty, "Ink3Brush");
        return t;
    }

    public static TextBlock Section(string text) =>
        new TextBlock { Text = text, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 12, 0, 7) }.Dim();

    static TextBlock Dim(this TextBlock t)
    {
        t.SetResourceReference(TextBlock.ForegroundProperty, "Ink3Brush");
        return t;
    }

    public static ToggleButton Chip(string text, bool on, Action click, string? icon = null, string? tip = null)
    {
        var b = new ToggleButton { Style = S("Chip"), IsChecked = on, ToolTip = tip ?? ChipTip(text) };
        b.Content = icon is null ? text : Row(new Icon { Data = G(icon), Size = 15 }, text.Length > 0 ? new TextBlock { Text = text, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center } : null);
        b.Click += (_, _) => click();
        return b;
    }

    public static Button IconButton(string icon, string tip, Action click, double size = 36, double iconSize = 17)
    {
        var b = new Button
        {
            Style = S("SoftButton"), Width = size, Height = size, ToolTip = tip, Cursor = System.Windows.Input.Cursors.Arrow,
            Content = new Icon { Data = G(icon), Size = iconSize },
        };
        UI.SetRadius(b, new CornerRadius(size / 2));
        b.SetResourceReference(UI.HoverBackgroundProperty, "FillBrush");
        Press.SetIsEnabled(b, true);
        b.Click += (_, _) => click();
        return b;
    }

    // Balões de ajuda dos chips e pílulas que não recebem um texto próprio.
    static readonly Dictionary<string, string> Tips = new()
    {
        ["Alfinete"] = "Prende com um alfinete",
        ["Fita"] = "Prende com fita adesiva",
        ["Solto"] = "Deixa solto, sem nada prendendo",
        ["Pautado"] = "Vira um post-it pautado, para listas",
        ["Todas"] = "Mostra todas as formas",
        ["Básicas"] = "Mostra as formas básicas",
        ["Setas"] = "Mostra as setas",
        ["Símbolos"] = "Mostra os símbolos",
        ["Objetos"] = "Mostra os objetos",
        ["Natureza"] = "Mostra as formas da natureza",
        ["Inter"] = "Usa a letra Inter, reta e moderna",
        ["Nunito"] = "Usa a letra Nunito, arredondada",
        ["Reta"] = "Deixa a linha reta",
        ["Curva"] = "Deixa a linha curva",
        ["Ângulo"] = "Deixa a linha em ângulo reto",
        ["Nada"] = "Ponta sem nada",
        ["Seta"] = "Ponta com seta",
        ["Bolinha"] = "Ponta com bolinha",
        ["Com seta"] = "Coloca uma seta no fim da linha",
        ["Fechar bandeja"] = "Fecha a bandeja de post-its",
        ["Abrir bandeja"] = "Abre a bandeja de post-its",
        ["Mostrar título"] = "Mostra o título grande do quadro",
        ["Ocultar título"] = "Esconde o título grande e aumenta o quadro",
        ["Pronto"] = "Termina de desenhar e volta à seleção · Esc",
        ["Agrupar"] = "Junta os itens num grupo · Ctrl+G",
        ["Desagrupar"] = "Separa o grupo em itens soltos · Ctrl+Shift+G",
        ["Copiar estilo"] = "Guarda a cor e o estilo deste item",
        ["Colar estilo"] = "Aplica o estilo copiado nos selecionados",
        ["Ler"] = "Mostra a nota formatada · Esc",
        ["Editar"] = "Edita o texto da nota",
    };

    static string? ChipTip(string text) => Tips.GetValueOrDefault(text);
    static string? PillTip(string text) => Tips.GetValueOrDefault(text);

    public static Button Pill(string text, Action click, string? icon = null)
    {
        var b = new Button { Style = S("PillButton"), ToolTip = PillTip(text) };
        b.SetResourceReference(Control.BackgroundProperty, "FillBrush");
        b.Content = icon is null ? text : Row(new Icon { Data = G(icon), Size = 15 }, new TextBlock { Text = text, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        b.Click += (_, _) => click();
        return b;
    }

    public static StackPanel Row(params UIElement?[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in children) if (c is not null) p.Children.Add(c);
        return p;
    }

    public static WrapPanel Wrap(params UIElement[] children)
    {
        var p = new WrapPanel();
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    /// <summary>Bolinha de cor (24 px). on = anel de destaque.</summary>
    public static Button Swatch(Brush fill, bool on, string tip, Action click, double size = 24)
    {
        var b = new Button { Style = S("ColorDot"), Background = fill, ToolTip = tip.Contains(' ') ? tip : $"Muda a cor para {tip.ToLowerInvariant()}", Width = size, Height = size, Tag = on ? "on" : null };
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>Controle deslizante com rótulo e valor.</summary>
    public static FrameworkElement Slider(string label, double min, double max, double value, Action<double> changed, string format = "0", double step = 1)
    {
        var dock = new DockPanel { Margin = new Thickness(4, 2, 12, 6) };
        var val = new TextBlock { Text = value.ToString(format), Width = 40, TextAlignment = TextAlignment.Right, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        val.SetResourceReference(TextBlock.ForegroundProperty, "Ink2Brush");
        DockPanel.SetDock(val, Dock.Right);
        var name = Label(label, 13, dim: false, FontWeights.Medium);
        name.Width = 74;
        DockPanel.SetDock(name, Dock.Left);
        var s = new System.Windows.Controls.Slider
        {
            Minimum = min, Maximum = max, Value = value, SmallChange = step, LargeChange = step * 5,
            IsSnapToTickEnabled = step > 0, TickFrequency = step, VerticalAlignment = VerticalAlignment.Center,
        };
        s.ValueChanged += (_, e) =>
        {
            val.Text = e.NewValue.ToString(format);
            changed(e.NewValue);
        };
        dock.Children.Add(val);
        dock.Children.Add(name);
        dock.Children.Add(s);
        return dock;
    }

    public static Border Separator()
    {
        var b = new Border { Height = 1, Margin = new Thickness(4, 10, 4, 2) };
        b.SetResourceReference(Border.BackgroundProperty, "SepBrush");
        return b;
    }

    /// <summary>Painel de vidro denso (Glass2) com o conteúdo.</summary>
    public static GlassPanel Glass(UIElement child, double radius = 24, Thickness? padding = null)
    {
        var g = new GlassPanel { CornerRadius = radius, Padding = padding ?? new Thickness(10), Child = child };
        g.SetResourceReference(GlassPanel.BackgroundProperty, "Glass2Brush");
        return g;
    }
}
