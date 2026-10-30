using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Quadtab.Models;
using Quadtab.Rendering;
using Quadtab.ViewModels;

namespace Quadtab.Controls;

/// <summary>Menu próprio do botão direito num item (estilo menu de toque longo do iPhone).</summary>
public partial class BoardMenu : UserControl
{
    BoardItemViewModel? _item;
    readonly List<Button> _itemDots = [];
    readonly List<Button> _lineDots = [];

    /// <summary>O menu terminou uma ação e deve fechar.</summary>
    public event Action? CloseRequested;

    public GlassPanel GlassPanel => Glass;

    MainViewModel Vm => App.ViewModel;

    public BoardMenu()
    {
        InitializeComponent();
        foreach (var c in Palette.Colors)
        {
            var key = c.Key;
            var d = Dot(c.PaperBrush, $"Muda a cor para {c.Name.ToLowerInvariant()}", () =>
            {
                if (_item is null) return;
                Vm.SetColor(_item, key);
                Refresh();
            });
            d.DataContext = key;
            _itemDots.Add(d);
            d.Margin = new Thickness(0, 0, 4.5, 6); // 8 por linha (16 cores, duas linhas)
            ItemDots.Children.Add(d);
        }
        // Linhas: cores próprias, mais vivas (inclui juta, a cor natural do barbante).
        foreach (var (name, hex) in BoardItemViewModel.LineColors)
        {
            var d = Dot(ShadowCache.BrushOf((Color)ColorConverter.ConvertFromString(hex)), $"Linha {name.ToLowerInvariant()}", () => ChangeLine(_ =>
            {
                if (_item is null) return;
                var st = _item.Style.Clone();
                st.Borda = hex;
                _item.Style = st;
            }));
            d.DataContext = hex;
            d.Margin = new Thickness(0, 0, 6, 6);
            _lineDots.Add(d);
            LineDots.Children.Add(d);
        }
    }

    static Button Dot(Brush fill, string tip, Action click)
    {
        var scale = new ScaleTransform();
        var b = new Button
        {
            Style = (Style)Application.Current.FindResource("ColorDot"),
            Background = fill,
            ToolTip = tip,
            RenderTransform = scale,
        };
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

    public void ShowForItem(BoardItemViewModel item)
    {
        _item = item;
        StylePart.Visibility = item.Kind == ItemKind.PostIt ? Visibility.Visible : Visibility.Collapsed;
        LinePart.Visibility = item.Line is null ? Visibility.Collapsed : Visibility.Visible;
        ColorPart.Visibility = item.Line is null ? Visibility.Visible : Visibility.Collapsed;
        SpiralRow.Visibility = item.Kind == ItemKind.Page ? Visibility.Visible : Visibility.Collapsed;
        SpiralText.Text = item.Spiral ? "Tirar a espiral" : "Pôr a espiral";
        // Linhas: sem "escrever na linha" por enquanto (nomes nas linhas desligados).
        EditRow.Visibility = item.Line is null ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    /// <summary>Marca a cor e o estilo atuais do item.</summary>
    void Refresh()
    {
        if (_item is null) return;
        foreach (var d in _itemDots) d.Tag = (string)d.DataContext == _item.ColorKey ? "on" : null;
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

    void ChangeLine(Action<LineData> change)
    {
        if (_item?.Line is not { } l) return;
        Vm.Checkpoint();
        change(l);
        _item.Touch(nameof(BoardItemViewModel.Line));
        Vm.CommitMove();
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

    void OnMore(object sender, RoutedEventArgs e)
    {
        var item = _item;
        var p = TranslatePoint(new Point(ActualWidth, 0), (UIElement)Parent);
        CloseRequested?.Invoke();
        if (item is not null) MoreRequested?.Invoke(item, p);
    }

    void OnSpiral(object sender, RoutedEventArgs e)
    {
        if (_item is not null)
        {
            Vm.Checkpoint();
            _item.Spiral = !_item.Spiral;
        }
        CloseRequested?.Invoke();
    }

    void OnDuplicate(object sender, RoutedEventArgs e)
    {
        if (_item is not null) Vm.Duplicate(_item);
        CloseRequested?.Invoke();
    }

    void OnLock(object sender, RoutedEventArgs e)
    {
        if (_item is not null) Vm.ToggleLock(_item);
        CloseRequested?.Invoke();
    }

    void OnFront(object sender, RoutedEventArgs e)
    {
        if (_item is not null) Vm.BringToFront(_item);
        CloseRequested?.Invoke();
    }

    void OnBack(object sender, RoutedEventArgs e)
    {
        if (_item is not null) Vm.SendToBack(_item);
        CloseRequested?.Invoke();
    }

    void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_item is not null) Vm.Remove(_item);
        CloseRequested?.Invoke();
    }
}
