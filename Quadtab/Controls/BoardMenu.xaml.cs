using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Quadtab.Models;
using Quadtab.Rendering;
using Quadtab.Services;
using Quadtab.ViewModels;

namespace Quadtab.Controls;

/// <summary>Aqui usamos a classe BoardMenu para o menu próprio do botão direito num item (estilo menu de toque longo do iPhone).</summary>
public partial class BoardMenu : UserControl
{
    BoardItemViewModel? _item;
    readonly List<Button> _itemDots = [];
    readonly List<Button> _lineDots = [];
    readonly List<Button> _recentDots = [];
    const int RecentCount = 8;

    /// <summary>O menu terminou uma ação e deve fechar.</summary>
    public event Action? CloseRequested;

    public GlassPanel GlassPanel => Glass;

    MainViewModel Vm => App.ViewModel;

    public BoardMenu()
    {
        InitializeComponent();
        // Paleta: as cores fortes primeiro; 8 por linha.
        foreach (var c in Palette.Colors.OrderByDescending(c => c.IsStrong))
        {
            var d = ItemDot(c);
            _itemDots.Add(d);
            ItemDots.Children.Add(d);
        }
        // Linhas: cores próprias, mais vivas (inclui juta, a cor natural do barbante).
        foreach (var (name, hex) in BoardItemViewModel.LineColors)
        {
            var d = LineDot(name, hex);
            _lineDots.Add(d);
            LineDots.Children.Add(d);
        }
    }

    // ---------- bolinhas de cor ----------

    // Bolinha de cor de item: aplica a cor e guarda nas últimas usadas.
    Button ItemDot(NoteColor c)
    {
        var key = c.Key;
        var d = Dot(c.PaperBrush, $"Muda a cor para {c.Name.ToLowerInvariant()}", () =>
        {
            if (_item is null) return;
            Vm.SetColor(_item, key);
            Remember(StorageService.State.CoresRecentes, key);
            Refresh();
        });
        d.DataContext = key;
        d.Margin = new Thickness(0, 0, 4.5, 6);
        return d;
    }

    // Bolinha de cor de linha (muda a cor da borda da linha).
    Button LineDot(string name, string hex)
    {
        var d = Dot(ShadowCache.BrushOf((Color)ColorConverter.ConvertFromString(hex)), $"Linha {name.ToLowerInvariant()}", () =>
        {
            ChangeLine(_ =>
            {
                if (_item is null) return;
                var st = _item.Style.Clone();
                st.Borda = hex;
                _item.Style = st;
            });
            Remember(StorageService.State.CoresLinhaRecentes, hex);
        });
        d.DataContext = hex;
        d.Margin = new Thickness(0, 0, 4.5, 6); // as 8 cores cabem numa linha só
        return d;
    }

    // Põe a cor no topo das últimas usadas (até 8) e grava.
    static void Remember(List<string> recent, string key)
    {
        recent.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, key);
        if (recent.Count > RecentCount) recent.RemoveRange(RecentCount, recent.Count - RecentCount);
        StorageService.SaveState();
    }

    /// <summary>Monta as últimas cores ao abrir o menu (sem histórico ainda: as primeiras da paleta). Não mudam de lugar com o menu aberto.</summary>
    void BuildRecents()
    {
        RecentDots.Children.Clear();
        _recentDots.Clear();
        var keys = StorageService.State.CoresRecentes.Where(k => Palette.Colors.Any(c => c.Key == k))
            .Concat(Palette.Colors.Where(c => c.IsStrong).Select(c => c.Key)).Distinct().Take(RecentCount);
        foreach (var key in keys)
        {
            var d = ItemDot(Palette.Get(key));
            _recentDots.Add(d);
            RecentDots.Children.Add(d);
        }
        RecentLineDots.Children.Clear();
        var lineKeys = StorageService.State.CoresLinhaRecentes
            .Where(k => BoardItemViewModel.LineColors.Any(c => string.Equals(c.Hex, k, StringComparison.OrdinalIgnoreCase)))
            .Concat(BoardItemViewModel.LineColors.Select(c => c.Hex)).Distinct(StringComparer.OrdinalIgnoreCase).Take(RecentCount);
        foreach (var hex in lineKeys)
        {
            var (name, h) = BoardItemViewModel.LineColors.First(c => string.Equals(c.Hex, hex, StringComparison.OrdinalIgnoreCase));
            var d = LineDot(name, h);
            _recentDots.Add(d);
            RecentLineDots.Children.Add(d);
        }
    }

    // "Paleta de cores": mostra ou esconde todas as cores.
    void OnPalette(object sender, RoutedEventArgs e)
    {
        var panel = _item?.Line is null ? ItemDots : LineDots;
        panel.Visibility = panel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        ContentChanged?.Invoke();
    }

    /// <summary>O menu mudou de altura (a paleta abriu ou fechou): a janela reposiciona.</summary>
    public event Action? ContentChanged;

    /// <summary>Uma coluna quando cabe na altura disponível; senão, duas, para aparecer tudo sem rolar.</summary>
    public void FitTo(double available)
    {
        SetColumns(false);
        MaxHeight = double.PositiveInfinity;
        UpdateLayout();
        if (ItemPart.DesiredSize.Height + 12 > available) SetColumns(true);
    }

    // Uma coluna (266) ou duas (528), com a parte da direita indo para o lado.
    void SetColumns(bool two)
    {
        Width = two ? 528 : 266;
        Grid.SetRow(RightPart, two ? 0 : 1);
        Grid.SetColumn(RightPart, two ? 1 : 0);
        RightPart.Width = 254;
        RightPart.Margin = new Thickness(two ? 8 : 0, 0, 0, 0);
        PartGap.Visibility = two ? Visibility.Collapsed : Visibility.Visible;
    }

    bool _fillingAlpha;

    // Barra "Transparência do fundo" das formas.
    void OnFillChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        FillText.Text = $"{Math.Round(FillSlider.Value)}%";
        if (_fillingAlpha || _item is not { } item) return;
        Vm.SetFillTransparency(item, Math.Round(FillSlider.Value));
    }

    // Bolinha que cresce 18% com o mouse em cima.
    static Button Dot(Brush fill, string tip, Action click)
    {
        var scale = new ScaleTransform();
        var b = new Button { Style = (Style)Application.Current.FindResource("ColorDot"), Background = fill, ToolTip = tip, RenderTransform = scale };
        b.Click += (_, _) => click();
        b.MouseEnter += (_, _) => Hover(1.18);
        b.MouseLeave += (_, _) => Hover(1);
        void Hover(double s)
        {
            Anim.To(scale, ScaleTransform.ScaleXProperty, s, 0.5, Anim.Tap);
            Anim.To(scale, ScaleTransform.ScaleYProperty, s, 0.5, Anim.Tap);
        }
        return b;
    }

    /// <summary>Prepara o menu para o item: mostra só as partes que valem para o tipo dele.</summary>
    public void ShowForItem(BoardItemViewModel item)
    {
        _item = item;
        StylePart.Visibility = item.Kind == ItemKind.PostIt ? Visibility.Visible : Visibility.Collapsed;
        LinePart.Visibility = item.Line is null ? Visibility.Collapsed : Visibility.Visible;
        ColorPart.Visibility = item.Line is null ? Visibility.Visible : Visibility.Collapsed;
        SpiralRow.Visibility = item.Kind == ItemKind.Page ? Visibility.Visible : Visibility.Collapsed;
        FontRow.Visibility = SizeRow.Visibility = item.Kind == ItemKind.Text ? Visibility.Visible : Visibility.Collapsed;
        FillPart.Visibility = item.Kind == ItemKind.Shape ? Visibility.Visible : Visibility.Collapsed;
        _fillingAlpha = true;
        FillSlider.Value = Math.Round((1 - MainViewModel.FillAlpha(item)) * 100);
        FillText.Text = $"{Math.Round(FillSlider.Value)}%";
        _fillingAlpha = false;
        _fillingSize = true;
        SizeBox.Text = Math.Round(item.Style.Tamanho).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _fillingSize = false;
        SpiralText.Text = item.Spiral ? "Tirar a espiral" : "Pôr a espiral";
        // Linhas: sem "escrever na linha" por enquanto (nomes nas linhas desligados).
        EditRow.Visibility = item.Line is null ? Visibility.Visible : Visibility.Collapsed;
        ItemDots.Visibility = LineDots.Visibility = Visibility.Collapsed;
        BuildRecents();
        Refresh();
    }

    /// <summary>Marca a cor e o estilo atuais do item.</summary>
    void Refresh()
    {
        if (_item is null) return;
        foreach (var d in _itemDots.Concat(_recentDots))
            d.Tag = string.Equals((string)d.DataContext, _item.Line is null ? _item.ColorKey : _item.Style.Borda, StringComparison.OrdinalIgnoreCase) ? "on" : null;
        ChipPin.IsChecked = _item.Fastening == Fastening.Pin;
        ChipTape.IsChecked = _item.Fastening == Fastening.Tape;
        ChipNone.IsChecked = _item.Fastening == Fastening.None;
        ChipLined.IsChecked = _item.Lined;
        LockText.Text = _item.Locked ? "Desbloquear" : "Bloquear";
        if (_item.Line is { } l)
        {
            int arrows = (l.De.Ponta == "seta" ? 1 : 0) + (l.Para.Ponta == "seta" ? 1 : 0);
            DirNone.IsChecked = arrows == 0;
            DirOne.IsChecked = arrows == 1;
            DirBoth.IsChecked = arrows == 2;
            PathStraight.IsChecked = l.Tipo is not ("curva" or "angulo");
            PathCurve.IsChecked = l.Tipo == "curva";
            PathAngle.IsChecked = l.Tipo == "angulo";
            StrokeSolid.IsChecked = l.Traco != "barbante";
            StrokeTwine.IsChecked = l.Traco == "barbante";
            foreach (var d in _lineDots)
                d.Tag = string.Equals((string)d.DataContext, _item.Style.Borda, StringComparison.OrdinalIgnoreCase) ? "on" : null;
        }
    }

    // ---------- linha ----------

    // Muda a linha com desfazer e lembra o estilo para as próximas.
    void ChangeLine(Action<LineData> change)
    {
        if (_item?.Line is not { } l) return;
        Vm.Checkpoint();
        change(l);
        _item.Touch(nameof(BoardItemViewModel.Line));
        Vm.CommitMove();
        DrawSettings.RememberLine(_item); // o que o usuário acabou de escolher vira o padrão das próximas linhas
        Refresh();
    }

    /// <summary>Sem seta · uma (no fim, ou onde já estava) · duas.</summary>
    void OnDirection(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string t }) return;
        ChangeLine(l =>
        {
            switch (t)
            {
                case "0": l.De.Ponta = l.Para.Ponta = "nenhuma"; break;
                case "1":
                    bool startOnly = l.De.Ponta == "seta" && l.Para.Ponta != "seta";
                    l.De.Ponta = startOnly ? "seta" : "nenhuma";
                    l.Para.Ponta = startOnly ? "nenhuma" : "seta";
                    break;
                default: l.De.Ponta = l.Para.Ponta = "seta"; break;
            }
        });
    }

    // Traço (sólido ou barbante), inverter a seta e traçado (reta, curva, ângulo).
    void OnStroke(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string t }) ChangeLine(l => l.Traco = t);
    }

    void OnFlip(object sender, RoutedEventArgs e) => ChangeLine(l => (l.De.Ponta, l.Para.Ponta) = (l.Para.Ponta, l.De.Ponta));

    void OnLineType(object sender, RoutedEventArgs e)
    {
        // Trocar o traçado volta para a dobra automática (depois dá para arrastar a alça do meio).
        if (sender is FrameworkElement { Tag: string t }) ChangeLine(l => { l.Tipo = t; l.DobraX = l.DobraY = null; });
    }

    // ---------- item ----------

    void OnItemStyle(object sender, RoutedEventArgs e)
    {
        if (_item is not null && sender is ToggleButton { Tag: NoteStyle s }) Vm.SetStyle(_item, s);
        Refresh();
    }

    void OnEdit(object sender, RoutedEventArgs e)
    {
        var item = _item;
        CloseRequested?.Invoke();
        if (item is { Kind: ItemKind.Page, FileFullPath: { } page }) App.OpenPage(page);
        else if (item is not null) Vm.BeginEdit(item, EditField.Text);
    }

    /// <summary>Pede o menu extra (agrupar, alinhar, camadas, estilo, descrição) no ponto da janela.</summary>
    public event Action<BoardItemViewModel, Point>? MoreRequested;

    // Tamanho do título: muda a cada dígito; Enter ou Esc fecha o menu. O desfazer volta ao tamanho de antes de digitar.
    bool _fillingSize;

    void OnSizeFocus(object sender, RoutedEventArgs e)
    {
        Vm.Checkpoint();
        SizeBox.SelectAll();
    }

    void OnSizeChanged(object sender, TextChangedEventArgs e)
    {
        if (_fillingSize || _item is not { } item) return;
        if (!double.TryParse(SizeBox.Text, out var px) || px < 8 || px > 400) return;
        var st = item.Style.Clone();
        st.Tamanho = px;
        item.Style = st;
    }

    void OnSizeKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (System.Windows.Input.Key.Enter or System.Windows.Input.Key.Escape)) return;
        CloseRequested?.Invoke();
        e.Handled = true;
    }

    /// <summary>Pede a lista de caligrafias no ponto da janela.</summary>
    public event Action<BoardItemViewModel, Point>? FontsRequested;

    // Caligrafias e "Mais opções": fecham este menu e pedem o outro ao lado dele.
    void OnFonts(object sender, RoutedEventArgs e) => OpenSide(FontsRequested);
    void OnMore(object sender, RoutedEventArgs e) => OpenSide(MoreRequested);

    void OpenSide(Action<BoardItemViewModel, Point>? request)
    {
        var item = _item;
        var p = TranslatePoint(new Point(ActualWidth, 0), (UIElement)Parent);
        CloseRequested?.Invoke();
        if (item is not null) request?.Invoke(item, p);
    }

    // Ações simples do item: fazem e fecham o menu.
    void OnSpiral(object sender, RoutedEventArgs e) => Act(i => { Vm.Checkpoint(); i.Spiral = !i.Spiral; });
    void OnDuplicate(object sender, RoutedEventArgs e) => Act(i => Vm.Duplicate(i));
    void OnLock(object sender, RoutedEventArgs e) => Act(Vm.ToggleLock);
    void OnFront(object sender, RoutedEventArgs e) => Act(Vm.BringToFront);
    void OnBack(object sender, RoutedEventArgs e) => Act(Vm.SendToBack);
    void OnDelete(object sender, RoutedEventArgs e) => Act(Vm.Remove);

    void Act(Action<BoardItemViewModel> action)
    {
        if (_item is not null) action(_item);
        CloseRequested?.Invoke();
    }
}
