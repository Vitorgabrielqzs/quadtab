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
    static readonly Brush GuideBrush = ShadowCache.BrushOf(Color.FromRgb(0x6E, 0x73, 0x7C)); // guias em cinza forte: aparecem bem no quadro claro e no escuro
    const double AttachReachPx = 40;  // distância (tela) em que a bolinha de anexar aparece
    static readonly Brush AttachFill = ShadowCache.BrushOf(Color.FromArgb(0xB3, 0x0A, 0x84, 0xFF));
    static readonly Brush AttachBrush = ShadowCache.BrushOf(Color.FromRgb(0x0A, 0x84, 0xFF));
    static readonly Brush GuideFill = ShadowCache.BrushOf(Color.FromArgb(0x38, 0x6E, 0x73, 0x7C));
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
        public Rect Box = Rect.Empty;            // caixa de tudo o que se move (antes do deslocamento)
        public List<Rect> Others = [];           // caixas dos itens parados, para as guias
        public List<BoardItemViewModel> Targets = []; // itens parados onde dá para anexar
        public BoardItemViewModel? AttachTarget;
        public string AttachSide = "baixo";
        public readonly GapHold HoldX = new(), HoldY = new();
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
        // Ponta de linha: onde estava presa. Solta no vazio, volta para lá.
        public readonly int? EndItem = handle == 9 ? item.Line?.De.Item : handle == 10 ? item.Line?.Para.Item : null;
        public readonly string? EndSide = handle == 9 ? item.Line?.De.Lado : handle == 10 ? item.Line?.Para.Lado : null;
    }

    readonly BoardCamera _camera = new();
    readonly QuadTree<BoardItemViewModel> _index = new();
    readonly Dictionary<BoardItemViewModel, BoardItemContainer> _containers = new();
    readonly List<BoardItemViewModel> _query = [];
    const int MaxPooled = 24;
    readonly List<BoardItemViewModel> _pending = [];
    readonly Stack<BoardItemContainer>[] _pool = Enumerable.Range(0, 13).Select(_ => new Stack<BoardItemContainer>()).ToArray();
    readonly BitmapCache _itemCache = new(1) { EnableClearType = false, SnapsToDevicePixels = false };

    // Imagem do item com o dobro de pixels da tela: em posição "quebrada" ela é reamostrada, e com 1× o texto borra.
    const double GapHoldMs = 500;
    readonly DispatcherTimer _holdTimer = new() { Interval = TimeSpan.FromMilliseconds(GapHoldMs + 20) };
    double ItemCacheScale =>Math.Clamp(_camera.Zoom * _dpi * 2, 0.2, 6);
    const double MaxExportSide = 16000, MaxExportPixels = 100_000_000; // imagem de até 100 megapixels
    readonly DispatcherTimer _idle;
    readonly Stopwatch _work = new();

    MainViewModel? _vm;
    Rect _realizedArea = Rect.Empty;
    bool _ticking, _cameraReady, _autoFit;
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
        _holdTimer.Tick += (_, _) => { _holdTimer.Stop(); Kick(); };
        Surface.PreviewMouseDown += OnMouseDown;
        Surface.PreviewMouseMove += OnMouseMove;
        Surface.PreviewMouseUp += OnMouseUp;
        Surface.PreviewMouseRightButtonDown += OnRightDown;
        Surface.PreviewMouseRightButtonUp += OnRightUp;
        Surface.LostMouseCapture += (_, _) =>
        {
            if (_panning) EndPan(fling: false);
            if (_marquee) EndMarquee();
        };
        Surface.Cursor = BoardCursors.Arrow;
        Surface.AddHandler(Mouse.LostMouseCaptureEvent, new MouseEventHandler((_, _) =>
        {
            if (_drag is not null && !_drag.Grabbed.IsMouseCaptured) EndDrag();
        }));

        // Arrastar arquivos do Windows para o quadro.
        Surface.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Surface.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && _vm is { ReadOnly: false })
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
                if (_drag is null) RefreshLines(it);
                if (it.IsSelected) UpdateAdorner();
                break;
            case nameof(BoardItemViewModel.IsSelected):
            case nameof(BoardItemViewModel.Editing):
                if (it.IsSelected || it.Editing != EditField.None) Realize(it); // nunca some enquanto está em uso
                if (_containers.TryGetValue(it, out var overviewHost)) ApplyOverview(it, overviewHost);
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
        _itemCache.RenderAtScale = ItemCacheScale;
        BoardSurfaces.Update(Vm.Model, _camera.Zoom);
        ApplyCamera();
        Settle();
    }

    // ---------- zoom bem afastado: sÃ³ os tÃ­tulos ----------

    bool _overview;
    double _overviewZoom = 1;

    static bool HasOverview(BoardItemViewModel v) => v.Kind is ItemKind.PostIt or ItemKind.Page or ItemKind.Block or ItemKind.TextBox;

    /// <summary>Liga abaixo de 34% e sÃ³ desliga acima de 42%, para nÃ£o piscar no limite.</summary>
    void UpdateOverview(double zoom)
    {
        bool on = _overview ? zoom < 0.42 : zoom < 0.34;
        if (on == _overview && (!on || Math.Abs(zoom / _overviewZoom - 1) < 0.03)) return;
        _overview = on;
        _overviewZoom = zoom;
        foreach (var (vm, c) in _containers) ApplyOverview(vm, c);
    }

    void ApplyOverview(BoardItemViewModel vm, BoardItemContainer c)
    {
        bool show = _overview && HasOverview(vm) && vm.Editing == EditField.None;
        if (c.IsOverview != show) c.IsOverview = show;
        if (!show) return;
        // Letra de 9 a 15 px na tela: item pequeno ganha tÃ­tulo menor, para caber mais palavras.
        double target = Math.Clamp(0.2 * vm.MeasuredSize.Width * _overviewZoom, 9, 15);
        double scale = Math.Clamp(target / (16 * _overviewZoom), 1, 8);
        if (Math.Abs(c.OverviewScale - scale) > 0.001) c.OverviewScale = scale;
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
        UpdateAttachTarget(d, off);
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
        UpdateOverview(z);
        SurfaceBg.SetCamera(tx, ty, z);
        UpdateGlass();
        UpdateAdorner();
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
        double scale = ItemCacheScale;
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
            // Reaproveitado no mesmo tamanho não avisa: guarda o tamanho e acerta o espaço dos anexos.
            if (c.ActualWidth > 0) { vm.Width = c.ActualWidth; vm.Height = c.ActualHeight; }
            Vm.OnItemResized(vm);
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
                RefreshLines(item);
                Vm.OnItemResized(item);
            };
            ItemsLayer.Children.Add(c);
        }
        c.Attach(vm);
        _containers[vm] = c;
        ApplyOverview(vm, c);
        ApplyLineVisibility(vm);
    }

    void Unrealize(BoardItemViewModel vm)
    {
        if (!_containers.Remove(vm, out var c)) return;
        c.Detach();
        c.Visibility = Visibility.Collapsed;
        // Mídia guardada solta a imagem ou o vídeo; a reserva de cada tipo tem limite (o resto é descartado).
        var pool = _pool[(int)vm.Kind];
        bool keep = pool.Count < MaxPooled;
        if (!keep || vm.Kind is ItemKind.Image or ItemKind.Video or ItemKind.File or ItemKind.Link) c.Content = null;
        if (keep) pool.Push(c);
        else ItemsLayer.Children.Remove(c);
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
        if (Tree.FindAncestor<System.Windows.Controls.Primitives.TextBoxBase>(src) is not null) return; // clique dentro do campo em edição
        // Somente visualização: arrastar só move a câmera; 2 cliques só abrem links e arquivos.
        if (Vm.ReadOnly)
        {
            var seen = Tree.FindAncestor<BoardItemContainer>(src)?.Item;
            if (e.ClickCount == 2 && seen is { Kind: ItemKind.Link or ItemKind.File or ItemKind.Video })
                OpenOrEdit(seen, Tree.FindAncestor<BoardItemContainer>(src)!, src, default);
            else StartPan(p);
            e.Handled = true;
            return;
        }
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
        // + da tabela: clicar ou arrastar
        if (src is FrameworkElement { Tag: TableView.AddTag add })
        {
            StartTableDrag(add, world);
            Surface.CaptureMouse();
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
            else if (!shift && item.Line is { } ln && (ln.De.Item is not null || ln.Para.Item is not null))
            {
                // Linha ligada: clicar não a solta dos itens. Curva/ângulo: arrastar muda a dobra.
                if (ln.Tipo is "curva" or "angulo")
                {
                    Vm.Checkpoint();
                    _handle = new HandleState(item, 11, world);
                    Surface.CaptureMouse();
                    Kick();
                }
            }
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
            case ItemKind.Link when item.Url is { } url:
                ItemViewBase.OpenExternal(url);
                return;
            case ItemKind.File or ItemKind.Video when item.FileFullPath is { } file:
                ItemViewBase.OpenExternal(file);
                return;
            case ItemKind.Image or ItemKind.Drawing:
                return;
            case ItemKind.Line:
                return; // nomes nas linhas desligados por enquanto
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
        // Os anexos andam junto com o item principal.
        foreach (var it in items.ToList())
            foreach (var c in Vm.AttachedTree(it))
                if (!items.Contains(c)) items.Add(c);
        // Seta ligada não anda com o arraste: as pontas seguem os itens e nunca se soltam.
        items.RemoveAll(i => i.Line is { } l && (l.De.Item is not null || l.Para.Item is not null));
        if (items.Count == 0) return;
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
        grabbed.CaptureMouse();
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
                _connecting = (rs.Item, SideFor(rs.Item, _camera.ToWorld(p)));
                _rightDrag = true;
                Surface.CaptureMouse();
            }
        }
        if (_tableDrag is not null)
        {
            if (e.LeftButton != MouseButtonState.Pressed) EndTableDrag();
            else UpdateTableDrag(_camera.ToWorld(p));
            e.Handled = true;
        }
        else if (_drag is { } d)
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
        if (_tableDrag is not null) { EndTableDrag(); e.Handled = true; }
        else if (_marquee) { EndMarquee(); e.Handled = true; }
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
                // Só linha sem nada preso entra no arraste (ver StartDrag): as duas pontas andam juntas.
                foreach (var end in new[] { l.De, l.Para })
                {
                    end.X += d.Offset.X;
                    end.Y += d.Offset.Y;
                }
            }
            it.X = Math.Round(it.X + d.Offset.X);
            it.Y = Math.Round(it.Y + d.Offset.Y);
            c.DragOffset.X = c.DragOffset.Y = 0;
            c.SetDragging(false);
            if (it.Line is not null) it.Touch(nameof(BoardItemViewModel.Line));
        }
        // Arrastado para fora do principal, o anexo se solta; perto de um item, anexa.
        foreach (var (_, it) in d.Parts)
            if (it.AttachedTo is { } parent && !moving.Contains(parent)) Vm.Detach(it);
        foreach (var (_, it) in d.Parts) Vm.LayoutAttached(it); // os que andaram junto ficam encostados, sem somar o arraste duas vezes
        if (d.AttachTarget is { } target)
        {
            Vm.Attach(d.Grabbed.Item!, target, d.AttachSide);
            d.Offset = default;
        }
        RefreshLines(d.Moving, default);
        Vm.CommitMove();
        UpdateAdorner();
        Settle();
    }

    // ---------- anexar ----------

    static bool CanAttach(BoardItemViewModel i) => i.Kind is not (ItemKind.Line or ItemKind.Drawing or ItemKind.Text);

    /// <summary>Só o que tem papel ou fundo recebe anexos dentro. Um anexo não recebe outro: vai para o principal dele.</summary>
    static bool CanReceive(BoardItemViewModel i) => i.AttachedTo is null && i.Kind is ItemKind.Page or ItemKind.PostIt or ItemKind.Block or ItemKind.TextBox;

    /// <summary>Arrastando um item perto de outro: escolhe onde anexar (cima ou baixo) e mostra a bolinha.</summary>
    void UpdateAttachTarget(DragState d, Vector off)
    {
        d.AttachTarget = null;
        if (d.Grabbed.Item is not { } moving || !CanAttach(moving) || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        // Só um item por vez (com os anexos dele).
        var family = Vm.AttachedTree(moving);
        if (d.Parts.Count != family.Count + 1) return;

        var b = moving.Box;
        b.Offset(off);
        var pointer = _camera.ToWorld(d.Pointer);
        double reach = AttachReachPx / _camera.Zoom, best = double.MaxValue;
        foreach (var t in d.Targets)
        {
            if (!CanReceive(t)) continue;
            var box = t.Box;
            if (b.Right <= box.Left || b.Left >= box.Right) continue;
            double above = Math.Abs(b.Bottom - box.Top), below = Math.Abs(b.Top - box.Bottom);
            string side = below <= above ? "baixo" : "cima";
            double dist = Math.Min(above, below);
            // Com o ponteiro em cima do item, vale a metade onde ele está.
            if (box.Contains(pointer))
            {
                side = pointer.Y >= box.Top + box.Height / 2 ? "baixo" : "cima";
                dist = 0;
            }
            if (dist > reach || dist >= best) continue;
            best = dist;
            d.AttachTarget = t;
            d.AttachSide = side;
        }
        if (d.AttachTarget is not { } target) return;
        Guides.Children.Clear(); // perto de anexar, só a bolinha aparece
        var edge = target.Box;
        double y = d.AttachSide == "baixo" ? edge.Bottom : edge.Top;
        var center = _camera.ToScreen(new Point(edge.Left + edge.Width / 2, y));
        var a = _camera.ToScreen(new Point(edge.Left, y));
        var z = _camera.ToScreen(new Point(edge.Right, y));
        Guides.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = z.X, Y2 = z.Y, Stroke = AttachBrush, StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false });
        var bubble = AttachBubble();
        bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(bubble, center.X - bubble.DesiredSize.Width / 2);
        Canvas.SetTop(bubble, center.Y - bubble.DesiredSize.Height / 2);
        Guides.Children.Add(bubble);
    }

    /// <summary>"Anexar" e, logo abaixo, um + numa bolinha semitransparente.</summary>
    static FrameworkElement AttachBubble()
    {
        var font = (FontFamily)Application.Current.FindResource("Inter");
        var stack = new StackPanel { IsHitTestVisible = false };
        stack.Children.Add(new Border
        {
            Background = AttachFill,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 3, 10, 3),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock { Text = "Anexar", FontFamily = font, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White },
        });
        stack.Children.Add(new Border
        {
            Width = 34,
            Height = 34,
            Margin = new Thickness(0, 4, 0, 0),
            CornerRadius = new CornerRadius(17),
            Background = AttachFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock { Text = "+", FontFamily = font, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -3, 0, 0) },
        });
        return stack;
    }

    // ---------- guias inteligentes ----------

    void PrepareGuides(DragState d)
    {
        d.Others.Clear();
        d.Targets.Clear();
        var moving = new HashSet<BoardItemViewModel>(d.Parts.Select(p => p.Item));
        var area = _camera.VisibleWorld;
        _query.Clear();
        _index.Query(area, _query);
        foreach (var it in _query)
        {
            if (moving.Contains(it) || it.Kind == ItemKind.Line) continue;
            d.Others.Add(it.Box);
            if (CanAttach(it)) d.Targets.Add(it);
        }
    }

    // As contas valem para os dois eixos: "ao longo" (a) e "de través" (c).
    static double Lo(Rect r, bool h) => h ? r.Left : r.Top;
    static double Hi(Rect r, bool h) => h ? r.Right : r.Bottom;
    static double CrossLo(Rect r, bool h) => h ? r.Top : r.Left;
    static double CrossHi(Rect r, bool h) => h ? r.Bottom : r.Right;

    /// <summary>Itens na mesma faixa do item arrastado (mesma linha ou coluna), em ordem.</summary>
    static List<Rect> Row(Rect b, List<Rect> others, bool h) =>
        others.Where(r => CrossLo(r, h) < CrossHi(b, h) && CrossHi(r, h) > CrossLo(b, h)).OrderBy(r => Lo(r, h)).ToList();

    /// <summary>Quanto mover para encaixar: bordas e centros dos outros, ou o mesmo espaço que já existe entre eles.</summary>
    static double SnapDelta(Rect b, List<Rect> others, bool h, double tol)
    {
        double best = 0, bestDist = double.MaxValue;
        void Try(double delta)
        {
            if (Math.Abs(delta) <= tol && Math.Abs(delta) < bestDist) { best = delta; bestDist = Math.Abs(delta); }
        }
        double lo = Lo(b, h), hi = Hi(b, h), size = hi - lo, mid = lo + size / 2;
        foreach (var r in others)
            foreach (var g in new[] { Lo(r, h), (Lo(r, h) + Hi(r, h)) / 2, Hi(r, h) })
            {
                Try(g - lo);
                Try(g - mid);
                Try(g - hi);
            }
        var row = Row(b, others, h);
        for (int i = 0; i + 1 < row.Count; i++)
        {
            double gap = Lo(row[i + 1], h) - Hi(row[i], h);
            if (gap <= 0) continue;
            Try(Hi(row[i + 1], h) + gap - lo);         // depois do par, com o mesmo espaço
            Try(Lo(row[i], h) - gap - hi);             // antes do par, com o mesmo espaço
            if (gap > size) Try(Hi(row[i], h) + (gap - size) / 2 - lo); // no meio do par
        }
        return best;
    }

    /// <summary>Encaixa nos outros itens e desenha as guias: alinhamentos, distâncias em px e espaços iguais.</summary>
    Vector Snap(DragState d, Vector off)
    {
        Guides.Children.Clear();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || d.Box.IsEmpty) return off;
        double tol = SnapPx / _camera.Zoom;
        var b = d.Box;
        b.Offset(off);
        var snap = new Vector(SnapDelta(b, d.Others, true, tol), SnapDelta(b, d.Others, false, tol));
        off += snap;
        b.Offset(snap);
        var held = new Vector(d.HoldX.Apply(this, off.X, EqualGap(b, d.Others, true)), d.HoldY.Apply(this, off.Y, EqualGap(b, d.Others, false)));
        b.Offset(held - off);
        off = held;
        foreach (var h in new[] { true, false })
        {
            DrawAlignments(b, d.Others, h);
            DrawGaps(b, d.Others, h);
        }
        return off;
    }

    /// <summary>O item está com o mesmo espaço dos dois lados, ou o mesmo espaço de um par vizinho?</summary>
    static bool EqualGap(Rect b, List<Rect> others, bool h)
    {
        var row = Row(b, others, h);
        var gaps = new List<double>();
        for (int i = 0; i + 1 < row.Count; i++)
        {
            double gap = Lo(row[i + 1], h) - Hi(row[i], h);
            if (gap > 0) gaps.Add(gap);
        }
        double before = row.Where(r => Hi(r, h) <= Lo(b, h)).Select(r => Lo(b, h) - Hi(r, h)).DefaultIfEmpty(-1).Where(g => g >= 0).DefaultIfEmpty(-1).Min();
        double after = row.Where(r => Lo(r, h) >= Hi(b, h)).Select(r => Lo(r, h) - Hi(b, h)).DefaultIfEmpty(-1).Where(g => g >= 0).DefaultIfEmpty(-1).Min();
        if (before > 0 && after > 0 && Math.Abs(before - after) < 0.5) return true;
        return gaps.Any(g => (before > 0 && Math.Abs(g - before) < 0.5) || (after > 0 && Math.Abs(g - after) < 0.5));
    }

    /// <summary>Trava um eixo por meio segundo quando os espaços ficam iguais, para sentir que encaixou.</summary>
    sealed class GapHold
    {
        double? _value, _done;
        long _since;

        public double Apply(BoardView view, double off, bool equal)
        {
            if (_value is { } v)
            {
                if (Stopwatch.GetElapsedTime(_since).TotalMilliseconds < GapHoldMs) return v;
                _done = v; // passou o tempo: solta, e só trava de novo depois de sair desse ponto
                _value = null;
            }
            if (!equal) { _done = null; return off; }
            if (_done == off) return off;
            _value = off;
            _since = Stopwatch.GetTimestamp();
            view._holdTimer.Stop();
            view._holdTimer.Start(); // solta mesmo com o mouse parado
            return off;
        }
    }

    // ---------- + da tabela: clicar põe uma; arrastar para fora ou para dentro põe ou tira ----------

    sealed record TableDragState(TableView.AddTag Tag, Point WorldStart, List<List<string>> Original, double Width, double Unit, int Count)
    {
        public bool Moved { get; set; }
    }

    TableDragState? _tableDrag;

    void StartTableDrag(TableView.AddTag add, Point world)
    {
        if (add.Table.Table is not { Count: > 0 } t) return;
        Vm.Checkpoint();
        int cols = t.Max(r => r.Count);
        var box = add.Table.Box;
        _tableDrag = new TableDragState(add, world, t.Select(r => r.ToList()).ToList(), box.Width,
            add.Row ? box.Height / t.Count : box.Width / cols, add.Row ? t.Count : cols);
        add.View.HoldAdd(add.Row, true);
    }

    void UpdateTableDrag(Point world)
    {
        if (_tableDrag is not { } d) return;
        double delta = d.Tag.Row ? world.Y - d.WorldStart.Y : world.X - d.WorldStart.X;
        if (Math.Abs(delta) * _camera.Zoom >= 4) d.Moved = true;
        if (d.Moved) Vm.TableResize(d.Tag.Table, d.Original, d.Width, d.Tag.Row, Math.Max(1, d.Count + (int)Math.Round(delta / d.Unit)));
    }

    void EndTableDrag()
    {
        if (_tableDrag is not { } d) return;
        _tableDrag = null;
        if (!d.Moved) Vm.TableResize(d.Tag.Table, d.Original, d.Width, d.Tag.Row, d.Count + 1); // só um clique
        d.Tag.View.HoldAdd(d.Tag.Row, false);
        ReleaseSurface();
    }

    /// <summary>Linha fina só entre os itens alinhados, em vez de cortar a tela inteira.</summary>
    void DrawAlignments(Rect b, List<Rect> others, bool h)
    {
        const double same = 0.5;
        foreach (var v in new[] { Lo(b, h), (Lo(b, h) + Hi(b, h)) / 2, Hi(b, h) })
        {
            double from = CrossLo(b, h), to = CrossHi(b, h);
            bool any = false;
            foreach (var r in others)
                if (Math.Abs(Lo(r, h) - v) < same || Math.Abs(Hi(r, h) - v) < same || Math.Abs((Lo(r, h) + Hi(r, h)) / 2 - v) < same)
                {
                    any = true;
                    from = Math.Min(from, CrossLo(r, h));
                    to = Math.Max(to, CrossHi(r, h));
                }
            if (any) GuideLine(At(v, from, h), At(v, to, h));
        }
    }

    /// <summary>Distância em px até o vizinho de cada lado; espaços iguais ficam pintados.</summary>
    void DrawGaps(Rect b, List<Rect> others, bool h)
    {
        var row = Row(b, others, h);
        Rect? before = null, after = null;
        foreach (var r in row)
        {
            if (Hi(r, h) <= Lo(b, h) && (before is null || Hi(r, h) > Hi(before.Value, h))) before = r;
            if (Lo(r, h) >= Hi(b, h) && (after is null || Lo(r, h) < Lo(after.Value, h))) after = r;
        }
        double gapBefore = before is { } p ? Lo(b, h) - Hi(p, h) : -1;
        double gapAfter = after is { } n ? Lo(n, h) - Hi(b, h) : -1;

        var pairs = new List<(Rect A, Rect B, double Gap)>();
        for (int i = 0; i + 1 < row.Count; i++)
        {
            double gap = Lo(row[i + 1], h) - Hi(row[i], h);
            if (gap > 0) pairs.Add((row[i], row[i + 1], gap));
        }
        bool Equal(double g) => g > 0 && (Math.Abs(gapBefore - gapAfter) < 0.5 || pairs.Any(x => Math.Abs(x.Gap - g) < 0.5 && !(x.A == before && x.B == after)));

        if (before is { } a) DrawGap(Hi(a, h), Lo(b, h), a, b, h, Equal(gapBefore));
        if (after is { } c) DrawGap(Hi(b, h), Lo(c, h), b, c, h, Equal(gapAfter));
        // O par que serviu de medida também fica pintado.
        foreach (var (A, B, Gap) in pairs)
            if (!(A == before && B == after) && ((gapBefore > 0 && Math.Abs(Gap - gapBefore) < 0.5) || (gapAfter > 0 && Math.Abs(Gap - gapAfter) < 0.5)))
                DrawGap(Hi(A, h), Lo(B, h), A, B, h, equal: true);
    }

    void DrawGap(double from, double to, Rect a, Rect b, bool h, bool equal)
    {
        if (to - from < 1) return;
        double lo = Math.Max(CrossLo(a, h), CrossLo(b, h)), hi = Math.Min(CrossHi(a, h), CrossHi(b, h));
        double cross = (lo + hi) / 2;
        if (equal)
        {
            var p1 = At(from, lo, h);
            var p2 = At(to, hi, h);
            var fill = new Rectangle { Width = Math.Abs(p2.X - p1.X), Height = Math.Abs(p2.Y - p1.Y), Fill = GuideFill, IsHitTestVisible = false };
            Canvas.SetLeft(fill, Math.Min(p1.X, p2.X));
            Canvas.SetTop(fill, Math.Min(p1.Y, p2.Y));
            Guides.Children.Add(fill);
        }
        var s = At(from, cross, h);
        var e = At(to, cross, h);
        GuideLine(s, e);
        // Pontinhas nas extremidades, como uma régua.
        var tick = h ? new Vector(0, 4) : new Vector(4, 0);
        GuideLine(s - tick, s + tick);
        GuideLine(e - tick, e + tick);
        GuideLabel(s + (e - s) / 2, Math.Round(to - from).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    Point At(double along, double cross, bool h) => _camera.ToScreen(h ? new Point(along, cross) : new Point(cross, along));

    void GuideLine(Point a, Point b) =>
        Guides.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = GuideBrush, StrokeThickness = 1, SnapsToDevicePixels = true, IsHitTestVisible = false });

    void GuideLabel(Point center, string text)
    {
        var label = new Border
        {
            Background = GuideBrush,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 1),
            IsHitTestVisible = false,
            Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, FontFamily = (FontFamily)Application.Current.FindResource("Inter") },
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, center.X - label.DesiredSize.Width / 2);
        Canvas.SetTop(label, center.Y - label.DesiredSize.Height / 2);
        Guides.Children.Add(label);
    }

    // ---------- alças: tamanho, rotação e pontas de linha ----------

    sealed record HandleTag(BoardItemViewModel Item, int Index);

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
        int handles = it.Line is { } lt ? (lt.Tipo is "curva" or "angulo" ? 3 : 2) : 8;
        if (Adorner.Children.Count != handles || Adorner.Tag != it) BuildAdorner(it);

        if (it.Kind == ItemKind.Line && it.Line is { } l)
        {
            Place(Adorner.Children[0], _camera.ToScreen(new Point(l.De.X, l.De.Y) + offset));
            Place(Adorner.Children[1], _camera.ToScreen(new Point(l.Para.X, l.Para.Y) + offset));
            if (Adorner.Children.Count == 3) Place(Adorner.Children[2], _camera.ToScreen(LineView.Midpoint(l) + offset));
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
            // Curva e ângulo: alça da dobra no meio (arrastar muda a forma como quiser).
            if (it.Line is { Tipo: "curva" or "angulo" })
            {
                var bendDot = Dot(new HandleTag(it, 11), 13, Cursors.SizeAll);
                bendDot.Fill = AccentBrush;
                bendDot.Stroke = Brushes.White;
                bendDot.ToolTip = "Arraste para mudar a dobra da linha";
                Adorner.Children.Add(bendDot);
            }
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
        if (h.Handle == 11 && it.Line is { } bl)
        {
            if ((h.Pointer - h.WorldStart).Length * _camera.Zoom < 3) return; // só clicou: a dobra não pula
            // Dobra: guardada em relação ao meio das pontas (acompanha quando os itens se movem).
            bl.DobraX = Math.Round(h.Pointer.X - (bl.De.X + bl.Para.X) / 2, 1);
            bl.DobraY = Math.Round(h.Pointer.Y - (bl.De.Y + bl.Para.Y) / 2, 1);
            FitLineBox(it);
            RefreshLines([it], default); // setas presas no meio desta linha acompanham
            return;
        }
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
        if (_handle is { Handle: 9 or 10 } h && h.Item.Line is { } l)
        {
            // Soltou a ponta em cima de um item: prende nele.
            var end = h.Handle == 9 ? l.De : l.Para;
            if (ItemAt(_camera.ToScreen(h.Pointer), h.Item) is { } target && !DependsOn(target, h.Item))
            {
                end.Item = target.Id;
                end.Lado = SideFor(target, h.Pointer);
                RefreshLines([target], default);
            }
            else if (h.EndItem is { } id && Vm.Items.FirstOrDefault(i => i.Id == id) is { } origin)
            {
                // Seta ligada não se solta: soltou no vazio, a ponta volta para o item.
                end.Item = id;
                end.Lado = h.EndSide;
                var p = AnchorOf(origin, h.EndSide);
                end.X = p.X;
                end.Y = p.Y;
                FitLineBox(h.Item);
            }
            h.Item.Touch(nameof(BoardItemViewModel.Line));
        }
        _handle = null;
        ReleaseSurface();
        Vm.CommitMove();
        UpdateAdorner();
    }

    // ---------- conectores ----------

    static Point SidePoint(Rect b, string? side) => side switch
    {
        "cima" => new Point(b.X + b.Width / 2, b.Y),
        "baixo" => new Point(b.X + b.Width / 2, b.Bottom),
        "esquerda" => new Point(b.X, b.Y + b.Height / 2),
        "direita" => new Point(b.Right, b.Y + b.Height / 2),
        _ => new Point(b.X + b.Width / 2, b.Y + b.Height / 2),
    };

    /// <summary>Onde uma ponta presa fica: num lado do item ou, se for uma linha, no meio dela.</summary>
    static Point AnchorOf(BoardItemViewModel item, string? side, Vector offset = default)
    {
        if (item.Line is { } l) return LineView.Midpoint(l) + offset;
        var box = item.Box;
        box.Offset(offset);
        return SidePoint(box, side);
    }

    /// <summary>Lado mais perto do ponto (linha: sempre o meio).</summary>
    static string SideFor(BoardItemViewModel item, Point p) => item.Line is null ? NearestSide(item.Box, p) : "meio";

    /// <summary>"item" é uma linha presa (direta ou indiretamente) em "line"? Evita ligações em círculo.</summary>
    bool DependsOn(BoardItemViewModel item, BoardItemViewModel line, int depth = 0)
    {
        if (item == line) return true;
        if (item.Line is not { } l || depth > 8) return false;
        foreach (var id in new[] { l.De.Item, l.Para.Item })
            if (id is { } i && Vm.Items.FirstOrDefault(x => x.Id == i) is { } next && DependsOn(next, line, depth + 1)) return true;
        return false;
    }

    static string NearestSide(Rect b, Point p)
    {
        var sides = new[] { "cima", "direita", "baixo", "esquerda" };
        return sides.OrderBy(s => (SidePoint(b, s) - p).LengthSquared).First();
    }

    void UpdateConnector(Point p)
    {
        if (_connecting is not { } c) return;
        var a = _camera.ToScreen(AnchorOf(c.From, c.Side));
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
        var start = AnchorOf(c.From, c.Side);
        if ((p - _camera.ToScreen(start)).Length < 12) return; // só clicou
        var target = ItemAt(p, c.From);
        if (target is not null && DependsOn(target, c.From)) return; // ligação em círculo: não cria
        if (target is null)
        {
            if (!copyOnEmpty || c.From.Line is not null) return; // linha solta no vazio não vira cópia
            // Soltou no vazio: cria uma cópia do item já ligada.
            var copy = Vm.DuplicateMany([c.From], default, select: false)[0];
            var size = c.From.Box.Size;
            copy.X = Math.Round(world.X - size.Width / 2);
            copy.Y = Math.Round(world.Y - size.Height / 2);
            target = copy;
        }
        AddConnector(c.From, c.Side, target, SideFor(target, start));
    }

    /// <summary>Cria uma linha presa a dois itens.</summary>
    public BoardItemViewModel AddConnector(BoardItemViewModel a, string sideA, BoardItemViewModel b, string sideB)
    {
        var pa = AnchorOf(a, sideA);
        var pb = AnchorOf(b, sideB);
        var line = Vm.AddItem(new BoardItem
        {
            Tipo = "linha",
            Cor = "branco",
            Linha = new LineData
            {
                De = new LineEnd { X = pa.X, Y = pa.Y, Item = a.Id, Lado = sideA },
                Para = new LineEnd { X = pb.X, Y = pb.Y, Item = b.Id, Lado = sideB }, // sem seta até o usuário pedir
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
        var r = LineView.Extent(l); // pontas e dobra
        line.X = r.X - LineView.Pad;
        line.Y = r.Y - LineView.Pad;
        line.Touch(nameof(BoardItemViewModel.Line));
    }

    // Só as linhas do quadro (refeita quando itens entram ou saem): arrastar não percorre todos os itens.
    List<BoardItemViewModel>? _lines;
    List<BoardItemViewModel> Lines => _lines ??= _vm is null ? [] : _vm.Items.Where(i => i.Kind == ItemKind.Line).ToList();

    static Dictionary<int, BoardItemViewModel> MovedById(IEnumerable<BoardItemViewModel> moved)
    {
        var ids = new Dictionary<int, BoardItemViewModel>();
        foreach (var m in moved) ids[m.Id] = m; // linhas também: setas presas no meio delas acompanham
        return ids;
    }

    void RefreshLines(BoardItemViewModel moved) => RefreshLines(MovedById([moved]), default);

    void RefreshLines(IEnumerable<BoardItemViewModel> moved, Vector offset) => RefreshLines(MovedById(moved), offset);

    /// <summary>Linhas presas aos itens que se moveram acompanham (offset = deslocamento do arraste).</summary>
    void RefreshLines(Dictionary<int, BoardItemViewModel> ids, Vector offset, int depth = 0)
    {
        if (_vm is null || ids.Count == 0 || depth > 6) return;
        Dictionary<int, BoardItemViewModel>? moved = null;
        foreach (var line in Lines)
        {
            if (line.Line is not { } l || ids.ContainsKey(line.Id) && offset != default) continue;
            bool changed = MoveEnd(line, l.De, ids, offset) | MoveEnd(line, l.Para, ids, offset);
            if (!changed) continue;
            FitLineBox(line);
            (moved ??= [])[line.Id] = line;
        }
        // Em cascata: as setas presas no meio das linhas que acabaram de mudar também acompanham.
        if (moved is not null) RefreshLines(moved, default, depth + 1);
    }

    static bool MoveEnd(BoardItemViewModel line, LineEnd end, Dictionary<int, BoardItemViewModel> ids, Vector offset)
    {
        if (end.Item is not { } id || id == line.Id || !ids.TryGetValue(id, out var target)) return false;
        var p = AnchorOf(target, end.Lado, offset);
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
            if (it is not null && it != except) { found = it; return HitTestResultBehavior.Stop; }
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
            De = new LineEnd { X = start.X, Y = start.Y, Item = a?.Id, Lado = a is null ? null : SideFor(a, start) },
            Para = new LineEnd { X = end.X, Y = end.Y, Item = b?.Id, Lado = b is null ? null : SideFor(b, end), Ponta = DrawSettings.ArrowEnd ? "seta" : "nenhuma" },
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
        if (Vm.ReadOnly || Tool != BoardTool.Select || _drag is not null || _connecting is not null) return;
        var item = Tree.FindAncestor<BoardItemContainer>(e.OriginalSource as DependencyObject)?.Item;
        if (item is not null && item.Editing == EditField.None) _rightStart = (item, e.GetPosition(Surface));
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
        if (Tree.FindAncestor<System.Windows.Controls.Primitives.TextBoxBase>(src) is not null) return;
        if (Vm.ReadOnly) { e.Handled = true; return; } // somente visualização: sem menus nem bandeja
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

    /// <summary>Desenha uma área do quadro (todo ou só a seleção) numa imagem, com o fundo do quadro atual por baixo.</summary>
    public BitmapSource RenderRegion(IReadOnlyCollection<BoardItemViewModel> items, double scale, bool onlyThese, BoardItemViewModel? centerOn = null)
    {
        // Primeiro cria e mede tudo: itens fora da tela ainda não têm o tamanho real, e a área sairia errada.
        foreach (var it in items) Realize(it);
        // A imagem exportada mostra o item inteiro, mesmo que o quadro esteja afastado na tela.
        bool wasOverview = _overview;
        if (wasOverview) { _overview = false; foreach (var (vm, c) in _containers) ApplyOverview(vm, c); ItemsLayer.UpdateLayout(); }
        var hidden = new List<BoardItemContainer>();
        if (onlyThese)
            foreach (var (vm, c) in _containers)
                if (!items.Contains(vm) && c.Visibility == Visibility.Visible) { c.Visibility = Visibility.Hidden; hidden.Add(c); }
        // Sem o cache de bitmap: o texto é desenhado direto, nítido em qualquer escala.
        var cached = _containers.Values.Where(c => c.CacheMode is not null).ToList();
        foreach (var c in cached) c.CacheMode = null;
        // Os + das tabelas são da tela: não vão para a imagem.
        var tables = _containers.Values.Where(c => c.Item?.Kind == ItemKind.Table).Select(FindChild<TableView>).OfType<TableView>().ToList();
        foreach (var tv in tables) tv.ShowAdd(false);
        UpdateLayout();

        var area = items.Aggregate(Rect.Empty, (r, i) => { r.Union(i.Bounds); return r; });
        if (area.IsEmpty) area = _camera.VisibleWorld;
        area.Inflate(40, 40);
        // Parte do centro do título e abre para os dois lados até caber tudo.
        if (centerOn is not null)
        {
            double cx = centerOn.Box.X + centerOn.Box.Width / 2;
            double half = Math.Max(cx - area.Left, area.Right - cx);
            area = new Rect(cx - half, area.Y, half * 2, area.Height);
        }
        scale = Math.Min(scale, Math.Min(MaxExportSide / Math.Max(area.Width, area.Height), Math.Sqrt(MaxExportPixels / (area.Width * area.Height))));

        // Fotos na resolução que a imagem pede (na tela elas ficam reduzidas para poupar memória).
        var images = _containers.Values.Select(FindChild<ImageView>).OfType<ImageView>().ToList();
        foreach (var iv in images) iv.LoadForExport(scale);

        int w = (int)Math.Ceiling(area.Width * scale), h = (int)Math.Ceiling(area.Height * scale);
        var rtb = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushTransform(new TranslateTransform(-area.X, -area.Y));
            SurfaceBg.PaintWorld(dc, area);
            dc.Pop();
            dc.Pop();
        }
        rtb.Render(dv);
        // Os itens vão direto para a imagem, sem VisualBrush: o pincel vira um bitmap intermediário de tamanho
        // limitado e saía borrado. O Render ignora a transformação do próprio elemento desenhado, então desenha a
        // Surface (só com a camada dos itens à mostra) e a câmera da camada vira a escala da exportação.
        // (Troca a matriz, não o objeto: um RenderTransform novo só vale depois do próximo arrange.)
        var camera = CameraTransform.Matrix;
        var clip = Surface.Clip;
        CameraTransform.Matrix = new Matrix(scale, 0, 0, scale, -area.X * scale, -area.Y * scale);
        RenderOptions.SetBitmapScalingMode(ItemsLayer, BitmapScalingMode.HighQuality);
        Surface.Clip = null;
        SurfaceBg.Opacity = Hud.Opacity = 0;
        try
        {
            UpdateLayout();
            rtb.Render(Surface);
        }
        finally
        {
            SurfaceBg.Opacity = Hud.Opacity = 1;
            Surface.Clip = clip;
            ItemsLayer.ClearValue(RenderOptions.BitmapScalingModeProperty);
            CameraTransform.Matrix = camera;
            foreach (var iv in images) iv.EndExport();
            foreach (var c in hidden) c.Visibility = Visibility.Visible;
            foreach (var c in cached) c.CacheMode = _itemCache;
            if (wasOverview) { _overview = true; foreach (var (vm, c) in _containers) ApplyOverview(vm, c); }
            foreach (var tv in tables) tv.ShowAdd(true);
        }
        rtb.Freeze();
        return rtb;
    }
}

/// <summary>Configuração da paleta de desenho e das linhas (compartilhada).</summary>
public static class DrawSettings
{
    public static Color Color = Color.FromRgb(0x1D, 0x21, 0x30);
    public static double Thickness = 4;
    public static string LineType = "reta";
    public static bool ArrowEnd; // linhas nascem sem seta; o "Seta" da bandeja liga

    public static InkStroke Stroke(BoardTool tool, List<double> points) => new()
    {
        Ferramenta = tool switch { BoardTool.Pencil => "lapis", BoardTool.Marker => "marcador", _ => "caneta" },
        Cor = Color.ToString(),
        Espessura = Thickness,
        Pontos = points,
    };
}
