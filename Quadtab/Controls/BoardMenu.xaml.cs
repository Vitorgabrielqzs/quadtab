using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Quadtab.Models;
using Quadtab.Rendering;
using Quadtab.ViewModels;

namespace Quadtab.Controls;

/// <summary>Menu próprio do botão direito (estilo menu de toque longo do iPhone).</summary>
public partial class BoardMenu : UserControl
{
    BoardItemViewModel? _item;
    Point _boardPoint;
    readonly List<Button> _itemDots = [];

    /// <summary>O menu terminou uma ação e deve fechar.</summary>
    public event Action? CloseRequested;

    /// <summary>Usado para criar itens no ponto clicado.</summary>
    public BoardView? Board { get; set; }

    public GlassPanel GlassPanel => Glass;

    MainViewModel Vm => App.ViewModel;

    public BoardMenu()
    {
        InitializeComponent();
        var cork = new ImageBrush(BoardSurfaces.Cork)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 96, 96),
        };
        cork.Freeze();
        CorkThumb.Background = cork;

        foreach (var c in Palette.Colors)
        {
            var key = c.Key;
            NewDots.Children.Add(Dot(c, $"Cola um post-it {c.Name.ToLowerInvariant()} aqui", () =>
            {
                Vm.Add(ItemKind.PostIt, key, _boardPoint.X, _boardPoint.Y, Vm.NewStyle);
                CloseRequested?.Invoke();
            }));
            var d = Dot(c, $"Muda a cor para {c.Name.ToLowerInvariant()}", () =>
            {
                if (_item is null) return;
                Vm.SetColor(_item, key);
                Refresh();
            });
            d.DataContext = key;
            _itemDots.Add(d);
            ItemDots.Children.Add(d);
        }
    }

    static Button Dot(NoteColor c, string tip, Action click)
    {
        var scale = new ScaleTransform();
        var b = new Button
        {
            Style = (Style)Application.Current.FindResource("ColorDot"),
            Background = c.PaperBrush,
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

    public void ShowForBoard(Point boardPoint)
    {
        _item = null;
        _boardPoint = boardPoint;
        BoardPart.Visibility = Visibility.Visible;
        ItemPart.Visibility = Visibility.Collapsed;
    }

    public void ShowForItem(BoardItemViewModel item)
    {
        _item = item;
        BoardPart.Visibility = Visibility.Collapsed;
        ItemPart.Visibility = Visibility.Visible;
        StylePart.Visibility = item.Kind == ItemKind.PostIt ? Visibility.Visible : Visibility.Collapsed;
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
    }

    // ---------- quadro vazio ----------

    void AddHere(ItemKind kind, string color)
    {
        Vm.Add(kind, color, _boardPoint.X, _boardPoint.Y);
        CloseRequested?.Invoke();
    }

    void OnAddPage(object sender, RoutedEventArgs e) => AddHere(ItemKind.Page, "branco");
    void OnAddText(object sender, RoutedEventArgs e) => AddHere(ItemKind.Text, "branco");
    void OnAddBlock(object sender, RoutedEventArgs e) => AddHere(ItemKind.Block, "azul");

    void OnToggleTray(object sender, RoutedEventArgs e)
    {
        Vm.TrayOpen = !Vm.TrayOpen;
        CloseRequested?.Invoke();
    }

    void OnToggleTitle(object sender, RoutedEventArgs e)
    {
        Vm.TitleHidden = !Vm.TitleHidden;
        CloseRequested?.Invoke();
    }

    void OnPickModel(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BoardModel m }) Vm.Model = m;
        CloseRequested?.Invoke();
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
