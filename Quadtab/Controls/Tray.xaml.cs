using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Quadtab.Models;
using Quadtab.Rendering;
using Quadtab.ViewModels;

namespace Quadtab.Controls;

/// <summary>O que um elemento da bandeja cria. Color null = última cor usada.</summary>
public sealed record TrayPayload(ItemKind Kind, string? Color, NoteStyle? Style = null);

/// <summary>
/// Dock + bandeja. Clicar adiciona no centro do quadro; arrastar mostra uma "cópia" seguindo o mouse
/// e cria o item onde for solto.
/// </summary>
public partial class Tray : UserControl
{
    MainViewModel? _vm;

    // arrastar da bandeja
    FrameworkElement? _source;
    TrayPayload? _payload;
    Point _start;
    Rectangle? _ghost;

    /// <summary>Clique: criar no centro da área visível.</summary>
    public event Action<TrayPayload>? AddRequested;

    /// <summary>Soltou em algum ponto (relativo a Root). Retorna se criou.</summary>
    public event Func<TrayPayload, Point, bool>? DropRequested;

    /// <summary>Camada da janela onde a cópia arrastada é desenhada.</summary>
    public Canvas? Overlay { get; set; }

    /// <summary>Referência de coordenadas (conteúdo da janela).</summary>
    public UIElement? Root { get; set; }

    /// <summary>Algo está sendo arrastado da bandeja agora.</summary>
    public bool IsDragging => _source is not null;

    public GlassPanel SheetGlass => Sheet;
    public GlassPanel DockGlass => Dock;

    public Tray()
    {
        InitializeComponent();

        Draggable(CardPage, new TrayPayload(ItemKind.Page, "branco"));
        Draggable(CardBlock, new TrayPayload(ItemKind.Block, "branco"));
        Draggable(CardText, new TrayPayload(ItemKind.Text, "branco"));
        Draggable(CardBox, new TrayPayload(ItemKind.TextBox, "branco"));
        Draggable(CardShape, new TrayPayload(ItemKind.Shape, "azul"));
        Draggable(CardTable, new TrayPayload(ItemKind.Table, "branco"));

        QuickPage.Click += (_, _) => AddRequested?.Invoke(new TrayPayload(ItemKind.Page, "branco"));
        QuickText.Click += (_, _) => AddRequested?.Invoke(new TrayPayload(ItemKind.Text, "branco"));
        QuickBlock.Click += (_, _) => AddRequested?.Invoke(new TrayPayload(ItemKind.Block, "branco"));
        QuickShape.Click += (_, _) => AddRequested?.Invoke(new TrayPayload(ItemKind.Shape, "azul"));

        DataContextChanged += (_, e) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVm;
            _vm = e.NewValue as MainViewModel;
            if (_vm is not null)
            {
                _vm.PropertyChanged += OnVm;
                SetOpen(_vm.TrayOpen, animate: false);
            }
        };
    }

    void OnVm(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.TrayOpen)) SetOpen(_vm!.TrayOpen, animate: true);
    }

    // ---------- abrir e fechar (mola de painel, 0.55 s) ----------

    void SetOpen(bool open, bool animate)
    {
        double dur = animate ? 1 : 0;
        Anim.To(ChevronTurn, RotateTransform.AngleProperty, open ? 180 : 0, 0.45 * dur, Anim.Item);
        if (open)
        {
            Sheet.Visibility = Visibility.Visible;
            Sheet.IsHitTestVisible = true;
            Anim.To(Sheet, OpacityProperty, 1, 0.25 * dur, Anim.Ease);
            Anim.To(SheetScale, ScaleTransform.ScaleXProperty, 1, 0.55 * dur, Anim.Panel);
            Anim.To(SheetScale, ScaleTransform.ScaleYProperty, 1, 0.55 * dur, Anim.Panel);
            Anim.To(SheetMove, TranslateTransform.YProperty, 0, 0.55 * dur, Anim.Panel);
        }
        else
        {
            Sheet.IsHitTestVisible = false;
            Anim.To(Sheet, OpacityProperty, 0, 0.25 * dur, Anim.Ease, () =>
            {
                if (_vm is { TrayOpen: false }) Sheet.Visibility = Visibility.Collapsed; // não desenha quando fechada
            });
            Anim.To(SheetScale, ScaleTransform.ScaleXProperty, 0.95, 0.55 * dur, Anim.Panel);
            Anim.To(SheetScale, ScaleTransform.ScaleYProperty, 0.95, 0.55 * dur, Anim.Panel);
            Anim.To(SheetMove, TranslateTransform.YProperty, 18, 0.55 * dur, Anim.Panel);
        }
        TrackBackdrop(0.6 * dur);
    }

    // Durante a animação o vidro precisa reposicionar o fundo desfocado a cada quadro.
    TimeSpan _trackUntil;
    bool _tracking;

    void TrackBackdrop(double seconds)
    {
        _trackUntil = DateTime.Now.TimeOfDay + TimeSpan.FromSeconds(seconds);
        if (_tracking) return;
        _tracking = true;
        CompositionTarget.Rendering += OnTrackFrame;
    }

    void OnTrackFrame(object? sender, EventArgs e)
    {
        Sheet.RefreshBackdrop();
        if (DateTime.Now.TimeOfDay > _trackUntil)
        {
            _tracking = false;
            CompositionTarget.Rendering -= OnTrackFrame;
        }
    }

    // ---------- clicar ou arrastar ----------

    void Draggable(FrameworkElement el, TrayPayload payload)
    {
        el.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _source = el;
            _payload = payload;
            _start = e.GetPosition(Root);
            el.CaptureMouse();
            e.Handled = true;
        };
        el.PreviewMouseMove += (_, e) =>
        {
            if (_source != el || _payload is null || Root is null) return;
            var p = e.GetPosition(Root);
            if (_ghost is null && (p - _start).Length > 4) StartGhost(el);
            if (_ghost is not null) MoveGhost(p);
        };
        el.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_source != el || _payload is null) return;
            var p = e.GetPosition(Root);
            var pl = _payload;
            bool dragged = _ghost is not null;
            Reset();
            if (dragged) DropRequested?.Invoke(pl, p);
            else AddRequested?.Invoke(pl);
            e.Handled = true;
        };
        el.LostMouseCapture += (_, _) => { if (_source == el) Reset(); };
    }

    void StartGhost(FrameworkElement el)
    {
        if (Overlay is null) return;
        _ghost = new Rectangle
        {
            Width = el.ActualWidth,
            Height = el.ActualHeight,
            Fill = new VisualBrush(el) { Stretch = Stretch.None },
            Opacity = 0.9,
            IsHitTestVisible = false,
        };
        Overlay.Children.Add(_ghost);
        el.Cursor = BoardCursors.Grabbing;
    }

    void MoveGhost(Point p)
    {
        Canvas.SetLeft(_ghost!, p.X - _ghost!.Width / 2);
        Canvas.SetTop(_ghost, p.Y - _ghost.Height / 2);
    }

    void Reset()
    {
        var src = _source;
        _source = null;
        _payload = null;
        if (_ghost is not null)
        {
            Overlay?.Children.Remove(_ghost);
            _ghost = null;
        }
        if (src is not null)
        {
            src.Cursor = BoardCursors.Arrow;
            if (src.IsMouseCaptured) src.ReleaseMouseCapture();
        }
    }
}
