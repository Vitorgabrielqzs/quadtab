using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Quadtab.Controls;

/// <summary>Uma linha de menu: texto, ícone à direita e ação. Separator = faixa antes da linha.</summary>
public sealed record ActionItem(string Label, string? Icon, Action Run, bool Danger = false, bool Separator = false, bool Checked = false)
{
    /// <summary>Cabeçalho (12.5/600/Ink3), sem ação.</summary>
    public static ActionItem Header(string text, bool separator = false) => new(text, null, () => { }, Separator: separator) { IsHeader = true };
    public bool IsHeader { get; init; }

    /// <summary>Balão de ajuda da linha (o que ela faz).</summary>
    public string? Tip { get; init; }

    /// <summary>Roda ao passar o mouse na linha (prévia, ex.: a caligrafia muda no item enquanto o mouse está em cima).</summary>
    public Action? Preview { get; init; }

    /// <summary>Letra do texto da linha (ex.: cada caligrafia escrita nela mesma).</summary>
    public FontFamily? LabelFont { get; init; }
}

/// <summary>
/// Menu de vidro genérico (mesmo visual do menu do botão direito): usado pelo "…" da barra,
/// pelos quadros e pastas da lateral e pelas opções extras dos itens.
/// </summary>
public sealed class ActionMenu : ContentControl
{
    const double ColumnWidth = 284;
    readonly StackPanel _list = new() { Width = ColumnWidth };
    readonly StackPanel _second = new() { Width = ColumnWidth, Margin = new Thickness(8, 0, 0, 0) };
    readonly List<UIElement> _rows = [];

    public event Action? Closed;

    /// <summary>Uma coluna quando cabe na altura disponível; senão, duas, para aparecer tudo sem rolar.</summary>
    public void FitTo(double available)
    {
        _list.Children.Clear();
        _second.Children.Clear();
        foreach (var r in _rows) _list.Children.Add(r);
        _list.Measure(new Size(ColumnWidth, double.PositiveInfinity));
        double total = _list.DesiredSize.Height;
        bool two = total + 12 > available;
        Glass.Width = two ? ColumnWidth * 2 + 8 + 12 : ColumnWidth + 12;
        _second.Visibility = two ? Visibility.Visible : Visibility.Collapsed;
        if (!two) return;
        _list.Children.Clear();
        double used = 0;
        foreach (var r in _rows)
        {
            r.Measure(new Size(ColumnWidth, double.PositiveInfinity));
            (used < total / 2 ? _list : _second).Children.Add(r);
            used += r.DesiredSize.Height;
        }
    }

    public ActionMenu()
    {
        var glass = new GlassPanel { CornerRadius = 24, Padding = new Thickness(6), Width = ColumnWidth + 12 };
        glass.SetResourceReference(GlassPanel.BackgroundProperty, "Glass2Brush");
        var columns = new StackPanel { Orientation = Orientation.Horizontal };
        columns.Children.Add(_list);
        columns.Children.Add(_second);
        // Mais alto que a janela: rola em vez de ficar cortado.
        glass.Child = new ScrollViewer
        {
            Content = columns, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false, Style = (Style)Application.Current.FindResource("OverlayScroll"),
        };
        Content = glass;
        Glass = glass;
    }

    public GlassPanel Glass { get; }

    public void SetItems(IEnumerable<ActionItem> items)
    {
        _list.Children.Clear();
        _second.Children.Clear();
        _rows.Clear();
        foreach (var it in items)
        {
            if (it.Separator)
            {
                var sep = new Border { Height = 7, Margin = new Thickness(-6, 5, -6, 5) };
                sep.SetResourceReference(Border.BackgroundProperty, "SepBrush");
                _rows.Add(sep);
            }
            if (it.IsHeader)
            {
                var h = new TextBlock { Text = it.Label, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Padding = new Thickness(12, 9, 12, 7) };
                h.SetResourceReference(TextBlock.ForegroundProperty, "Ink3Brush");
                _rows.Add(h);
                continue;
            }
            var row = new Button { Style = (Style)Application.Current.FindResource(it.Danger ? "MenuRowDanger" : "MenuRow"), ToolTip = it.Tip ?? ActionTips.For(it) };
            var dock = new DockPanel();
            if (it.Icon is not null)
            {
                var icon = new Icon { Data = (Geometry)Application.Current.FindResource(it.Icon) };
                if (!it.Danger) icon.SetResourceReference(Icon.ForegroundProperty, "Ink2Brush");
                DockPanel.SetDock(icon, Dock.Right);
                dock.Children.Add(icon);
            }
            if (it.Checked)
            {
                var check = new Icon { Data = (Geometry)Application.Current.FindResource("IcCheckSmall"), Size = 17, Margin = new Thickness(0, 0, 8, 0) };
                check.SetResourceReference(Icon.ForegroundProperty, "AccentBrush");
                DockPanel.SetDock(check, Dock.Right);
                dock.Children.Add(check);
            }
            var label = new TextBlock { Text = it.Label, VerticalAlignment = VerticalAlignment.Center };
            if (it.LabelFont is { } lf) { label.FontFamily = lf; label.FontSize = 19; }
            dock.Children.Add(label);
            row.Content = dock;
            if (it.Preview is { } preview) row.MouseEnter += (_, _) => preview();
            var run = it.Run;
            row.Click += (_, _) =>
            {
                Closed?.Invoke();
                run();
            };
            _rows.Add(row);
        }
    }
}

/// <summary>Texto do balão de ajuda das linhas de menu que não trazem o seu (frase simples + atalho).</summary>
public static class ActionTips
{
    static readonly Dictionary<string, string> Tips = new()
    {
        ["Para o topo"] = "Coloca na frente de todos os itens · Ctrl+Shift+]",
        ["Para o fundo"] = "Coloca atrás de todos os itens · Ctrl+Shift+[",
        ["Uma camada para frente"] = "Sobe uma camada · Ctrl+]",
        ["Uma camada para trás"] = "Desce uma camada · Ctrl+[",
        ["Copiar estilo"] = "Guarda a cor e o estilo deste item para colar em outros",
        ["Colar estilo"] = "Aplica o estilo copiado nos itens selecionados",
        ["Descrição…"] = "Abre o inspetor para escrever uma descrição que aparece ao passar o mouse",
        ["Agrupar (Ctrl+G)"] = "Junta os itens selecionados num grupo · Ctrl+G",
        ["Desagrupar (Ctrl+Shift+G)"] = "Separa o grupo em itens soltos · Ctrl+Shift+G",
        ["Alinhar à esquerda"] = "Alinha os itens pela borda esquerda",
        ["Centralizar"] = "Alinha os itens pelo centro, na horizontal",
        ["Alinhar no topo"] = "Alinha os itens pela borda de cima",
        ["Centralizar na vertical"] = "Alinha os itens pelo meio, na vertical",
        ["Distribuir na horizontal"] = "Deixa o mesmo espaço entre os itens, da esquerda para a direita",
        ["Distribuir na vertical"] = "Deixa o mesmo espaço entre os itens, de cima para baixo",
        ["Desfazer (Ctrl+Z)"] = "Desfaz a última mudança · Ctrl+Z",
        ["Refazer (Ctrl+Shift+Z)"] = "Refaz o que foi desfeito · Ctrl+Shift+Z",
        ["Procurar (Ctrl+K)"] = "Procura em quadros, notas e textos · Ctrl+K",
        ["Buscar no quadro (Ctrl+F)"] = "Destaca os itens deste quadro que têm o texto buscado · Ctrl+F",
        ["Ajustar o quadro à tela"] = "Enquadra todos os itens na tela · Ctrl+1",
        ["Renomear quadro"] = "Muda o nome deste quadro",
        ["Mostrar grade"] = "Mostra ou esconde a grade de fundo",
        ["Modo diagrama"] = "Mostra ou esconde as linhas que ligam os itens",
        ["Inspetor"] = "Mostra ou esconde o painel de propriedades do item",
        ["Mostrar título"] = "Mostra o título grande do quadro",
        ["Ocultar título"] = "Esconde o título grande e aumenta o quadro",
        ["Cenas e apresentação…"] = "Salva vistas do quadro e navega entre elas",
        ["Apresentar"] = "Apresenta as cenas em tela cheia, com as setas ← →",
        ["Apresentar (setas ← →)"] = "Apresenta as cenas em tela cheia, com as setas ← →",
        ["Exportar quadro em PDF…"] = "Salva o quadro inteiro em PDF, com a qualidade máxima",
        ["Exportar seleção em PDF…"] = "Salva só os itens selecionados em PDF, com a qualidade máxima",
        ["Abrir a pasta do cofre"] = "Abre no Explorador a pasta onde tudo fica guardado",
        ["Salvar a vista atual como cena"] = "Guarda o que está na tela agora como uma cena",
        ["Apagar a última cena"] = "Apaga a cena salva por último",
    };

    public static string? For(ActionItem it) =>
        Tips.TryGetValue(it.Label, out var t) ? t : it.Icon == "IcTarget" ? "Leva a câmera até esta cena" : null;
}
