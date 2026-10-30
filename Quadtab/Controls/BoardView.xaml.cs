using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Quadtab.Behaviors;
using Quadtab.Models;
using Quadtab.Rendering;
using Quadtab.Services;
using Quadtab.ViewModels;

namespace Quadtab.Controls;

/// <summary>Ferramenta ativa no quadro.</summary>
public enum BoardTool { Select, Pen, Pencil, Marker, Eraser, Lasso, Line }

/// <summary>
/// O quadro infinito: câmera (posição + zoom de 10% a 400%), virtualização com quadtree e todas as
/// interações (arrastar, alças, guias, conectores, desenho, soltar arquivos). Tudo o que se move é
/// aplicado uma única vez por quadro, em OnFrame.
/// </summary>
public partial class BoardView : UserControl
{
    static readonly Random Rng = new();
    const double FrameBudgetMs = 2;  // tempo máximo criando itens por quadro
    const double EdgeZone = 40;      // perto da borda (px), arrastando um item, a câmera anda sozinha
    const double EdgeSpeed = 1400;   // velocidade máxima desse movimento (px/s na tela)
    const double SnapPx = 6;         // distância (tela) para encaixar nas guias
    static readonly Brush GuideBrush = ShadowCache.BrushOf(Color.FromRgb(0xFF, 0xD6, 0x0A)); // guias amarelas, estilo Apple
    static readonly Brush AccentBrush = ShadowCache.BrushOf(Color.FromRgb(0x0A, 0x84, 0xFF));

    /// <summary>Arraste de um ou vários itens: andam só pelo TranslateTransform; X/Y gravados ao soltar.</summary>
    sealed class DragState(BoardItemContainer grabbed, Point world, Point pointer)
    {
        public readonly BoardItemContainer Grabbed = grabbed;
        public readonly Point WorldStart = world;
        public readonly List<(BoardItemContainer Container, BoardItemViewModel Item)> Parts = [];
        public Point Pointer = pointer;
        public Vector Offset;
        public bool Moved;
        public Rect Box;                         // caixa de tudo o que se move (antes do deslocamento)
        public List<double> GuideX = [], GuideY = [];
        Dictionary<int, BoardItemViewModel>? _moving;
        /// <summary>Itens que se movem, por id (montado uma vez por arraste; as linhas presas a eles acompanham).</summary>
        public Dictionary<int, BoardItemViewModel> Moving => _moving ??= MovedById(Parts.Select(p => p.Item));
    }

    /// <summary>Alça de redimensionar (0..7) ou girar (8), ou ponta de linha (9 = início, 10 = fim).</summary>
    sealed class HandleState(BoardItemViewModel item, int handle, Point world)
    {
        public readonly BoardItemViewModel Item = item;
        public readonly int Handle = handle;
        public readonly Point WorldStart = world;
        public readonly Rect Box = item.Box;
        public readonly double Rotation = item.Rotation;
        public readonly double FontSize = item.Style.Tamanho;
        public Point Pointer = world;
    }

    readonly BoardCamera _camera = new();
    readonly QuadTree<BoardItemViewModel> _index = new();
    readonly Dictionary<BoardItemViewModel, BoardItemContainer> _containers = new();
    readonly List<BoardItemViewModel> _query = [];
    readonly List<BoardItemViewModel> _pending = [];
    readonly Stack<BoardItemContainer>[] _pool = Enumerable.Range(0, 13).Select(_ => new Stack<BoardItemContainer>()).ToArray();
    readonly BitmapCache _itemCache = new(1) { EnableClearType = false, SnapsToDevicePixels = false };
    readonly DispatcherTimer _idle;
    readonly Stopwatch _work = new();

    MainViewModel? _vm;
    Rect _realizedArea = Rect.Empty;
    bool _ticking, _cameraReady, _autoFit, _testing;
    TimeSpan _lastFrame;
    double _dpi = 1;

    DragState? _drag;
    HandleState? _handle;

    // seleção em caixa (Shift + arrastar no vazio)
    bool _marquee;
    Point _marqueeStart;
    HashSet<BoardItemViewModel> _marqueeBase = [];

    // mover o quadro arrastando (botão esquerdo no vazio, botão do meio ou Espaço + arrastar)
    bool _panning;
    Point _panPointer, _panApplied;
    readonly Queue<(long Ticks, Point Pos)> _panSamples = new();

    // desenho, laço, borracha e conectores
    List<Point>? _stroke;
    List<Point>? _lasso;
    bool _erasing;
    (BoardItemViewModel From, string Side)? _connecting;
    Point? _lineStart;
    BoardItemViewModel? _hoverItem;

    /// <summary>Pede o menu do item: (item, ponto na janela).</summary>
    public event Action<BoardItemViewModel, Point>? MenuRequested;

    /// <summary>Clique direito no vazio: abre a bandeja junto ao cursor (ponto no mundo, ponto na janela).</summary>
    public event Action<Point, Point>? TrayRequested;

    /// <summary>A câmera parou.</summary>
    public event Action? Settled;

    /// <summary>A ferramenta mudou (a paleta de desenho acompanha).</summary>
    public event Action? ToolChanged;

    public double LastWorkMs { get; private set; }
    public int RealizedCount => _containers.Count;
    public int ItemCount => _index.Count;
    public double Zoom => _camera.Zoom;
    public BoardCamera Camera => _camera;

    /// <summary>A câmera ou algum item está se movendo agora.</summary>
    public bool IsBusy => _camera.IsMoving || _panning || _drag is not null || _marquee || _handle is not null || _stroke is not null;

    public BoardTool Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            RestoreCursor();
            ClearPorts();
            ToolChanged?.Invoke();
        }
    }
    BoardTool _tool;

    /// <summary>Cursor da ferramenta atual (mão aberta na seleção).</summary>
    void RestoreCursor() => Surface.Cursor = _tool switch
    {
        BoardTool.Select => BoardCursors.Grab,
        BoardTool.Eraser or BoardTool.Lasso => Cursors.Cross,
        _ => Cursors.Pen,
    };

    public BoardView()
    {
        InitializeComponent();

        _idle = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(160) };
        _idle.Tick += (_, _) => OnIdle();

        DataContextChanged += (_, e) => Attach(e.NewValue as MainViewModel);

        Surface.SizeChanged += (_, e) =>
        {
            ApplyFrame();
            // O centro da câmera não muda: o que estava no meio continua no meio.
            _camera.Viewport = e.NewSize;
            if (!_cameraReady && e.NewSize.Width > 0) StartCamera();
            else if (_autoFit) Fit(animate: false);
            Invalidate();
        };

        Loaded += (_, _) => _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;

        Surface.PreviewMouseWheel += OnWheel;
        Surface.PreviewMouseDown += OnMouseDown;
        Surface.PreviewMouseMove += OnMouseMove;
        Surface.PreviewMouseUp += OnMouseUp;
        Surface.PreviewMouseRightButtonDown += OnRightDown;
        Surface.PreviewMouseRightButtonUp += OnRightUp;
        Surface.MouseLeave += (_, _) => { if (_connecting is null) ClearPorts(); };
        Surface.LostMouseCapture += (_, _) =>
        {
            if (_panning) EndPan(fling: false);
            if (_marquee) EndMarquee();
        };
        Surface.Cursor = BoardCursors.Arrow;
        Surface.AddHandler(Mouse.LostMouseCaptureEvent, new MouseEventHandler((_, _) =>
        {
            if (_drag is not null && !_drag.Grabbed.IsMouseCaptured && !_testing) EndDrag();
        }));

        // Arrastar arquivos do Windows para o quadro.
        Surface.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Surface.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && _vm is not null)
                MediaService.AddFiles(_vm, files, _camera.ToWorld(e.GetPosition(Surface)));
        };
    }

    MainViewModel Vm => _vm!;

    // ---------- ligação com o ViewModel ----------

    void Attach(MainViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.Items.CollectionChanged -= OnItemsChanged;
            _vm.PropertyChanged -= OnVmChanged;
            foreach (var it in _vm.Items) it.PropertyChanged -= OnItemChanged;
        }
        foreach (var c in _containers.Values) c.Detach();
        _containers.Clear();
        ItemsLayer.Children.Clear();
        ItemsLayer.Children.Add(InkPreview);
        foreach (var p in _pool) p.Clear();
        _index.Clear();
        _lines = null;
        _realizedArea = Rect.Empty;
        _drag = null;
        _handle = null;
        ClearPorts();
        Adorner.Children.Clear();

        _vm = vm;
        if (vm is null) return;
        vm.Items.CollectionChanged += OnItemsChanged;
        vm.PropertyChanged += OnVmChanged;
        foreach (var it in vm.Items) Track(it);
        ApplyGlassModel();
        if (_cameraReady) StartCamera(); // trocou de quadro: câmera deste quadro
        Invalidate();
        // Os itens do quadro aberto surgem suavemente.
        Anim.Set(ItemsLayer, OpacityProperty, 0.0);
        Anim.To(ItemsLayer, OpacityProperty, 1, 0.35, Anim.Ease);
    }

    void Track(BoardItemViewModel it)
    {
        it.PropertyChanged += OnItemChanged;
        _index.Set(it, it.Bounds);
    }

    void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset) { Attach(_vm); return; }
        _lines = null;
        if (e.OldItems is not null)
            foreach (BoardItemViewModel it in e.OldItems)
            {
                it.PropertyChanged -= OnItemChanged;
                _index.Remove(it);
                Unrealize(it);
            }
        if (e.NewItems is not null)
            foreach (BoardItemViewModel it in e.NewItems)
            {
                Track(it);
                Realize(it); // itens novos aparecem na hora, com uma mola
                if (e.NewItems.Count <= 20 && _containers.TryGetValue(it, out var c)) c.PlayAppear();
            }
        UpdateAdorner();
        Settle();
    }

    void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not BoardItemViewModel it) return;
        switch (e.PropertyName)
        {
            case nameof(BoardItemViewModel.X):
            case nameof(BoardItemViewModel.Y):
            case nameof(BoardItemViewModel.W):
            case nameof(BoardItemViewModel.H):
            case nameof(BoardItemViewModel.Rotation):
                _index.Set(it, it.Bounds);
                if (!_containers.ContainsKey(it) && it.Bounds.IntersectsWith(_realizedArea)) Realize(it);
                if (_drag is null && it.Kind != ItemKind.Line) RefreshLines(it);
                if (it.IsSelected) UpdateAdorner();
                break;
            case nameof(BoardItemViewModel.IsSelected):
            case nameof(BoardItemViewModel.Editing):
                if (it.IsSelected || it.Editing != EditField.None) Realize(it); // nunca some enquanto está em uso
                UpdateAdorner();
                break;
            case nameof(BoardItemViewModel.Line):
                ApplyLineVisibility(it);
                break;
        }
    }

    void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Model):
                BoardSurfaces.Update(Vm.Model, _camera.Zoom);
                ApplyFrame();
                ApplyGlassModel();
                Settle();
                break;
            case nameof(MainViewModel.DiagramMode):
                foreach (var (it, _) in _containers) ApplyLineVisibility(it);
                break;
        }
    }

    // ---------- câmera ----------

    void StartCamera()
    {
        _cameraReady = true;
        _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        _camera.Stop();
        _autoFit = false;
        if (Vm.Camera is { } saved)
        {
            _camera.Cx = saved.X;
            _camera.Cy = saved.Y;
            _camera.Zoom = BoardCamera.Clamp(saved.Zoom);
        }
        else if (Vm.Items.Count == 0)
        {
            // Quadro novo: câmera centralizada em (0, 0).
            _camera.Cx = _camera.Cy = 0;
            _camera.Zoom = 1;
        }
        else
        {
            _autoFit = true; // primeira vez: enquadra tudo (e reenquadra enquanto a janela termina de abrir)
            Fit(animate: false);
        }
        _itemCache.RenderAtScale = Math.Clamp(_camera.Zoom * _dpi, 0.1, 6);
        BoardSurfaces.Update(Vm.Model, _camera.Zoom);
        ApplyCamera();
        Settle();
    }

    void Kick()
    {
        if (_ticking) return;
        _ticking = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    void Invalidate()
    {
        _realizedArea = Rect.Empty;
        Kick();
    }

    /// <summary>Uma vez por quadro: avança a câmera, aplica arrastes e cria itens que ficaram visíveis.</summary>
    void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return;
        double dt = _lastFrame == TimeSpan.Zero ? 1 / 120.0 : Math.Min(0.05, (now - _lastFrame).TotalSeconds);
        _lastFrame = now;
        _work.Restart();

        bool changed = _camera.Step(dt);

        if (_panning && _panPointer != _panApplied)
        {
            _camera.PanNow(_panPointer - _panApplied);
            _panApplied = _panPointer;
            changed = true;
        }

        if (_drag is { } d) changed |= StepDrag(d, dt);
        if (_handle is { } h) StepHandle(h);

        bool needsRealize = !_realizedArea.Contains(_camera.VisibleWorld);
        if (changed || needsRealize) ApplyCamera();
        else if (_drag is not null || _handle is not null) UpdateAdorner();
        if (_pending.Count > 0) CreateSome();

        if (changed || _drag is not null) GlassGlow.Paused = true;
        LastWorkMs = _work.Elapsed.TotalMilliseconds;

        if (!changed && !_camera.IsMoving && _pending.Count == 0 && !_panning && _drag is null && _handle is null)
        {
            CompositionTarget.Rendering -= OnFrame;
            _ticking = false;
        }
    }

    bool StepDrag(DragState d, double dt)
    {
        bool changed = false;
        if (d.Moved)
        {
            // Perto da borda da janela a câmera anda sozinha e o item vai junto.
            var vs = _camera.Viewport;
            var edge = new Vector(Edge(d.Pointer.X, vs.Width), Edge(d.Pointer.Y, vs.Height));
            if (edge.X != 0 || edge.Y != 0)
            {
                _camera.PanNow(edge * EdgeSpeed * dt);
                changed = true;
            }
        }
        var off = _camera.ToWorld(d.Pointer) - d.WorldStart;
        if (!d.Moved && off.Length * _camera.Zoom >= 3)
        {
            d.Moved = true;
            foreach (var (c, _) in d.Parts) c.SetDragging(true);
            PrepareGuides(d);
        }
        if (!d.Moved) return changed;

        off = new Vector(Math.Round(off.X), Math.Round(off.Y));
        off = Snap(d, off);
        if (off != d.Offset)
        {
            d.Offset = off;
            foreach (var (c, _) in d.Parts)
            {
                c.DragOffset.X = off.X;
                c.DragOffset.Y = off.Y;
            }
            RefreshLines(d.Moving, off);
        }
        return changed;
    }

    static double Edge(double v, double size)
    {
        if (v < EdgeZone) return Math.Min(1, (EdgeZone - v) / EdgeZone);
        if (v > size - EdgeZone) return -Math.Min(1, (v - (size - EdgeZone)) / EdgeZone);
        return 0;
    }

    void ApplyCamera()
    {
        if (_camera.Viewport.Width <= 0) return;
        double z = _camera.Zoom;
        double tx = _camera.Viewport.Width / 2 - _camera.Cx * z;
        double ty = _camera.Viewport.Height / 2 - _camera.Cy * z;
        tx = Math.Round(tx * _dpi) / _dpi;
        ty = Math.Round(ty * _dpi) / _dpi;
        CameraTransform.Matrix = new Matrix(z, 0, 0, z, tx, ty);
        SurfaceBg.SetCamera(tx, ty, z);
        UpdateGlass();
        UpdateAdorner();
        if (_hoverItem is not null) ShowPorts(_hoverItem);
        if (_vm is not null && Math.Round(_vm.Zoom * 100) != Math.Round(z * 100)) _vm.Zoom = z;

        var visible = _camera.VisibleWorld;
        if (!_realizedArea.Contains(visible)) RealizeArea(visible);
        Settle();
    }

    void Settle()
    {
        _idle.Stop();
        _idle.Start();
    }

    void OnIdle()
    {
        if (IsBusy) return;
        _idle.Stop();
        if (_vm is null || !_cameraReady) return;
        BoardSurfaces.Update(_vm.Model, _camera.Zoom);
        GlassGlow.Paused = false;
        double scale = Math.Clamp(_camera.Zoom * _dpi, 0.1, 6);
        if (Math.Abs(_itemCache.RenderAtScale - scale) > 0.01) _itemCache.RenderAtScale = scale;
        _vm.ReportCamera(_camera.Cx, _camera.Cy, _camera.Zoom);
        Settled?.Invoke();
    }

    // ---------- virtualização ----------

    void RealizeArea(Rect visible)
    {
        var area = visible;
        area.Inflate(visible.Width * 0.5, visible.Height * 0.5);
        _realizedArea = area;

        _query.Clear();
        _index.Query(area, _query);
        var want = new HashSet<BoardItemViewModel>(_query);
        foreach (var (vm, _) in _containers.ToArray())
            if (!want.Contains(vm) && !IsPinned(vm)) Unrealize(vm);

        _pending.Clear();
        foreach (var vm in _query)
            if (!_containers.ContainsKey(vm)) _pending.Add(vm);
        var c = new Point(_camera.Cx, _camera.Cy);
        _pending.Sort((a, b) => Dist(a, c).CompareTo(Dist(b, c)));
        if (_pending.Count > 0) Kick();
    }

    static double Dist(BoardItemViewModel v, Point c) => (v.X - c.X) * (v.X - c.X) + (v.Y - c.Y) * (v.Y - c.Y);

    bool IsPinned(BoardItemViewModel vm) =>
        vm.IsSelected || vm.Editing != EditField.None || (_drag?.Parts.Exists(p => p.Item == vm) ?? false);

    void CreateSome()
    {
        var sw = Stopwatch.StartNew();
        int i = 0;
        while (i < _pending.Count && (i == 0 || sw.Elapsed.TotalMilliseconds < FrameBudgetMs)) Realize(_pending[i++]);
        _pending.RemoveRange(0, i);
    }

    void Realize(BoardItemViewModel vm)
    {
        if (_containers.ContainsKey(vm)) return;
        var pool = _pool[(int)vm.Kind];
        BoardItemContainer c;
        if (pool.Count > 0)
        {
            c = pool.Pop();
            c.DataContext = vm;
            c.Content = vm;
            c.Visibility = Visibility.Visible;
        }
        else
        {
            c = new BoardItemContainer { DataContext = vm, Content = vm, CacheMode = vm.Kind == ItemKind.Video ? null : _itemCache };
            c.SizeChanged += (_, e) =>
            {
                if (c.Item is not { } item) return;
                item.Width = e.NewSize.Width;
                item.Height = e.NewSize.Height;
                _index.Set(item, item.Bounds);
                if (item.IsSelected) UpdateAdorner();
                if (item.Kind != ItemKind.Line) RefreshLines(item);
            };
            ItemsLayer.Children.Add(c);
        }
        c.Attach(vm);
        _containers[vm] = c;
        ApplyLineVisibility(vm);
    }

    void Unrealize(BoardItemViewModel vm)
    {
        if (!_containers.Remove(vm, out var c)) return;
        c.Detach();
        c.Visibility = Visibility.Collapsed;
        _pool[(int)vm.Kind].Push(c);
    }

    /// <summary>No modo diagrama desligado, os conectores (linhas presas a itens) somem.</summary>
    void ApplyLineVisibility(BoardItemViewModel vm)
    {
        if (vm.Kind != ItemKind.Line || !_containers.TryGetValue(vm, out var c) || _vm is null) return;
        bool connector = vm.Line is { } l && (l.De.Item is not null || l.Para.Item is not null);
        c.Visibility = connector && !_vm.DiagramMode ? Visibility.Hidden : Visibility.Visible;
    }

    // ---------- zoom e navegação ----------

    Point ScreenCenter => new(_camera.Viewport.Width / 2, _camera.Viewport.Height / 2);

    public void ZoomStep(int direction)
    {
        _autoFit = false;
        _camera.ZoomBy(direction > 0 ? 1.25 : 0.8, ScreenCenter);
        Kick();
    }

    public void ZoomReset()
    {
        _autoFit = false;
        _camera.TravelTo(_camera.Cx, _camera.Cy, 1, 0.35);
        Kick();
    }

    /// <summary>"Ajustar": enquadra todos os itens (ou os indicados), centralizados, com margem de 80 px.</summary>
    public void Fit(bool animate = true, IEnumerable<BoardItemViewModel>? only = null)
    {
        var e = only is null ? _index.Extent() : only.Aggregate(Rect.Empty, (r, i) => { r.Union(i.Bounds); return r; });
        double vw = Math.Max(1, _camera.Viewport.Width - 160), vh = Math.Max(1, _camera.Viewport.Height - 160);
        if (e.IsEmpty) _camera.TravelTo(0, 0, 1, animate ? 0.55 : 0);
        else
        {
            double z = Math.Min(1, Math.Min(vw / e.Width, vh / e.Height));
            _camera.TravelTo(e.X + e.Width / 2, e.Y + e.Height / 2, z, animate ? 0.55 : 0);
        }
        if (!animate) ApplyCamera();
        Kick();
    }

    /// <summary>Vai até uma cena salva, com animação de câmera.</summary>
    public void GoTo(double x, double y, double zoom, double seconds = 0.7)
    {
        _autoFit = false;
        _camera.TravelTo(x, y, zoom, seconds);
        Kick();
    }

    public bool HasContentInView => _index.Count == 0 || _index.Any(_camera.VisibleWorld);

    public void FocusItem(BoardItemViewModel item)
    {
        _autoFit = false;
        var b = item.Box;
        _camera.TravelTo(b.X + b.Width / 2, b.Y + b.Height / 2, Math.Max(_camera.Zoom, 0.6));
        Kick();
    }

    public void PanBy(Vector contentDelta)
    {
        _autoFit = false;
        _camera.PanSmooth(contentDelta);
        Kick();
    }

    public bool SpaceHeld { get; set; }

    public Point ViewCenter => new(_camera.Cx, _camera.Cy);

    /// <summary>Ponto do mundo sob o mouse (ou o centro, se o mouse está fora do quadro).</summary>
    public Point PointerWorld
    {
        get
        {
            var p = Mouse.GetPosition(Surface);
            return p.X >= 0 && p.Y >= 0 && p.X <= Surface.ActualWidth && p.Y <= Surface.ActualHeight ? _camera.ToWorld(p) : ViewCenter;
        }
    }

    public Point ToWorld(UIElement from, Point p) => _camera.ToWorld(from.TranslatePoint(p, Surface));

    public bool IsInside(UIElement from, Point p)
    {
        var s = from.TranslatePoint(p, Surface);
        return s.X >= 0 && s.Y >= 0 && s.X <= Surface.ActualWidth && s.Y <= Surface.ActualHeight;
    }

    public void AddAtCenter(ItemKind kind, string color, NoteStyle? style = null) =>
        Vm.Add(kind, color, _camera.Cx + (Rng.NextDouble() * 120 - 60) / _camera.Zoom,
               _camera.Cy + (Rng.NextDouble() * 80 - 40) / _camera.Zoom, style);

    public bool TryAddAt(UIElement from, Point point, ItemKind kind, string color, NoteStyle? style)
    {
        if (!IsInside(from, point)) return false;
        var w = ToWorld(from, point);
        Vm.Add(kind, color, w.X, w.Y, style);
        return true;
    }

    // ---------- fundo dos painéis de vidro sobre o quadro ----------

    readonly List<GlassPanel> _glass = [];

    /// <summary>Painéis de vidro que ficam sobre o quadro (dica, status, dock...): enxergam a cor do fundo.</summary>
    public void UseAsGlassBackdrop(params GlassPanel[] panels)
    {
        foreach (var p in panels)
            _glass.Add(p);
        ApplyGlassModel();
    }

    // O vidro sobre o quadro enxerga a cor do fundo: a do tema no liso, a cor média nos outros modelos.
    void ApplyGlassModel()
    {
        if (_vm is null) return;
        if (_vm.Model == BoardModel.Liso)
            foreach (var p in _glass) p.SetResourceReference(GlassPanel.BackdropBrushProperty, "CanvasBrush");
        else
        {
            var solid = ShadowCache.BrushOf(ColorMath.Saturate(BoardSurfaces.AverageColor(_vm.Model), 1.9));
            foreach (var p in _glass) p.BackdropBrush = solid;
        }
    }

    bool _fullBleed;

    /// <summary>Tela cheia: o quadro vai de borda a borda, sem moldura, cantos ou sombras.</summary>
    public bool FullBleed
    {
        get => _fullBleed;
        set { _fullBleed = value; ApplyFrame(); }
    }

    /// <summary>Batente da moldura e cantos da superfície conforme o modelo (e a tela cheia).</summary>
    void ApplyFrame()
    {
        var model = _vm?.Model ?? BoardModel.Liso;
        FrameBg.FullBleed = SurfaceBg.FullBleed = _fullBleed;
        Surface.Margin = new Thickness(FrameBackground.Inset(model, _fullBleed));
        double r = SurfaceBackground.CornerRadius(model, _fullBleed);
        var size = Surface.RenderSize;
        var g = new RectangleGeometry(new Rect(size), r, r);
        g.Freeze();
        Surface.Clip = g;
    }

    static void UpdateGlass() { } // nada a alinhar: o fundo é liso

    public void RefreshGlass() => UpdateGlass();

    static double Mod(double a, double m) => a - Math.Floor(a / m) * m;

    // ---------- mouse ----------

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        _autoFit = false;
        // Rodinha (ou Ctrl + rodinha / pinça do touchpad): zoom no cursor. Shift + rodinha: anda na horizontal.
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            _camera.PanSmooth(new Vector(e.Delta * 0.6, 0));
        else
            _camera.ZoomBy(Math.Pow(1.0015, e.Delta), e.GetPosition(Surface));
        Kick();
        e.Handled = true;
    }

    void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _autoFit = false;
        var p = e.GetPosition(Surface);
        if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && SpaceHeld))
        {
            StartPan(p);
            e.Handled = true;
            return;
        }
        if (e.ChangedButton != MouseButton.Left) return;

        var src = e.OriginalSource as DependencyObject;
        if (Tree.FindAncestor<TextBox>(src) is not null) return; // clique dentro do campo em edição
        _camera.Stop();
        var world = _camera.ToWorld(p);

        // Alças de tamanho/rotação e pontas de linha
        if (src is FrameworkElement { Tag: HandleTag ht })
        {
            Vm.Checkpoint();
            _handle = new HandleState(ht.Item, ht.Index, world);
            Surface.CaptureMouse();
            Kick();
            e.Handled = true;
            return;
        }
        // Ponto de conexão: arrastar cria uma linha (ou uma cópia ligada, se soltar no vazio)
        if (src is FrameworkElement { Tag: PortTag pt })
        {
            _connecting = (pt.Item, pt.Side);
            Surface.CaptureMouse();
            UpdateConnector(p);
            e.Handled = true;
            return;
        }

        switch (Tool)
        {
            case BoardTool.Pen or BoardTool.Pencil or BoardTool.Marker:
                _stroke = [world];
                InkPreview.Stroke = ShadowCache.BrushOf(DrawSettings.Color);
                var pen = DrawingView.PenFor(DrawSettings.Stroke(Tool, []));
                InkPreview.Stroke = pen.Brush;
                InkPreview.StrokeThickness = pen.Thickness;
                InkPreview.Data = null;
                Surface.CaptureMouse();
                e.Handled = true;
                return;
            case BoardTool.Eraser:
                Vm.Checkpoint();
                _erasing = true;
                EraseAt(world);
                Surface.CaptureMouse();
                e.Handled = true;
                return;
            case BoardTool.Lasso:
                _lasso = [p];
                Lasso.Points = [p];
                Lasso.Visibility = Visibility.Visible;
                Surface.CaptureMouse();
                e.Handled = true;
                return;
            case BoardTool.Line:
                _lineStart = world;
                Surface.CaptureMouse();
                UpdateLinePreview(p);
                e.Handled = true;
                return;
        }

        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var container = Tree.FindAncestor<BoardItemContainer>(src);
        if (container?.Item is { } item)
        {
            if (e.ClickCount == 2)
            {
                OpenOrEdit(item, container, src, e.GetPosition(container));
                e.Handled = true;
                return;
            }
            Surface.Focus();
            if (shift) Vm.ToggleSelection(item);
            else if (!item.IsSelected) Vm.SelectWithGroup(item);

            if (item.Locked) StartPan(p); // bloqueado não se move: arrastar em cima dele move a câmera
            else if (item.IsSelected) StartDrag(container, p, Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
            e.Handled = true;
            return;
        }

        // Quadro vazio
        if (e.ClickCount == 2)
        {
            Vm.RepeatLast(world); // 2 cliques no vazio: outro item igual ao último criado (sem nenhum ainda: página)
            e.Handled = true;
            return;
        }
        Vm.EndEdit();
        Surface.Focus();
        if (shift) StartMarquee(p);
        else
        {
            Vm.ClearSelection();
            StartPan(p);
        }
        e.Handled = true;
    }

    /// <summary>Dois cliques: abre a página, o link ou o arquivo; edita a célula da tabela; ou edita o texto.</summary>
    void OpenOrEdit(BoardItemViewModel item, BoardItemContainer container, DependencyObject? src, Point local)
    {
        switch (item.Kind)
        {
            case ItemKind.Page when item.FileFullPath is { } page:
                App.OpenPage(page);
                return;
            case ItemKind.Link when item.Url is { } url:
                ItemViewBase.OpenExternal(url);
                return;
            case ItemKind.File or ItemKind.Video when item.FileFullPath is { } file:
                ItemViewBase.OpenExternal(file);
                return;
            case ItemKind.Image or ItemKind.Drawing:
                return;
            case ItemKind.Line:
                Vm.BeginEdit(item, EditField.Text); // rótulo no meio da linha (como no Obsidian)
                return;
            case ItemKind.Table:
                if (FindChild<TableView>(container) is { } table) table.EditAt(container.TranslatePoint(local, table));
                return;
        }
        var field = Tree.FindField(src, container);
        Vm.BeginEdit(item, field == "title" && item.HasTitle ? EditField.Title : EditField.Text);
    }

    static T? FindChild<T>(DependencyObject d) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var c = VisualTreeHelper.GetChild(d, i);
            if (c is T t) return t;
            if (FindChild<T>(c) is { } r) return r;
        }
        return null;
    }

    void StartDrag(BoardItemContainer grabbed, Point p, bool duplicate)
    {
        var items = Vm.SelectedItems.Where(i => !i.Locked).OrderBy(i => i.Z).ToList();
        // Alt + arrastar: arrasta uma cópia.
        if (duplicate)
        {
            var copies = Vm.DuplicateMany(items, default);
            items = copies;
            foreach (var it in copies) Realize(it);
            grabbed = _containers[copies[0]];
        }
        Vm.Checkpoint();
        var d = new DragState(grabbed, _camera.ToWorld(p), p);
        foreach (var it in items)
        {
            Realize(it);
            Vm.BringToFront(it);
            d.Parts.Add((_containers[it], it));
            d.Box.Union(it.Box);
        }
        _drag = d;
        if (!_testing) grabbed.CaptureMouse();
        ClearPorts();
        Kick();
    }

    void OnMouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(Surface);
        // Botão direito segurado num item e arrastado: vira uma linha nova até onde o mouse soltar.
        if (_rightStart is { } rs && _connecting is null)
        {
            if (e.RightButton != MouseButtonState.Pressed) _rightStart = null;
            else if ((p - rs.Pointer).Length >= 6)
            {
                _connecting = (rs.Item, NearestSide(rs.Item.Box, _camera.ToWorld(p)));
                _rightDrag = true;
                ClearPorts();
                Surface.CaptureMouse();
            }
        }
        if (_drag is { } d)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }
            d.Pointer = p;
            Kick();
            e.Handled = true;
        }
        else if (_handle is { } h)
        {
            h.Pointer = _camera.ToWorld(p);
            Kick();
            e.Handled = true;
        }
        else if (_connecting is not null)
        {
            UpdateConnector(p);
            e.Handled = true;
        }
        else if (_stroke is not null)
        {
            var w = _camera.ToWorld(p);
            if ((w - _stroke[^1]).Length * _camera.Zoom >= 1.5)
            {
                _stroke.Add(w);
                InkPreview.Data = DrawingView.Smooth(_stroke.SelectMany(q => new[] { q.X, q.Y }).ToList());
            }
            e.Handled = true;
        }
        else if (_erasing)
        {
            EraseAt(_camera.ToWorld(p));
            e.Handled = true;
        }
        else if (_lasso is not null)
        {
            _lasso.Add(p);
            Lasso.Points = new PointCollection(_lasso);
            e.Handled = true;
        }
        else if (_lineStart is not null)
        {
            UpdateLinePreview(p);
            e.Handled = true;
        }
        else if (_marquee)
        {
            UpdateMarquee(p);
            e.Handled = true;
        }
        else if (_panning)
        {
            _panPointer = p;
            long now = Stopwatch.GetTimestamp();
            _panSamples.Enqueue((now, _panPointer));
            while (_panSamples.Count > 2 && Stopwatch.GetElapsedTime(_panSamples.Peek().Ticks, now).TotalMilliseconds > 90)
                _panSamples.Dequeue();
            Kick();
            e.Handled = true;
        }
        else if (Tool == BoardTool.Select)
        {
            // Pontos de conexão aparecem ao passar o mouse sobre um item.
            var item = Tree.FindAncestor<BoardItemContainer>(e.OriginalSource as DependencyObject)?.Item;
            if (item is null && e.OriginalSource is FrameworkElement { Tag: PortTag }) return;
            if (item is { Kind: not ItemKind.Line } && item.Editing == EditField.None) ShowPorts(item);
            else ClearPorts();
        }
    }

    void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(Surface);
        if (_panning && (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Left))
        {
            EndPan(fling: true);
            e.Handled = true;
            return;
        }
        if (e.ChangedButton != MouseButton.Left) return;
        if (_marquee) { EndMarquee(); e.Handled = true; }
        else if (_handle is not null) { EndHandle(); e.Handled = true; }
        else if (_connecting is not null && !_rightDrag) { EndConnector(p); e.Handled = true; }
        else if (_stroke is not null) { EndStroke(); e.Handled = true; }
        else if (_erasing) { _erasing = false; ReleaseSurface(); e.Handled = true; }
        else if (_lasso is not null) { EndLasso(); e.Handled = true; }
        else if (_lineStart is not null) { EndLineTool(p); e.Handled = true; }
        else EndDrag();
    }

    void ReleaseSurface()
    {
        if (Surface.IsMouseCaptured) Surface.ReleaseMouseCapture();
    }

    void StartPan(Point p)
    {
        _camera.Stop();
        _panning = true;
        _panPointer = _panApplied = p;
        _panSamples.Clear();
        _panSamples.Enqueue((Stopwatch.GetTimestamp(), p));
        Surface.CaptureMouse();
        Surface.Cursor = BoardCursors.Grabbing;
        ClearPorts();
        Kick();
    }

    void EndPan(bool fling)
    {
        if (!_panning) return;
        _panning = false;
        if (fling && _panSamples.Count >= 2)
        {
            var (t0, p0) = _panSamples.Peek();
            var (t1, p1) = _panSamples.Last();
            double secs = Stopwatch.GetElapsedTime(t0, t1).TotalSeconds;
            bool recent = Stopwatch.GetElapsedTime(t1).TotalMilliseconds < 60;
            if (secs > 0.01 && recent) _camera.Fling((p1 - p0) / secs); // leve inércia
        }
        _panSamples.Clear();
        ReleaseSurface();
        RestoreCursor();
        Kick();
    }

    void EndDrag()
    {
        if (_drag is not { } d) return;
        _drag = null;
        Guides.Children.Clear();
        if (d.Grabbed.IsMouseCaptured) d.Grabbed.ReleaseMouseCapture();
        if (!d.Moved) return;
        var moving = new HashSet<int>(d.Parts.Select(p => p.Item.Id));
        foreach (var (c, it) in d.Parts)
        {
            if (it.Line is { } l)
            {
                // Linha arrastada: as pontas andam juntas e soltam dos itens que ficaram parados.
                foreach (var end in new[] { l.De, l.Para })
                {
                    if (end.Item is { } id && moving.Contains(id)) continue;
                    end.X += d.Offset.X;
                    end.Y += d.Offset.Y;
                    end.Item = null;
                    end.Lado = null;
                }
            }
            it.X = Math.Round(it.X + d.Offset.X);
            it.Y = Math.Round(it.Y + d.Offset.Y);
            c.DragOffset.X = c.DragOffset.Y = 0;
            c.SetDragging(false);
            if (it.Line is not null) it.Touch(nameof(BoardItemViewModel.Line));
        }
        RefreshLines(d.Moving, default);
        Vm.CommitMove();
        UpdateAdorner();
        Settle();
    }

    // ---------- guias inteligentes ----------

    void PrepareGuides(DragState d)
    {
        d.GuideX.Clear();
        d.GuideY.Clear();
        var moving = new HashSet<BoardItemViewModel>(d.Parts.Select(p => p.Item));
        var area = _camera.VisibleWorld;
        _query.Clear();
        _index.Query(area, _query);
        foreach (var it in _query)
        {
            if (moving.Contains(it) || it.Kind == ItemKind.Line) continue;
            var b = it.Box;
            d.GuideX.AddRange([b.Left, b.Left + b.Width / 2, b.Right]);
            d.GuideY.AddRange([b.Top, b.Top + b.Height / 2, b.Bottom]);
        }
    }

    /// <summary>Encaixa bordas e centros nos dos outros itens e desenha as guias amarelas.</summary>
    Vector Snap(DragState d, Vector off)
    {
        Guides.Children.Clear();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || d.Box.IsEmpty) return off;
        double tol = SnapPx / _camera.Zoom;
        var b = d.Box;
        b.Offset(off);
        var xs = new[] { b.Left, b.Left + b.Width / 2, b.Right };
        var ys = new[] { b.Top, b.Top + b.Height / 2, b.Bottom };
        (double Delta, double Line)? bx = null, by = null;
        foreach (var g in d.GuideX)
            foreach (var x in xs)
                if (Math.Abs(g - x) <= tol && (bx is null || Math.Abs(g - x) < Math.Abs(bx.Value.Delta))) bx = (g - x, g);
        foreach (var g in d.GuideY)
            foreach (var y in ys)
                if (Math.Abs(g - y) <= tol && (by is null || Math.Abs(g - y) < Math.Abs(by.Value.Delta))) by = (g - y, g);
        if (bx is { } sx)
        {
            off.X += sx.Delta;
            var top = _camera.ToScreen(new Point(sx.Line, b.Top - 4000)).Y;
            var a = _camera.ToScreen(new Point(sx.Line, 0)).X;
            Guides.Children.Add(new Line { X1 = a, X2 = a, Y1 = Math.Max(0, top), Y2 = _camera.Viewport.Height, Stroke = GuideBrush, StrokeThickness = 1 });
        }
        if (by is { } sy)
        {
            off.Y += sy.Delta;
            var a = _camera.ToScreen(new Point(0, sy.Line)).Y;
            Guides.Children.Add(new Line { X1 = 0, X2 = _camera.Viewport.Width, Y1 = a, Y2 = a, Stroke = GuideBrush, StrokeThickness = 1 });
        }
        return off;
    }

    // ---------- alças: tamanho, rotação e pontas de linha ----------

    sealed record HandleTag(BoardItemViewModel Item, int Index);
    sealed record PortTag(BoardItemViewModel Item, string Side);

    static readonly Cursor[] HandleCursors =
        [Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE, Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE];

    /// <summary>Desenha as 8 alças de tamanho do item selecionado ou as pontas da linha.</summary>
    public void UpdateAdorner()
    {
        if (_vm is null) return;
        var sel = _vm.SelectedItems.ToList();
        if (sel.Count != 1 || sel[0].Locked || sel[0].Editing != EditField.None || Tool != BoardTool.Select)
        {
            if (Adorner.Children.Count > 0) Adorner.Children.Clear();
            if (sel.Count > 1) DrawGroupBox(sel);
            return;
        }
        var it = sel[0];
        var offset = _drag?.Offset ?? default;
        if (Adorner.Children.Count != (it.Kind == ItemKind.Line ? 2 : 8) || Adorner.Tag != it) BuildAdorner(it);

        if (it.Kind == ItemKind.Line && it.Line is { } l)
        {
            Place(Adorner.Children[0], _camera.ToScreen(new Point(l.De.X, l.De.Y) + offset));
            Place(Adorner.Children[1], _camera.ToScreen(new Point(l.Para.X, l.Para.Y) + offset));
            return;
        }
        var box = it.Box;
        box.Offset(offset);
        var c = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
        var rot = new RotateTransform(it.Rotation, c.X, c.Y);
        Point S(double fx, double fy) => _camera.ToScreen(rot.Transform(new Point(box.X + box.Width * fx, box.Y + box.Height * fy)));
        var pts = new[] { S(0, 0), S(0.5, 0), S(1, 0), S(1, 0.5), S(1, 1), S(0.5, 1), S(0, 1), S(0, 0.5) };
        for (int i = 0; i < 8; i++) Place(Adorner.Children[i], pts[i]);
    }

    void DrawGroupBox(List<BoardItemViewModel> sel)
    {
        var r = Rect.Empty;
        var offset = _drag?.Offset ?? default;
        foreach (var i in sel) r.Union(i.Box);
        r.Offset(offset);
        var a = _camera.ToScreen(r.TopLeft);
        var b = _camera.ToScreen(r.BottomRight);
        var rect = new Rectangle
        {
            Width = Math.Max(1, b.X - a.X + 16), Height = Math.Max(1, b.Y - a.Y + 16), Stroke = AccentBrush, StrokeThickness = 1.5,
            StrokeDashArray = [4, 3], RadiusX = 8, RadiusY = 8, IsHitTestVisible = false,
        };
        Canvas.SetLeft(rect, a.X - 8);
        Canvas.SetTop(rect, a.Y - 8);
        Adorner.Children.Add(rect);
    }

    void BuildAdorner(BoardItemViewModel it)
    {
        Adorner.Children.Clear();
        Adorner.Tag = it;
        if (it.Kind == ItemKind.Line)
        {
            Adorner.Children.Add(Dot(new HandleTag(it, 9), 14, Cursors.SizeAll));
            Adorner.Children.Add(Dot(new HandleTag(it, 10), 14, Cursors.SizeAll));
            return;
        }
        // Só as 8 alças de tamanho: os itens ficam sempre retos (sem alça de girar).
        for (int i = 0; i < 8; i++) Adorner.Children.Add(Dot(new HandleTag(it, i), 11, HandleCursors[i]));
    }

    static Ellipse Dot(object tag, double size, Cursor cursor) => new()
    {
        Width = size, Height = size, Fill = Brushes.White, Stroke = AccentBrush, StrokeThickness = 2, Tag = tag, Cursor = cursor,
        Effect = null,
    };

    static void Place(UIElement el, Point center)
    {
        var fe = (FrameworkElement)el;
        Canvas.SetLeft(fe, center.X - fe.Width / 2);
        Canvas.SetTop(fe, center.Y - fe.Height / 2);
    }

    void StepHandle(HandleState h)
    {
        var it = h.Item;
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (h.Handle >= 9 && it.Line is { } l)
        {
            var end = h.Handle == 9 ? l.De : l.Para;
            end.X = h.Pointer.X;
            end.Y = h.Pointer.Y;
            end.Item = null;
            end.Lado = null;
            FitLineBox(it);
            return;
        }
        var box = h.Box;
        var c = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
        if (h.Handle == 8)
        {
            var v = h.Pointer - c;
            double angle = Math.Atan2(v.Y, v.X) * 180 / Math.PI + 90;
            if (shift) angle = Math.Round(angle / 15) * 15;
            it.Rotation = Math.Round(((angle % 360) + 540) % 360 - 180, 1);
            return;
        }
        // Converte o ponteiro para o sistema do item (sem rotação) e move só as bordas da alça.
        var inv = new RotateTransform(-h.Rotation, c.X, c.Y);
        var p = inv.Transform(h.Pointer);
        double l2 = box.Left, t = box.Top, r = box.Right, b = box.Bottom;
        int k = h.Handle;
        if (k is 0 or 6 or 7) l2 = Math.Min(p.X, r - 24);
        if (k is 2 or 3 or 4) r = Math.Max(p.X, l2 + 24);
        if (k is 0 or 1 or 2) t = Math.Min(p.Y, b - 24);
        if (k is 4 or 5 or 6) b = Math.Max(p.Y, t + 24);
        if (shift && k is 0 or 2 or 4 or 6 && box.Height > 0)
        {
            double ratio = box.Width / box.Height, w = r - l2, hh = b - t;
            if (w / hh > ratio) w = hh * ratio; else hh = w / ratio;
            if (k is 0 or 6) l2 = r - w; else r = l2 + w;
            if (k is 0 or 2) t = b - hh; else b = t + hh;
        }
        var nb = new Rect(new Point(l2, t), new Point(r, b));
        // O centro novo, de volta ao mundo (girado).
        var rot = new RotateTransform(h.Rotation, c.X, c.Y);
        var nc = rot.Transform(new Point(nb.X + nb.Width / 2, nb.Y + nb.Height / 2));
        if (it.Kind == ItemKind.Text)
        {
            // Texto solto: as alças mudam o tamanho da letra.
            var s = it.Style.Clone();
            s.Tamanho = Math.Round(Math.Clamp(h.FontSize * nb.Height / Math.Max(1, box.Height), 8, 400), 1);
            it.Style = s;
            it.X = Math.Round(nb.X);
            it.Y = Math.Round(nb.Y);
            return;
        }
        it.W = Math.Round(nb.Width);
        it.H = Math.Round(nb.Height);
        it.X = Math.Round(nc.X - nb.Width / 2);
        it.Y = Math.Round(nc.Y - nb.Height / 2);
    }

    void EndHandle()
    {
        if (_handle is { Handle: >= 9 } h && h.Item.Line is { } l)
        {
            // Soltou a ponta em cima de um item: prende nele.
            var end = h.Handle == 9 ? l.De : l.Para;
            if (ItemAt(_camera.ToScreen(h.Pointer), h.Item) is { } target)
            {
                end.Item = target.Id;
                end.Lado = NearestSide(target.Box, h.Pointer);
                RefreshLines([target], default);
            }
            h.Item.Touch(nameof(BoardItemViewModel.Line));
        }
        _handle = null;
        ReleaseSurface();
        Vm.CommitMove();
        UpdateAdorner();
    }

    // ---------- conectores ----------

    void ShowPorts(BoardItemViewModel item)
    {
        if (_drag is not null || _panning || Tool != BoardTool.Select) return;
        _hoverItem = item;
        if (Ports.Tag != item || Ports.Children.Count != 4)
        {
            Ports.Children.Clear();
            Ports.Tag = item;
            foreach (var side in new[] { "cima", "direita", "baixo", "esquerda" })
            {
                var dot = new Ellipse
                {
                    Width = 14, Height = 14, Fill = AccentBrush, Stroke = Brushes.White, StrokeThickness = 2.5,
                    Tag = new PortTag(item, side), Cursor = Cursors.Cross, ToolTip = "Arraste para ligar (no vazio: cria uma cópia ligada)",
                };
                Ports.Children.Add(dot);
            }
        }
        var b = item.Box;
        Place(Ports.Children[0], _camera.ToScreen(new Point(b.X + b.Width / 2, b.Y)) + new Vector(0, -16));
        Place(Ports.Children[1], _camera.ToScreen(new Point(b.Right, b.Y + b.Height / 2)) + new Vector(16, 0));
        Place(Ports.Children[2], _camera.ToScreen(new Point(b.X + b.Width / 2, b.Bottom)) + new Vector(0, 16));
        Place(Ports.Children[3], _camera.ToScreen(new Point(b.X, b.Y + b.Height / 2)) + new Vector(-16, 0));
    }

    void ClearPorts()
    {
        _hoverItem = null;
        if (Ports.Children.Count > 0) Ports.Children.Clear();
        Ports.Tag = null;
    }

    static Point SidePoint(Rect b, string? side) => side switch
    {
        "cima" => new Point(b.X + b.Width / 2, b.Y),
        "baixo" => new Point(b.X + b.Width / 2, b.Bottom),
        "esquerda" => new Point(b.X, b.Y + b.Height / 2),
        "direita" => new Point(b.Right, b.Y + b.Height / 2),
        _ => new Point(b.X + b.Width / 2, b.Y + b.Height / 2),
    };

    static string NearestSide(Rect b, Point p)
    {
        var sides = new[] { "cima", "direita", "baixo", "esquerda" };
        return sides.OrderBy(s => (SidePoint(b, s) - p).LengthSquared).First();
    }

    void UpdateConnector(Point p)
    {
        if (_connecting is not { } c) return;
        var a = _camera.ToScreen(SidePoint(c.From.Box, c.Side));
        ConnectorPreview.X1 = a.X; ConnectorPreview.Y1 = a.Y;
        ConnectorPreview.X2 = p.X; ConnectorPreview.Y2 = p.Y;
        ConnectorPreview.Visibility = Visibility.Visible;
    }

    void EndConnector(Point p, bool copyOnEmpty = true)
    {
        if (_connecting is not { } c) return;
        _connecting = null;
        ConnectorPreview.Visibility = Visibility.Collapsed;
        ReleaseSurface();
        var world = _camera.ToWorld(p);
        if ((p - _camera.ToScreen(SidePoint(c.From.Box, c.Side))).Length < 12) return; // só clicou
        var target = ItemAt(p, c.From);
        if (target is null)
        {
            if (!copyOnEmpty) return;
            // Soltou no vazio: cria uma cópia do item já ligada.
            var copy = Vm.DuplicateMany([c.From], default, select: false)[0];
            var size = c.From.Box.Size;
            copy.X = Math.Round(world.X - size.Width / 2);
            copy.Y = Math.Round(world.Y - size.Height / 2);
            target = copy;
        }
        AddConnector(c.From, c.Side, target, NearestSide(target.Box, SidePoint(c.From.Box, c.Side)));
    }

    /// <summary>Cria uma linha presa a dois itens.</summary>
    public BoardItemViewModel AddConnector(BoardItemViewModel a, string sideA, BoardItemViewModel b, string sideB)
    {
        var pa = SidePoint(a.Box, sideA);
        var pb = SidePoint(b.Box, sideB);
        var line = Vm.AddItem(new BoardItem
        {
            Tipo = "linha",
            Cor = "branco",
            Linha = new LineData
            {
                De = new LineEnd { X = pa.X, Y = pa.Y, Item = a.Id, Lado = sideA },
                Para = new LineEnd { X = pb.X, Y = pb.Y, Item = b.Id, Lado = sideB, Ponta = "seta" },
                Tipo = DrawSettings.LineType,
            },
            Fixacao = "nenhuma",
        });
        FitLineBox(line);
        if (!Vm.DiagramMode) Vm.DiagramMode = true; // com o modo diagrama desligado o conector ficaria invisível
        return line;
    }

    /// <summary>Recalcula a caixa (X, Y) da linha a partir das pontas.</summary>
    static void FitLineBox(BoardItemViewModel line)
    {
        if (line.Line is not { } l) return;
        line.X = Math.Min(l.De.X, l.Para.X) - LineView.Pad;
        line.Y = Math.Min(l.De.Y, l.Para.Y) - LineView.Pad;
        line.Touch(nameof(BoardItemViewModel.Line));
    }

    // Só as linhas do quadro (refeita quando itens entram ou saem): arrastar não percorre todos os itens.
    List<BoardItemViewModel>? _lines;
    List<BoardItemViewModel> Lines => _lines ??= _vm is null ? [] : _vm.Items.Where(i => i.Kind == ItemKind.Line).ToList();

    static Dictionary<int, BoardItemViewModel> MovedById(IEnumerable<BoardItemViewModel> moved)
    {
        var ids = new Dictionary<int, BoardItemViewModel>();
        foreach (var m in moved)
            if (m.Kind != ItemKind.Line) ids[m.Id] = m;
        return ids;
    }

    void RefreshLines(BoardItemViewModel moved) => RefreshLines(MovedById([moved]), default);

    void RefreshLines(IEnumerable<BoardItemViewModel> moved, Vector offset) => RefreshLines(MovedById(moved), offset);

    /// <summary>Linhas presas aos itens que se moveram acompanham (offset = deslocamento do arraste).</summary>
    void RefreshLines(Dictionary<int, BoardItemViewModel> ids, Vector offset)
    {
        if (_vm is null || ids.Count == 0) return;
        foreach (var line in Lines)
        {
            if (line.Line is not { } l) continue;
            bool changed = MoveEnd(l.De, ids, offset) | MoveEnd(l.Para, ids, offset);
            if (changed) FitLineBox(line);
        }
    }

    static bool MoveEnd(LineEnd end, Dictionary<int, BoardItemViewModel> ids, Vector offset)
    {
        if (end.Item is not { } id || !ids.TryGetValue(id, out var target)) return false;
        var box = target.Box;
        box.Offset(offset);
        var p = SidePoint(box, end.Lado);
        if (p.X == end.X && p.Y == end.Y) return false;
        end.X = p.X;
        end.Y = p.Y;
        return true;
    }

    BoardItemViewModel? ItemAt(Point screen, BoardItemViewModel? except)
    {
        BoardItemViewModel? found = null;
        VisualTreeHelper.HitTest(ItemsLayer, null, r =>
        {
            var it = Tree.FindAncestor<BoardItemContainer>(r.VisualHit)?.Item;
            if (it is not null && it != except && it.Kind != ItemKind.Line) { found = it; return HitTestResultBehavior.Stop; }
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(Surface.TranslatePoint(screen, ItemsLayer)));
        return found;
    }

    // ---------- desenho à mão, borracha, laço e linha ----------

    void EndStroke()
    {
        var pts = _stroke!;
        _stroke = null;
        InkPreview.Data = null;
        ReleaseSurface();
        if (pts.Count == 0) return;
        double pad = DrawSettings.Thickness * 2 + 2;
        double minX = pts.Min(p => p.X) - pad, minY = pts.Min(p => p.Y) - pad;
        var stroke = DrawSettings.Stroke(Tool, pts.SelectMany(p => new[] { Math.Round(p.X - minX, 1), Math.Round(p.Y - minY, 1) }).ToList());
        Vm.AddItem(new BoardItem { Tipo = "desenho", X = minX, Y = minY, Tracos = [stroke], Fixacao = "nenhuma" }, select: false);
    }

    void EraseAt(Point world)
    {
        double r = 8 / _camera.Zoom;
        _query.Clear();
        _index.Query(new Rect(world.X - r, world.Y - r, 2 * r, 2 * r), _query);
        foreach (var it in _query.Where(i => i.Kind == ItemKind.Drawing).ToList())
        {
            if (it.Strokes is null) continue;
            var local = new Point((world.X - it.X) / it.DrawScaleX, (world.Y - it.Y) / it.DrawScaleY);
            foreach (var s in it.Strokes)
            {
                var g = DrawingView.Smooth(s.Pontos);
                if (g.StrokeContains(new Pen(Brushes.Black, s.Espessura * 3 + r * 2), local))
                {
                    Vm.Items.Remove(it);
                    Vm.CommitMove();
                    break;
                }
            }
        }
    }

    void EndLasso()
    {
        var pts = _lasso!;
        _lasso = null;
        Lasso.Visibility = Visibility.Collapsed;
        ReleaseSurface();
        if (pts.Count < 3) return;
        var poly = new StreamGeometry();
        using (var c = poly.Open())
        {
            c.BeginFigure(_camera.ToWorld(pts[0]), true, true);
            foreach (var p in pts.Skip(1)) c.LineTo(_camera.ToWorld(p), true, false);
        }
        var hit = Vm.Items.Where(i =>
        {
            var b = i.Box;
            return poly.FillContains(new Point(b.X + b.Width / 2, b.Y + b.Height / 2));
        }).ToList();
        Vm.SelectMany(hit, add: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    void UpdateLinePreview(Point p)
    {
        if (_lineStart is not { } s) return;
        var a = _camera.ToScreen(s);
        ConnectorPreview.X1 = a.X; ConnectorPreview.Y1 = a.Y;
        ConnectorPreview.X2 = p.X; ConnectorPreview.Y2 = p.Y;
        ConnectorPreview.Visibility = Visibility.Visible;
    }

    void EndLineTool(Point p)
    {
        var start = _lineStart!.Value;
        _lineStart = null;
        ConnectorPreview.Visibility = Visibility.Collapsed;
        ReleaseSurface();
        var end = _camera.ToWorld(p);
        if ((end - start).Length * _camera.Zoom < 6) end = start + new Vector(160, 0);
        var a = ItemAt(_camera.ToScreen(start), null);
        var b = ItemAt(p, a);
        var data = new LineData
        {
            De = new LineEnd { X = start.X, Y = start.Y, Item = a?.Id, Lado = a is null ? null : NearestSide(a.Box, start) },
            Para = new LineEnd { X = end.X, Y = end.Y, Item = b?.Id, Lado = b is null ? null : NearestSide(b.Box, end), Ponta = DrawSettings.ArrowEnd ? "seta" : "nenhuma" },
            Tipo = DrawSettings.LineType,
        };
        var line = Vm.AddItem(new BoardItem { Tipo = "linha", Linha = data, Fixacao = "nenhuma" });
        if (a is not null) RefreshLines([a], default);
        if (b is not null) RefreshLines([b], default);
        FitLineBox(line);
        if ((a ?? b) is not null && !Vm.DiagramMode) Vm.DiagramMode = true;
        Tool = BoardTool.Select;
    }

    // ---------- seleção em caixa ----------

    void StartMarquee(Point p)
    {
        _marquee = true;
        _marqueeStart = p;
        _marqueeBase = Vm.SelectedItems.ToHashSet();
        Surface.CaptureMouse();
        UpdateMarquee(p);
        Marquee.Visibility = Visibility.Visible;
    }

    void UpdateMarquee(Point p)
    {
        var r = new Rect(_marqueeStart, p);
        Canvas.SetLeft(Marquee, r.X);
        Canvas.SetTop(Marquee, r.Y);
        Marquee.Width = r.Width;
        Marquee.Height = r.Height;
        var world = new Rect(_camera.ToWorld(r.TopLeft), _camera.ToWorld(r.BottomRight));
        _query.Clear();
        _index.Query(world, _query);
        var hit = new HashSet<BoardItemViewModel>(_marqueeBase);
        foreach (var it in _query)
            if (it.Box.IntersectsWith(world)) hit.Add(it);
        Vm.SelectMany(hit, add: false);
    }

    void EndMarquee()
    {
        _marquee = false;
        Marquee.Visibility = Visibility.Collapsed;
        ReleaseSurface();
    }

    // ---------- botão direito ----------

    // Arraste com o botão direito a partir de um item (cria uma linha até o item onde soltar).
    (BoardItemViewModel Item, Point Pointer)? _rightStart;
    bool _rightDrag;

    void OnRightDown(object sender, MouseButtonEventArgs e)
    {
        _rightStart = null;
        if (Tool != BoardTool.Select || _drag is not null || _connecting is not null) return;
        var item = Tree.FindAncestor<BoardItemContainer>(e.OriginalSource as DependencyObject)?.Item;
        if (item is { Kind: not ItemKind.Line } && item.Editing == EditField.None) _rightStart = (item, e.GetPosition(Surface));
    }

    void OnRightUp(object sender, MouseButtonEventArgs e)
    {
        _rightStart = null;
        if (_rightDrag)
        {
            _rightDrag = false;
            EndConnector(e.GetPosition(Surface), copyOnEmpty: false); // soltou no vazio: não cria nada
            e.Handled = true;
            return;
        }
        var src = e.OriginalSource as DependencyObject;
        if (Tree.FindAncestor<TextBox>(src) is not null) return;
        var root = Window.GetWindow(this)?.Content as IInputElement;
        var windowPoint = root is null ? default : e.GetPosition(root);
        Vm.EndEdit();
        if (Tool != BoardTool.Select) Tool = BoardTool.Select;
        if (Tree.FindAncestor<BoardItemContainer>(src)?.Item is { } item)
        {
            if (!item.IsSelected) Vm.SelectWithGroup(item);
            MenuRequested?.Invoke(item, windowPoint);
        }
        else TrayRequested?.Invoke(_camera.ToWorld(e.GetPosition(Surface)), windowPoint);
        e.Handled = true;
    }

    // ---------- exportar ----------

    /// <summary>Desenha uma área do quadro (todo ou só a seleção) numa imagem, com a superfície por baixo.</summary>
    public BitmapSource RenderRegion(IReadOnlyCollection<BoardItemViewModel> items, double scale, bool onlyThese)
    {
        var area = items.Aggregate(Rect.Empty, (r, i) => { r.Union(i.Bounds); return r; });
        if (area.IsEmpty) area = _camera.VisibleWorld;
        area.Inflate(40, 40);
        double maxSide = 8000;
        scale = Math.Min(scale, maxSide / Math.Max(area.Width, area.Height));
        foreach (var it in items) Realize(it);
        var hidden = new List<BoardItemContainer>();
        if (onlyThese)
            foreach (var (vm, c) in _containers)
                if (!items.Contains(vm) && c.Visibility == Visibility.Visible) { c.Visibility = Visibility.Hidden; hidden.Add(c); }
        var cacheBackup = _itemCache.RenderAtScale;
        UpdateLayout();

        int w = (int)Math.Ceiling(area.Width * scale), h = (int)Math.Ceiling(area.Height * scale);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushTransform(new TranslateTransform(-area.X, -area.Y));
            dc.DrawRectangle((Brush)FindResource("CanvasBrush"), null, area); // fundo liso do quadro
            dc.DrawRectangle(new VisualBrush(ItemsLayer) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = area, Stretch = Stretch.Fill }, null, area);
            dc.Pop();
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        foreach (var c in hidden) c.Visibility = Visibility.Visible;
        _itemCache.RenderAtScale = cacheBackup;
        return rtb;
    }

    // ---------- testes (desenvolvimento) ----------

    internal int PeakRealized;

    internal async Task<string> TestDragAsync(BoardItemViewModel item, double dx, double dy)
    {
        Vm.Selected = item;
        Realize(item);
        var c = _containers[item];
        var start = _camera.ToScreen(new Point(item.X + 20, item.Y + 20));
        double x0 = item.X, y0 = item.Y;
        _testing = true;
        StartDrag(c, start, duplicate: false);
        for (int i = 1; i <= 10; i++)
        {
            _drag!.Pointer = start + new Vector(dx * i / 10, dy * i / 10);
            await Task.Delay(20);
        }
        string mid = $"durante: X={item.X} offset={c.DragOffset.X:0},{c.DragOffset.Y:0}";
        EndDrag();
        _testing = false;
        return $"{mid} | depois: X {x0}->{item.X}, Y {y0}->{item.Y}, offset={c.DragOffset.X},{c.DragOffset.Y}";
    }

    internal async Task<List<double>> BenchmarkAsync(double seconds)
    {
        var frames = new List<double>();
        TimeSpan last = TimeSpan.Zero;
        double cx = _camera.Cx, cy = _camera.Cy, z0 = _camera.Zoom;
        void Frame(object? s, EventArgs e)
        {
            var t = ((RenderingEventArgs)e).RenderingTime;
            if (t == last) return;
            if (last != TimeSpan.Zero) frames.Add((t - last).TotalMilliseconds);
            last = t;
            double a = frames.Count / 40.0;
            _camera.Zoom = BoardCamera.Clamp(z0 * (2.5 + 1.5 * Math.Sin(a * 0.7)));
            _camera.Cx = cx + Math.Cos(a) * 1500;
            _camera.Cy = cy + Math.Sin(a) * 900;
            ApplyCamera();
            if (_pending.Count > 0) CreateSome();
            PeakRealized = Math.Max(PeakRealized, _containers.Count);
        }
        CompositionTarget.Rendering += Frame;
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        CompositionTarget.Rendering -= Frame;
        return frames;
    }
}

/// <summary>Configuração da paleta de desenho e das linhas (compartilhada).</summary>
public static class DrawSettings
{
    public static Color Color = Color.FromRgb(0x1D, 0x21, 0x30);
    public static double Thickness = 4;
    public static string LineType = "reta";
    public static bool ArrowEnd = true;

    public static InkStroke Stroke(BoardTool tool, List<double> points) => new()
    {
        Ferramenta = tool switch { BoardTool.Pencil => "lapis", BoardTool.Marker => "marcador", _ => "caneta" },
        Cor = Color.ToString(),
        Espessura = Thickness,
        Pontos = points,
    };
}
