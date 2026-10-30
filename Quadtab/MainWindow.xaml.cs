using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Quadtab.Behaviors;
using Quadtab.Controls;
using Quadtab.Models;
using Quadtab.Rendering;
using Quadtab.Services;
using Quadtab.ViewModels;

namespace Quadtab;

public partial class MainWindow : Window
{
    const double SidebarWidth = 292;
    const double SidePeek = 12;        // escondida, a lateral deixa ~12 px à mostra (como o dock)
    const double DockHiddenY = 52;     // escondido, o dock deixa ~14 px à mostra

    MainViewModel _vm;
    readonly BackdropBlobs _glassBackdrop = new() { Saturation = 1.9 };
    readonly ScaleTransform MenuScale = new();
    readonly ScaleTransform ActionsScale = new();
    readonly ScaleTransform TrayScale = new();
    readonly TranslateTransform TrayShift = new() { Y = DockHiddenY }; // dock quase todo abaixo da borda
    bool _menuOpen, _actionsOpen, _ctxOpen, _ctxWasOpen;
    // Botão que abriu o menu de ações: clicar nele de novo fecha (em vez de reabrir).
    FrameworkElement? _actionsOwner, _ownerJustClosed;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        FitToScreen();

        // O vidro sobre o fundo enxerga uma cópia saturada das manchas (saturate 190%, sem custo de efeito).
        ApplyGlassBackdropColors();
        WindowRoot.SizeChanged += (_, e) => LayoutGlassBackdrop(e.NewSize);
        foreach (var g in new[] { SidebarGlass, TitleCap, MoreCap, WindowCap, Editor.Glass, Quick.Glass })
            g.BackdropSource = _glassBackdrop;

        // O vidro sobre o quadro usa a textura já desfocada, alinhada com a câmera.
        Board.Settled += RefreshBoardGlass;
        Board.UseAsGlassBackdrop(Hint, Status, Tray.DockGlass, Tray.SheetGlass, GoToContent, FpsPanel, ZoomPanel, SearchBar, PresentBar, Palette.Glass);
        foreach (var g in new[] { Menu.GlassPanel, Actions.Glass, CtxTray.Glass })
        {
            g.SetResourceReference(GlassPanel.BackgroundProperty, "MenuGlassBrush");
            g.SetResourceReference(GlassPanel.BackdropBaseProperty, "BgBrush");
            g.BackdropSource = AppRoot;
        }

        // Ligações entre as partes
        Menu.RenderTransform = MenuScale;
        Actions.RenderTransform = ActionsScale;
        CtxTray.RenderTransform = TrayScale;
        Tray.RenderTransform = TrayShift;
        Board.MenuRequested += (item, p) => ShowMenu(item, default, p);
        Board.TrayRequested += ShowContextTray;
        Menu.CloseRequested += CloseMenu;
        Menu.MoreRequested += ShowItemExtras;
        Actions.Closed += CloseActions;
        CtxTray.Board = Board;
        CtxTray.CloseRequested += CloseContextTray;
        Palette.Board = Board;
        Tray.Overlay = Overlay;
        Tray.Root = WindowRoot;
        // Post-it da bandeja: última cor e último estilo usados.
        Tray.AddRequested += p => Board.AddAtCenter(p.Kind, p.Color ?? _vm.LastColor, p.Style ?? NoteStyleFor(p.Kind));
        Tray.ShapesRequested += ShowShapePicker;
        Tray.DropRequested += (p, pt) => Board.TryAddAt(WindowRoot, pt, p.Kind, p.Color ?? _vm.LastColor, p.Style ?? NoteStyleFor(p.Kind));
        Editor.CloseRequested += ClosePage;
        Editor.ExpandChanged += ApplyEditorLayout;
        // Quadro estreito (página aberta ao lado): a dica e o status saem para não cobrir o dock.
        BoardArea.SizeChanged += (_, e) =>
        {
            Status.Visibility = e.NewSize.Width < 1000 || _presenting ? Visibility.Collapsed : Visibility.Visible;
            UpdateHint();
        };

        Attach(vm);
        App.Library.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LibraryViewModel.GalleryOpen)) ApplyEmptyState(_emptyMode);
        };
        _sideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sideTimer.Tick += (_, _) => { _sideTimer.Stop(); if (!KeepSide()) RevealSide(false); };
        ApplySidebar(animate: false);


        SourceInitialized += (_, _) =>
        {
            ThemeService.ApplyToWindow(this);
            GlassGlow.Attach(this);
            // Rolagem horizontal do touchpad (o WPF não entrega essa mensagem sozinho).
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
            BoardCursors.SetScale(VisualTreeHelper.GetDpi(this).DpiScaleX);
            if (StorageService.State.TelaCheia) SetFullscreen(true);
        };
        // Balões de ajuda (3 s parado em cima de um botão) e faíscas a cada clique.
        HelpTips.Attach(this, TipLayer);
        Sparks.Enabled = StorageService.State.Faiscas;
        // Ponteiro próprio em todo o app: troca os cursores do Windows pelos equivalentes desenhados.
        AddHandler(Mouse.QueryCursorEvent, new QueryCursorEventHandler((_, e) =>
        {
            e.Cursor = BoardCursors.Map(e.Cursor);
            e.Handled = true;
        }), handledEventsToo: true);
        DpiChanged += (_, e) => BoardCursors.SetScale(e.NewDpi.DpiScaleX);

        _dockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.8) };
        _dockTimer.Tick += (_, _) => { _dockTimer.Stop(); if (!KeepDock()) SetDock(false); };
        _zoomTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        _zoomTimer.Tick += (_, _) => { _zoomTimer.Stop(); HideZoom(); };
        PreviewMouseMove += (_, e) => { DockNear(e.GetPosition(BoardArea)); SideNear(e.GetPosition(WindowRoot)); };
        MouseLeave += (_, _) => { DockNear(new Point(-1, -1)); SideNear(new Point(9999, -1)); };
        _itemsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _itemsTimer.Tick += (_, _) => { _itemsTimer.Stop(); App.Library.RefreshCurrent(); };

        ThemeService.Changed += () =>
        {
            AnimateBlobs();
            ApplyGlassBackdropColors();
            LayoutGlassBackdrop(WindowRoot.RenderSize);
            RefreshBoardGlass();
        };
        StateChanged += (_, _) => ApplyWindowState();
        // O X fecha o app sempre, mesmo com algo aberto por cima dele (Procurar, galeria, menus).
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!CloseButton.IsVisible || ToolbarCollapse.Progress < 0.5) return;
            var p = e.GetPosition(CloseButton);
            if (p.X < 0 || p.Y < 0 || p.X > CloseButton.ActualWidth || p.Y > CloseButton.ActualHeight) return;
            e.Handled = true;
            CloseApp();
        };
        PreviewMouseDown += (_, e) => Sparks.Fire(e.GetPosition(Sparks));
        PreviewMouseDown += OnPreviewMouseDown;
        PreviewMouseRightButtonUp += OnPreviewMouseRightUp;
        PreviewMouseWheel += (_, _) => { CloseMenu(); CloseActions(); };
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += (_, e) =>
        {
            if (e.Key == Key.Space) Board.SpaceHeld = false;
        };
        Deactivated += (_, _) => Board.SpaceHeld = false;
        Closing += (_, _) => { if (!_closing) SaveAll(); };
    }

    /// <summary>Raiz da janela (coordenadas dos menus).</summary>
    public FrameworkElement Root => WindowRoot;

    // ---------- trocar de quadro ----------

    void Attach(MainViewModel vm)
    {
        _vm.PropertyChanged -= OnVmChanged;
        _vm.Items.CollectionChanged -= OnItemsChanged;
        _vm = vm;
        vm.PropertyChanged += OnVmChanged;
        vm.Items.CollectionChanged += OnItemsChanged;
        _quietZoomUntil = DateTime.Now.AddSeconds(1); // abrir um quadro não mostra o balão de zoom
        ApplyEmptyState(StorageService.IsEmptyBoard(vm.FilePath));
        ApplyReadOnly();
        ApplyTitle(animate: false);
        UpdateSearchCount();
    }

    /// <summary>Todos os quadros apagados: a tela vazia cobre título e quadro, com o botão "Criar novo quadro".</summary>
    void ApplyEmptyState(bool empty)
    {
        _emptyMode = empty;
        TitleCollapse.Visibility = empty ? Visibility.Collapsed : Visibility.Visible; // sem o nome interno "vazio"
        // A galeria (ex.: Apagados recentemente, para restaurar) fica por cima da tela vazia.
        empty &= !App.Library.GalleryOpen;
        bool was = EmptyState.Visibility == Visibility.Visible;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (!empty || was) return;
        // Surge com fade e uma leve mola.
        Anim.Set(EmptyState, OpacityProperty, 0.0);
        Anim.Set(EmptyScale, ScaleTransform.ScaleXProperty, 0.92);
        Anim.Set(EmptyScale, ScaleTransform.ScaleYProperty, 0.92);
        Anim.To(EmptyState, OpacityProperty, 1, 0.3, Anim.Ease);
        Anim.To(EmptyScale, ScaleTransform.ScaleXProperty, 1, 0.55, Anim.Panel);
        Anim.To(EmptyScale, ScaleTransform.ScaleYProperty, 1, 0.55, Anim.Panel);
    }

    bool _emptyMode;

    /// <summary>Somente visualização: some o dock da bandeja (nada para adicionar) e fecham os menus.</summary>
    void ApplyReadOnly()
    {
        Tray.Visibility = _vm.ReadOnly || _presenting ? Visibility.Collapsed : Visibility.Visible;
        if (_vm.ReadOnly) { _vm.TrayOpen = false; CloseMenu(); CloseContextTray(); }
    }

    void OnCreateFirstBoard(object sender, RoutedEventArgs e) => App.Library.NewBoard();

    /// <summary>Mostra outro quadro (App.OpenBoard).</summary>
    public void SetBoard(MainViewModel vm)
    {
        CloseMenu();
        CloseActions();
        CloseContextTray();
        if (_presenting) StopPresenting();
        CloseSearch();
        Board.Tool = BoardTool.Select;
        Attach(vm);
        DataContext = vm;
        RefreshBoardGlass();
    }

    /// <summary>Tamanho de referência 1440×920, mas sem passar da área livre da tela.</summary>
    void FitToScreen()
    {
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
        Width = Math.Max(MinWidth, Math.Min(Width, area.Width * 0.94));
        Height = Math.Max(MinHeight, Math.Min(Height, area.Height * 0.94));
    }

    // A cópia saturada fica fora da árvore visual, então recebe as cores do tema diretamente.
    void ApplyGlassBackdropColors()
    {
        _glassBackdrop.Bg = (Color)FindResource("BgColor");
        _glassBackdrop.Blob1 = (Color)FindResource("Blob1Color");
        _glassBackdrop.Blob2 = (Color)FindResource("Blob2Color");
        _glassBackdrop.Blob3 = (Color)FindResource("Blob3Color");
    }

    void LayoutGlassBackdrop(Size size)
    {
        _glassBackdrop.Measure(size);
        _glassBackdrop.Arrange(new Rect(size));
        _glassBackdrop.UpdateLayout();
    }

    /// <summary>Reposiciona o fundo desfocado dos painéis sobre o quadro e o botão "Ir para o conteúdo".</summary>
    void RefreshBoardGlass()
    {
        GoToContent.Visibility = Board.HasContentInView || _presenting ? Visibility.Collapsed : Visibility.Visible;
        Board.RefreshGlass();
    }

    void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.TitleHidden): ApplyTitle(animate: true); break;
            case nameof(MainViewModel.SidebarOpen): ApplySidebar(animate: true); break;
            case nameof(MainViewModel.ReadOnly): ApplyReadOnly(); break;
            case nameof(MainViewModel.BoardSearch): _hit = -1; UpdateSearchCount(); break;
            case nameof(MainViewModel.Zoom): ShowZoom(); break;
            case nameof(MainViewModel.TrayOpen):
                if (_vm.TrayOpen) SetDock(true);
                else if (!KeepDock()) { _dockTimer.Stop(); _dockTimer.Start(); }
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.6) };
                t.Tick += (_, _) => { t.Stop(); RefreshBoardGlass(); };
                t.Start();
                break;
        }
    }

    void OnGoToContent(object sender, RoutedEventArgs e) => Board.Fit();

    // ---------- contador de FPS (Ctrl+Shift+F) ----------

    readonly System.Diagnostics.Stopwatch _fpsClock = new();
    int _fpsFrames;
    double _fpsWorst;
    TimeSpan _fpsLast;

    void ToggleFps()
    {
        bool on = FpsPanel.Visibility != Visibility.Visible;
        FpsPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on)
        {
            _fpsClock.Restart();
            _fpsFrames = 0;
            _fpsWorst = 0;
            _fpsLast = TimeSpan.Zero;
            FpsText.Text = "medindo…";
            CompositionTarget.Rendering += OnFpsFrame;
            RefreshBoardGlass();
        }
        else CompositionTarget.Rendering -= OnFpsFrame;
    }

    void OnFpsFrame(object? sender, EventArgs e)
    {
        var t = ((RenderingEventArgs)e).RenderingTime;
        if (t == _fpsLast) return;
        if (_fpsLast != TimeSpan.Zero) _fpsWorst = Math.Max(_fpsWorst, (t - _fpsLast).TotalMilliseconds);
        _fpsLast = t;
        _fpsFrames++;
        double secs = _fpsClock.Elapsed.TotalSeconds;
        if (secs < 0.5) return;
        string gpu = (RenderCapability.Tier >> 16) == 2 ? "GPU" : "sem GPU";
        FpsText.Text = $"{_fpsFrames / secs:0} fps · {gpu} · pior quadro {_fpsWorst:0.0} ms · cálculo {Board.LastWorkMs:0.0} ms · {Board.RealizedCount}/{Board.ItemCount} itens";
        _fpsClock.Restart();
        _fpsFrames = 0;
        _fpsWorst = 0;
    }

    // Rolagem horizontal (WM_MOUSEHWHEEL): dois dedos no touchpad ou rodinha inclinada.
    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_MOUSEHWHEEL = 0x020E;
        if (msg == WM_MOUSEHWHEEL && Board.IsMouseOver)
        {
            int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            Board.PanBy(new Vector(-delta * 0.6, 0));
            CloseMenu();
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ---------- barra lateral: fixa ou escondida na borda (aparece como o dock da bandeja) ----------

    readonly DispatcherTimer _sideTimer;
    bool _sideRevealed;

    /// <summary>A lateral ocupa a coluna dela (fixa) ou flutua escondida sobre o quadro.</summary>
    bool SideDocked => _vm.SidebarOpen; // fixa também na tela cheia, até clicar para esconder

    double SideMargin => _fullscreen ? 0 : 14;

    /// <summary>Fixa ↔ escondida: o quadro cresce ou encolhe com a mola da lateral.</summary>
    void ApplySidebar(bool animate)
    {
        double k = animate ? 1 : 0;
        Anim.To(MainColumn, MarginProperty, new Thickness(SideDocked ? SidebarWidth + 14 : 0, 0, 0, 0), 0.45 * k, Anim.Sidebar);
        _sideTimer.Stop();
        // Quem acabou de esconder com o mouse em cima: ela só sai quando o mouse se afastar.
        _sideRevealed = !SideDocked && SidebarGlass.IsMouseOver;
        MoveSide(animate);
    }

    void MoveSide(bool animate)
    {
        bool shown = SideDocked || _sideRevealed;
        double x = shown ? 0 : -(SidebarWidth + SideMargin - SidePeek);
        if (animate) Anim.To(SideShift, TranslateTransform.XProperty, x, shown ? 0.55 : 0.4, shown ? Anim.Panel : Anim.Ease);
        else Anim.Set(SideShift, TranslateTransform.XProperty, x);
        SidebarView.IsHitTestVisible = shown;
        Anim.To(SidebarView, OpacityProperty, shown ? 1 : 0, animate ? (shown ? 0.3 : 0.2) : 0, Anim.Ease);
        // Flutuando sobre o quadro, o vidro fica mais denso; o fundo desfocado acompanha o deslize.
        SidebarGlass.SetResourceReference(GlassPanel.BackgroundProperty, SideDocked ? "GlassBrush" : "Glass2Brush");
        TrackGlass(0.6);
    }

    void RevealSide(bool show)
    {
        if (show == _sideRevealed || SideDocked) return;
        _sideRevealed = show;
        if (!show) _sideHeld = false;
        MoveSide(animate: true);
    }

    /// <summary>Escondida: aparece com o mouse a menos de ~30 px da borda esquerda e some 1 s depois que ele sai.</summary>
    void SideNear(Point p)
    {
        if (SideDocked || _presenting) return;
        bool near = (p.X >= 0 && p.Y >= 0 && p.X <= SideMargin + SidePeek + 16) || (_sideRevealed && SidebarGlass.IsMouseOver);
        if (near || KeepSide())
        {
            _sideTimer.Stop();
            if (near) RevealSide(true);
        }
        else if (_sideRevealed && !_sideTimer.IsEnabled) _sideTimer.Start();
    }

    // Clicou dentro dela enquanto aberta: fica na tela até clicar fora (no quadro, na barra...).
    bool _sideHeld;

    // Segura aberta: clicada, com um menu dela aberto, renomeando algo nela ou com o mouse em cima.
    bool KeepSide() => _sideHeld || _actionsOpen || (Keyboard.FocusedElement is TextBox tb && SidebarView.IsAncestorOf(tb)) || (_sideRevealed && SidebarGlass.IsMouseOver);

    // ---------- título grande (recolhe em 0.5 s) e renomear ----------

    void ApplyTitle(bool animate)
    {
        bool hidden = _vm.TitleHidden;
        LayoutStage();
        UpdateHint();
        double k = animate ? 1 : 0;
        Anim.To(TitleCollapse, CollapsePanel.ProgressProperty, hidden ? 0 : 1, 0.5 * k, Anim.Collapse);
        Anim.To(BigTitle, OpacityProperty, hidden ? 0 : 1, 0.3 * k, Anim.Ease);
        Anim.To(BigMove, TranslateTransform.YProperty, hidden ? -14 : 0, 0.5 * k, Anim.CollapseMove);
        Anim.To(BigScale, ScaleTransform.ScaleXProperty, hidden ? 0.98 : 1, 0.5 * k, Anim.CollapseMove);
        Anim.To(BigScale, ScaleTransform.ScaleYProperty, hidden ? 0.98 : 1, 0.5 * k, Anim.CollapseMove);
        BigTitle.IsHitTestVisible = !hidden;

        // A seta da barra (sempre à mostra) aponta para baixo quando o título está escondido.
        Anim.To(TitleArrow, RotateTransform.AngleProperty, hidden ? 180 : 0, 0.5 * k, Anim.Tap);
        TitleButton.ToolTip = hidden ? "Mostra o título grande do quadro" : "Esconde o título grande e aumenta o quadro";
    }

    /// <summary>
    /// Onde fica o quadro: abaixo do título (normal) ou de borda a borda (tela cheia sem título).
    /// Na tela cheia a seta continua funcionando: com o título à mostra, o quadro começa abaixo dele.
    /// </summary>
    /// <summary>A pílula com o nome do quadro só aparece quando o título grande está escondido.</summary>
    void UpdateHint()
    {
        bool show = _vm.TitleHidden && !_presenting && SearchBar.Visibility != Visibility.Visible && BoardArea.ActualWidth >= 700;
        if (show == (Hint.Visibility == Visibility.Visible)) return;
        Hint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        // Surge com fade, descendo 6 px.
        var drop = new TranslateTransform(0, -6);
        Hint.RenderTransform = drop;
        Anim.Set(Hint, OpacityProperty, 0.0);
        Anim.To(Hint, OpacityProperty, 1, 0.3, Anim.Ease);
        Anim.To(drop, TranslateTransform.YProperty, 0, 0.45, Anim.Panel);
    }

    void LayoutStage()
    {
        bool full = _fullscreen && _vm.TitleHidden;
        Grid.SetRow(Stage, full ? 0 : 2);
        Grid.SetRowSpan(Stage, full ? 3 : 1);
        Stage.Margin = new Thickness(0, full ? 0 : 14, 0, 0);
        BigTitle.Margin = new Thickness(_fullscreen ? 24 : 10, 14, 10, 0);
    }

    void OnTitleDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { StartRename(); e.Handled = true; }
        else DragArea(e);
    }

    void StartRename()
    {
        if (_vm.TitleHidden) _vm.TitleHidden = false;
        TitleEditor.Text = _vm.BoardTitle;
        TitleEditor.Visibility = Visibility.Visible;
        BigTitleText.Visibility = Visibility.Hidden;
        Dispatcher.BeginInvoke(() => { TitleEditor.Focus(); TitleEditor.SelectAll(); }, DispatcherPriority.Input);
    }

    void EndRename(bool commit)
    {
        if (TitleEditor.Visibility != Visibility.Visible) return;
        TitleEditor.Visibility = Visibility.Collapsed;
        BigTitleText.Visibility = Visibility.Visible;
        if (commit)
        {
            _vm.Rename(TitleEditor.Text);
            App.Library.Refresh();
        }
    }

    void OnTitleKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { EndRename(true); Board.Focus(); e.Handled = true; }
        else if (e.Key == Key.Escape) { EndRename(false); e.Handled = true; }
    }

    void OnTitleLost(object sender, KeyboardFocusChangedEventArgs e) => EndRename(true);

    // ---------- zoom ----------

    void OnZoomIn(object sender, RoutedEventArgs e) => Board.ZoomStep(+1);
    void OnZoomOut(object sender, RoutedEventArgs e) => Board.ZoomStep(-1);
    void OnFit(object sender, RoutedEventArgs e) => Board.Fit();

    // ---------- menus flutuantes (abrem onde clicou e nunca saem da tela) ----------

    void Place(FrameworkElement el, ScaleTransform scale, Point windowPoint, bool pop = true)
    {
        el.Visibility = Visibility.Visible;
        // Nunca maior que a janela: o que passar rola por dentro (nada fica cortado).
        el.MaxHeight = Math.Max(120, WindowRoot.ActualHeight - 16);
        el.MaxWidth = Math.Max(200, WindowRoot.ActualWidth - 16);
        el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = el.DesiredSize;
        double w = WindowRoot.ActualWidth, h = WindowRoot.ActualHeight;
        double x = Math.Clamp(windowPoint.X, 8, Math.Max(8, w - size.Width - 8));
        double y = Math.Clamp(windowPoint.Y, 8, Math.Max(8, h - size.Height - 8));
        Canvas.SetLeft(el, x);
        Canvas.SetTop(el, y);
        el.RenderTransformOrigin = new Point(
            Math.Clamp((windowPoint.X - x) / Math.Max(1, size.Width), 0, 1),
            Math.Clamp((windowPoint.Y - y) / Math.Max(1, size.Height), 0, 1));
        if (!pop) return;
        Anim.Set(scale, ScaleTransform.ScaleXProperty, 0.86);
        Anim.Set(scale, ScaleTransform.ScaleYProperty, 0.86);
        Anim.Set(el, OpacityProperty, 0.0);
        Anim.To(scale, ScaleTransform.ScaleXProperty, 1, 0.4, Anim.Pop);
        Anim.To(scale, ScaleTransform.ScaleYProperty, 1, 0.4, Anim.Pop);
        Anim.To(el, OpacityProperty, 1, 0.2, Anim.Ease);
        GlassGlow.Kick();
    }

    void ShowMenu(BoardItemViewModel? item, Point boardPoint, Point windowPoint)
    {
        CloseActions();
        CloseContextTray();
        if (item is null) return;
        Menu.ShowForItem(item);
        Place(Menu, MenuScale, windowPoint);
        _menuOpen = true;
        Dispatcher.BeginInvoke(Menu.GlassPanel.RefreshBackdrop, DispatcherPriority.Loaded);
    }

    void CloseMenu()
    {
        if (!_menuOpen) return;
        _menuOpen = false;
        Menu.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Menu genérico de ações (lateral, galeria, "…", opções extras do item).
    /// owner = botão que abre o menu: clicar nele com o menu aberto fecha o menu.
    /// </summary>
    public void ShowActions(Point windowPoint, IEnumerable<ActionItem> items, FrameworkElement? owner = null)
    {
        CloseMenu();
        CloseContextTray();
        if (owner is not null && owner == _ownerJustClosed) { _ownerJustClosed = null; return; }
        Actions.SetItems(items);
        Place(Actions, ActionsScale, windowPoint);
        _actionsOpen = true;
        _actionsOwner = owner;
        Dispatcher.BeginInvoke(Actions.Glass.RefreshBackdrop, DispatcherPriority.Loaded);
    }

    void CloseActions()
    {
        if (!_actionsOpen) return;
        _actionsOpen = false;
        _actionsOwner = null;
        Actions.Visibility = Visibility.Collapsed;
    }

    /// <summary>Bandeja junto ao cursor. Um segundo clique direito fecha.</summary>
    void ShowContextTray(Point world, Point windowPoint)
    {
        CloseMenu();
        CloseActions();
        if (_ctxWasOpen) { _ctxWasOpen = false; return; }
        CtxTray.Show(world);
        Place(CtxTray, TrayScale, windowPoint);
        _ctxOpen = true;
        Dispatcher.BeginInvoke(CtxTray.Glass.RefreshBackdrop, DispatcherPriority.Loaded);
    }

    NoteStyle? NoteStyleFor(ItemKind kind) => kind == ItemKind.PostIt ? _vm.NewStyle : null;

    /// <summary>Biblioteca de formas acima do dock: a forma escolhida nasce no centro da tela.</summary>
    void ShowShapePicker()
    {
        CloseMenu();
        CloseActions();
        CtxTray.ShowShapesAt(Board.ViewCenter);
        var dock = Tray.DockGlass;
        Place(CtxTray, TrayScale, dock.TranslatePoint(new Point(dock.ActualWidth / 2 - 186, -440), WindowRoot));
        _ctxOpen = true;
        Dispatcher.BeginInvoke(CtxTray.Glass.RefreshBackdrop, DispatcherPriority.Loaded);
    }

    void CloseContextTray()
    {
        if (!_ctxOpen) return;
        _ctxOpen = false;
        CtxTray.Visibility = Visibility.Collapsed;
    }

    // Dois cliques com o botão direito no quadro: outro item igual ao último criado, no ponto clicado.
    long _lastRightDown;
    Point _lastRightPos;
    bool _eatRightUp;

    bool IsRightDouble(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Right) return false;
        var p = e.GetPosition(WindowRoot);
        long now = Environment.TickCount64;
        bool dbl = now - _lastRightDown <= GetDoubleClickTime()
                   && Math.Abs(p.X - _lastRightPos.X) <= SystemParameters.MinimumHorizontalDragDistance * 2
                   && Math.Abs(p.Y - _lastRightPos.Y) <= SystemParameters.MinimumVerticalDragDistance * 2;
        _lastRightDown = dbl ? 0 : now; // um terceiro clique começa de novo
        _lastRightPos = p;
        return dbl && BoardArea.IsVisible && !Quick.IsOpen && !App.Library.GalleryOpen && !_presenting && Board.IsInside(WindowRoot, p);
    }

    void OnPreviewMouseRightUp(object sender, MouseButtonEventArgs e)
    {
        if (!_eatRightUp) return;
        _eatRightUp = false;
        e.Handled = true; // o soltar do 2º clique não abre a bandeja de novo
    }

    void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsRightDouble(e) && !_vm.ReadOnly)
        {
            CloseMenu();
            CloseActions();
            CloseContextTray();
            _ctxWasOpen = false;
            _eatRightUp = true;
            _vm.EndEdit();
            Board.Tool = BoardTool.Select;
            _vm.RepeatLast(Board.ToWorld(WindowRoot, e.GetPosition(WindowRoot)));
            e.Handled = true;
            return;
        }
        // Lateral escondida e aberta pelo mouse: um clique nela a prende; um clique fora solta (e ela sai em 1 s).
        if (_sideRevealed && !SideDocked)
        {
            _sideHeld = SidebarGlass.IsMouseOver;
            if (!_sideHeld && !_sideTimer.IsEnabled) _sideTimer.Start();
        }
        // Renomeando (na lateral ou no título) e clicou fora do campo: termina e grava o nome.
        if (Keyboard.FocusedElement is TextBox renaming && !renaming.IsMouseOver
            && (renaming == TitleEditor || SidebarView.IsAncestorOf(renaming)))
            Keyboard.ClearFocus();
        // Clicar fora fecha os menus (o clique continua valendo para o que estiver embaixo).
        _ctxWasOpen = _ctxOpen && !CtxTray.IsMouseOver && e.ChangedButton == MouseButton.Right;
        _ownerJustClosed = _actionsOpen && _actionsOwner is { IsMouseOver: true } ? _actionsOwner : null;
        if (_menuOpen && !Menu.IsMouseOver) CloseMenu();
        if (_actionsOpen && !Actions.IsMouseOver) CloseActions();
        if (_ctxOpen && !CtxTray.IsMouseOver) CloseContextTray();
    }

    /// <summary>Opções extras de um item: agrupar, alinhar, camadas, estilo e descrição.</summary>
    void ShowItemExtras(BoardItemViewModel item, Point at)
    {
        var sel = _vm.SelectedItems.ToList();
        var list = new List<ActionItem>
        {
            new("Para o topo", "IcChevronUp", () => _vm.ToTop(_vm.SelectedItems.ToList())),
            new("Para o fundo", "IcChevronDown", () => _vm.ToBottom(_vm.SelectedItems.ToList())),
            new("Uma camada para frente", "IcArrowUp", () => _vm.Step(item, +1)),
            new("Uma camada para trás", "IcArrowDown", () => _vm.Step(item, -1)),
            new("Copiar estilo", "IcBrush", () => _vm.CopyStyle(item), Separator: true),
        };
        if (_vm.HasStyleClip) list.Add(new("Colar estilo", "IcBrush", _vm.PasteStyle));
        if (sel.Count >= 2)
        {
            list.Add(new("Agrupar (Ctrl+G)", "IcGroup", _vm.GroupSelection, Separator: true));
            list.Add(new("Alinhar à esquerda", "IcAlignLeft", () => _vm.Align("esquerda")));
            list.Add(new("Centralizar", "IcAlignCenter", () => _vm.Align("centro")));
            list.Add(new("Alinhar no topo", "IcAlignTop", () => _vm.Align("topo")));
            list.Add(new("Centralizar na vertical", "IcAlignMiddle", () => _vm.Align("meio")));
            if (sel.Count >= 3)
            {
                list.Add(new("Distribuir na horizontal", "IcDistributeH", () => _vm.Distribute(true)));
                list.Add(new("Distribuir na vertical", "IcDistributeV", () => _vm.Distribute(false)));
            }
        }
        if (sel.Any(i => i.Group is not null)) list.Add(new("Desagrupar (Ctrl+Shift+G)", "IcGroup", _vm.Ungroup, Separator: sel.Count < 2));
        ShowActions(at, list);
    }

    // ---------- menu "…" da barra ----------

    void OnMore(object sender, RoutedEventArgs e)
    {
        var p = MoreButton.TranslatePoint(new Point(MoreButton.ActualWidth, MoreButton.ActualHeight + 8), WindowRoot);
        var sel = _vm.SelectedItems.ToList();
        var list = new List<ActionItem>
        {
            new("Desfazer (Ctrl+Z)", "IcUndo", _vm.Undo),
            new("Refazer (Ctrl+Shift+Z)", "IcRedo", _vm.Redo),
            new("Procurar (Ctrl+K)", "IcSearch", ShowSearch, Separator: true),
            new("Ajustar o quadro à tela", "IcTarget", () => Board.Fit()) { Tip = "Enquadra todos os itens na tela · Ctrl+1" },
            new("Buscar no quadro (Ctrl+F)", "IcTarget", OpenSearch),
            new("Renomear quadro", "IcPencil", StartRename),
            new("Mostrar grade", "IcGrid", () => _vm.ShowGrid = !_vm.ShowGrid, Separator: true, Checked: _vm.ShowGrid),
            new("Modo diagrama", "IcLine", () => _vm.DiagramMode = !_vm.DiagramMode, Checked: _vm.DiagramMode),
            new("Somente visualização", "IcLock", () => _vm.ReadOnly = !_vm.ReadOnly, Checked: _vm.ReadOnly)
                { Tip = "Bloqueia o quadro: dá para ver e navegar, mas nada se move, cria, edita ou apaga" },
            new(_vm.TitleHidden ? "Mostrar título" : "Ocultar título", "IcTitle", () => _vm.TitleHidden = !_vm.TitleHidden),
            new("Cenas e apresentação…", "IcScenes", () => ShowScenes(p), Separator: true),
            new("Apresentar", "IcPresent", StartPresenting),
            new(_fullscreen ? "Sair da tela cheia" : "Tela cheia", _fullscreen ? "IcExitFullscreen" : "IcFullscreen", () => SetFullscreen(!_fullscreen))
                { Tip = "Deixa o quadro em tela cheia · F11" },
            new("Exportar quadro (PNG, PDF)…", "IcExport", () => Export(selectionOnly: false), Separator: true),
        };
        if (sel.Count > 0) list.Add(new("Exportar seleção…", "IcExport", () => Export(selectionOnly: true)));
        list.Add(new("Abrir a pasta do cofre", "IcFolder", () => ItemViewBase.OpenExternal(StorageService.Root), Separator: true));
        ShowActions(p, list, MoreButton);
    }

    // ---------- cenas e apresentação ----------

    void ShowScenes(Point at)
    {
        var list = new List<ActionItem> { ActionItem.Header("Cenas deste quadro") };
        foreach (var s in _vm.Scenes)
        {
            var scene = s;
            list.Add(new(scene.Nome, "IcTarget", () => Board.GoTo(scene.X, scene.Y, scene.Zoom)));
        }
        if (_vm.Scenes.Count == 0) list.Add(ActionItem.Header("Nenhuma ainda: enquadre o quadro e salve a vista"));
        list.Add(new("Salvar a vista atual como cena", "IcPlus", () => _vm.AddScene($"Cena {_vm.Scenes.Count + 1}"), Separator: true));
        if (_vm.Scenes.Count > 0)
        {
            list.Add(new("Apresentar (setas ← →)", "IcPresent", StartPresenting));
            list.Add(new("Apagar a última cena", "IcTrash", () => _vm.RemoveScene(_vm.Scenes[^1]), Danger: true, Separator: true));
        }
        Dispatcher.BeginInvoke(() => ShowActions(at, list), DispatcherPriority.Input);
    }

    bool _presenting, _presentMadeFullscreen;
    int _scene;

    void StartPresenting()
    {
        if (_presenting) return;
        _presenting = true;
        CloseSearch();
        _vm.EndEdit();
        _vm.ClearSelection();
        Board.Tool = BoardTool.Select;
        _presentMadeFullscreen = !_fullscreen;
        if (!_fullscreen) SetFullscreen(true);
        foreach (var el in new UIElement[] { Hint, Tray, Status, Palette }) el.Visibility = Visibility.Collapsed;
        PresentBar.Visibility = Visibility.Visible;
        _scene = 0;
        GoScene(0);
        Board.Focus();
    }

    void StopPresenting()
    {
        _presenting = false;
        PresentBar.Visibility = Visibility.Collapsed;
        Status.Visibility = Visibility.Visible;
        ApplyReadOnly(); // o dock volta, a não ser que o quadro esteja só para visualização
        UpdateHint();
        if (_presentMadeFullscreen) SetFullscreen(false);
    }

    void GoScene(int delta)
    {
        int count = Math.Max(1, _vm.Scenes.Count);
        _scene = Math.Clamp(_scene + delta, 0, count - 1);
        if (_vm.Scenes.Count == 0) Board.Fit();
        else
        {
            var s = _vm.Scenes[_scene];
            Board.GoTo(s.X, s.Y, s.Zoom, 0.9);
        }
        PresentText.Text = _vm.Scenes.Count == 0 ? "Quadro inteiro" : $"{_vm.Scenes[_scene].Nome} · {_scene + 1} de {count}";
    }

    void OnPresentPrev(object sender, RoutedEventArgs e) => GoScene(-1);
    void OnPresentNext(object sender, RoutedEventArgs e) => GoScene(+1);
    void OnPresentExit(object sender, RoutedEventArgs e) => StopPresenting();

    // ---------- busca no quadro (Ctrl+F) ----------

    int _hit = -1;

    void OpenSearch()
    {
        SearchBar.Visibility = Visibility.Visible;
        UpdateHint();
        Dispatcher.BeginInvoke(() => { SearchBox.Focus(); SearchBox.SelectAll(); }, DispatcherPriority.Input);
        RefreshBoardGlass();
    }

    void CloseSearch()
    {
        if (SearchBar.Visibility != Visibility.Visible) return;
        SearchBar.Visibility = Visibility.Collapsed;
        UpdateHint();
        _vm.BoardSearch = "";
        Board.Focus();
    }

    void UpdateSearchCount()
    {
        int n = _vm.BoardSearchHits.Count;
        SearchCount.Text = (_vm.BoardSearch ?? "").Trim().Length == 0 ? "" : n == 0 ? "nada" : _hit >= 0 ? $"{_hit + 1} de {n}" : n == 1 ? "1 item" : $"{n} itens";
    }

    void JumpHit(int dir)
    {
        var hits = _vm.BoardSearchHits;
        if (hits.Count == 0) return;
        _hit = ((_hit < 0 && dir < 0 ? 0 : _hit + dir) % hits.Count + hits.Count) % hits.Count;
        Board.FocusItem(hits[_hit]); // a câmera voa até o resultado
        UpdateSearchCount();
    }

    void OnSearchKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { JumpHit(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : +1); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseSearch(); e.Handled = true; }
    }

    void OnSearchPrev(object sender, RoutedEventArgs e) => JumpHit(-1);
    void OnSearchNext(object sender, RoutedEventArgs e) => JumpHit(+1);
    void OnSearchClose(object sender, RoutedEventArgs e) => CloseSearch();

    // ---------- exportar ----------

    void Export(bool selectionOnly)
    {
        var items = selectionOnly ? _vm.SelectedItems.ToList() : _vm.Items.ToList();
        if (items.Count == 0) return;
        var dlg = new SaveFileDialog
        {
            Title = selectionOnly ? "Exportar seleção" : "Exportar quadro",
            FileName = _vm.BoardTitle + (selectionOnly ? " (seleção)" : ""),
            Filter = "Imagem PNG|*.png|Documento PDF|*.pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(this) != true) return;
        var sel = _vm.SelectedItems.ToList();
        _vm.ClearSelection(); // o contorno azul não vai para a imagem
        var image = Board.RenderRegion(items, 2, onlyThese: selectionOnly);
        _vm.SelectMany(sel, add: false);
        try
        {
            if (dlg.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) ExportService.SavePdf(image, dlg.FileName);
            else ExportService.SavePng(image, dlg.FileName);
        }
        catch (IOException) { MessageBox.Show(this, "Não foi possível salvar o arquivo (ele está aberto em outro programa?).", "Quadtab"); }
    }

    /// <summary>Miniatura do quadro para a galeria (sem seleção).</summary>
    public void MakeThumbnail()
    {
        try
        {
            if (_vm.Items.Count == 0) return;
            var area = _vm.Items.Aggregate(Rect.Empty, (r, i) => { r.Union(i.Bounds); return r; });
            double scale = Math.Min(1, 560 / Math.Max(1, Math.Max(area.Width, area.Height)));
            var sel = _vm.SelectedItems.ToList();
            foreach (var i in sel) i.IsSelected = false;
            var image = Board.RenderRegion(_vm.Items.ToList(), scale, onlyThese: false);
            foreach (var i in sel) i.IsSelected = true;
            var path = StorageService.ThumbPath(_vm.FilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ExportService.SavePng(image, path);
        }
        catch { } // miniatura é opcional
    }

    // ---------- páginas (editor ao lado) ----------

    public void OpenPage(string path)
    {
        if (!File.Exists(path)) return;
        Editor.Open(path);
        ApplyEditorLayout();
        StorageService.Touch(path);
        App.Library.Refresh();
    }

    void ClosePage()
    {
        Editor.Close();
        ApplyEditorLayout();
    }

    void ApplyEditorLayout()
    {
        bool open = Editor.Visibility == Visibility.Visible;
        bool expanded = open && Editor.Expanded;
        EditorColumn.Width = open ? new GridLength(expanded ? 1 : 0.85, GridUnitType.Star) : new GridLength(0);
        BoardColumn.Width = expanded ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Editor.Margin = new Thickness(expanded ? 0 : 14, 0, 0, 0);
        BoardArea.Visibility = expanded ? Visibility.Hidden : Visibility.Visible;
    }

    public void PageRenamed(string oldPath, string newPath)
    {
        Editor.Renamed(oldPath, newPath);
        App.Library.Refresh();
    }

    public void FlushPage() => Editor.Flush();

    // ---------- tela cheia (F11): lateral e barra se escondem e voltam pela borda ----------

    bool _fullscreen;
    Rect _restoreBounds;
    WindowState _restoreState;

    void SetFullscreen(bool on)
    {
        if (on == _fullscreen) return;
        _fullscreen = on;
        StorageService.State.TelaCheia = on && !_presenting;
        StorageService.SaveState();
        if (on)
        {
            _restoreState = WindowState;
            _restoreBounds = new Rect(Left, Top, Width, Height);
            WindowState = WindowState.Normal;
            var mon = MonitorBounds();
            Left = mon.Left; Top = mon.Top;
            Width = mon.Width; Height = mon.Height;
            ResizeMode = ResizeMode.NoResize;
            // Tela realmente cheia: o quadro vai de borda a borda; lateral e barra flutuam por cima.
            AppRoot.Margin = new Thickness(0);
            Board.FullBleed = true;
        }
        else
        {
            ResizeMode = ResizeMode.CanResize;
            Left = _restoreBounds.Left; Top = _restoreBounds.Top;
            Width = _restoreBounds.Width; Height = _restoreBounds.Height;
            WindowState = _restoreState;
            AppRoot.Margin = new Thickness(14);
            Board.FullBleed = false;
        }
        ApplySidebar(animate: true);
        GlassPanel.ShadowsEnabled = !on; // na tela cheia, sem sombras (ficavam cortadas sobre o quadro)
        ApplyTitle(animate: true);
        RefreshBoardGlass();
    }

    Rect MonitorBounds()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (hwnd == IntPtr.Zero || !GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info))
            return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        double s = GetDpiForWindow(hwnd) / 96.0;
        var r = info.rcMonitor;
        return new Rect(r.Left / s, r.Top / s, (r.Right - r.Left) / s, (r.Bottom - r.Top) / s);
    }

    // ---------- balão do zoom (some 1,2 s depois que o zoom para) ----------

    readonly DispatcherTimer _zoomTimer;
    DateTime _quietZoomUntil;
    bool _zoomShown;

    void ShowZoom()
    {
        if (DateTime.Now < _quietZoomUntil) return;
        _zoomTimer.Stop();
        _zoomTimer.Start();
        if (_zoomShown) return;
        _zoomShown = true;
        ZoomPanel.Visibility = Visibility.Visible;
        Anim.Set(ZoomScale, ScaleTransform.ScaleXProperty, 0.9);
        Anim.Set(ZoomScale, ScaleTransform.ScaleYProperty, 0.9);
        Anim.To(ZoomScale, ScaleTransform.ScaleXProperty, 1, 0.4, Anim.Pop);
        Anim.To(ZoomScale, ScaleTransform.ScaleYProperty, 1, 0.4, Anim.Pop);
        Anim.To(ZoomPanel, OpacityProperty, 1, 0.18, Anim.Ease);
        Board.RefreshGlass();
    }

    void HideZoom()
    {
        _zoomShown = false;
        Anim.To(ZoomPanel, OpacityProperty, 0, 0.25, Anim.Ease, () =>
        {
            if (!_zoomShown) ZoomPanel.Visibility = Visibility.Collapsed;
        });
    }

    // ---------- dock da bandeja que se esconde ----------

    readonly DispatcherTimer _dockTimer;
    bool _dockShown;

    /// <summary>Mouse a menos de 80 px da borda de baixo, na faixa central do quadro.</summary>
    void DockNear(Point p)
    {
        double w = BoardArea.ActualWidth, h = BoardArea.ActualHeight;
        bool near = p.X >= 0 && p.Y <= h && h - p.Y <= 80 && Math.Abs(p.X - w / 2) <= Math.Max(260, Tray.DockGlass.ActualWidth / 2 + 80) && !_presenting;
        if (near || KeepDock())
        {
            _dockTimer.Stop();
            if (near) SetDock(true);
        }
        else if (_dockShown && !_dockTimer.IsEnabled) _dockTimer.Start();
    }

    /// <summary>O dock fica à mostra: bandeja aberta, algo sendo arrastado dela ou mouse em cima.</summary>
    bool KeepDock() => _vm.TrayOpen || Tray.IsDragging || Tray.IsMouseOver;

    void SetDock(bool show)
    {
        if (show == _dockShown) return;
        _dockShown = show;
        if (show) Anim.To(TrayShift, TranslateTransform.YProperty, 0, 0.55, Anim.Panel);
        else Anim.To(TrayShift, TranslateTransform.YProperty, DockHiddenY, 0.4, Anim.Ease);
        TrackGlass(0.6);
    }

    // Enquanto o dock anda, o fundo desfocado do vidro acompanha a cada quadro.
    DateTime _trackUntil;
    bool _tracking;

    void TrackGlass(double seconds)
    {
        _trackUntil = DateTime.Now.AddSeconds(seconds);
        if (_tracking) return;
        _tracking = true;
        CompositionTarget.Rendering += OnTrackGlass;
    }

    void OnTrackGlass(object? sender, EventArgs e)
    {
        Board.RefreshGlass();
        SidebarGlass.RefreshBackdrop();
        if (DateTime.Now < _trackUntil) return;
        _tracking = false;
        CompositionTarget.Rendering -= OnTrackGlass;
    }

    // ---------- itens do quadro aberto na lateral ----------

    readonly DispatcherTimer _itemsTimer;

    void OnItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        _itemsTimer.Stop();
        _itemsTimer.Start();
    }

    // ---------- tema: as manchas do fundo mudam de cor suavemente ----------

    Color[]? _blobColors;

    void AnimateBlobs()
    {
        var to = new[] { "BgColor", "Blob1Color", "Blob2Color", "Blob3Color" }.Select(k => (Color)FindResource(k)).ToArray();
        var dps = new[] { BackdropBlobs.BgProperty, BackdropBlobs.Blob1Property, BackdropBlobs.Blob2Property, BackdropBlobs.Blob3Property };
        var from = _blobColors ?? dps.Select(d => (Color)Blobs.GetValue(d)).ToArray();
        for (int i = 0; i < 4; i++)
            Blobs.BeginAnimation(dps[i], new System.Windows.Media.Animation.ColorAnimation(from[i], to[i], TimeSpan.FromSeconds(SystemParameters.ClientAreaAnimation ? 0.3 : 0)));
        _blobColors = to;
    }

    // ---------- Procurar (Ctrl+K) e Ajustes ----------

    /// <summary>Caixa de busca de vidro no centro: quadros, notas e textos.</summary>
    public void ShowSearch()
    {
        CloseActions();
        Quick.Show();
    }

    public void ShowSettings(Point at, FrameworkElement? owner = null)
    {
        var s = StorageService.State;
        ShowActions(at,
        [
            ActionItem.Header("Ajustes"),
            new("Seguir o tema do Windows", "IcSettings", ThemeService.FollowSystem, Checked: s.Tema is null) { Tip = "Volta a usar o tema claro ou escuro do Windows" },
            new("Faíscas ao clicar", "IcStar", () =>
            {
                s.Faiscas = !s.Faiscas;
                Sparks.Enabled = s.Faiscas;
                StorageService.SaveState();
            }, Checked: s.Faiscas) { Tip = "Liga ou desliga as faíscas que saem do ponteiro a cada clique" },
            new("Contador de FPS", "IcGrid", ToggleFps, Checked: FpsPanel.Visibility == Visibility.Visible) { Tip = "Mostra a velocidade do quadro · Ctrl+Shift+F" },
            new("Abrir a pasta Cofre", "IcFolder", () => ItemViewBase.OpenExternal(StorageService.Root), Separator: true) { Tip = "Abre no Explorador a pasta onde tudo fica guardado" },
        ], owner);
    }

    // ---------- teclado ----------

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control), shift = mods.HasFlag(ModifierKeys.Shift);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Atalhos que valem em qualquer lugar
        switch (key)
        {
            case Key.F11: SetFullscreen(!_fullscreen); e.Handled = true; return;
            case Key.O or Key.K when ctrl: ShowSearch(); e.Handled = true; return;
            case Key.Left when mods == ModifierKeys.Alt: App.Library.GoBackCommand.Execute(null); e.Handled = true; return;
            case Key.Right when mods == ModifierKeys.Alt: App.Library.GoForwardCommand.Execute(null); e.Handled = true; return;
            case Key.F when ctrl && shift: ToggleFps(); e.Handled = true; return;
        }

        // Sem nenhum quadro: só criar um novo (o resto dos atalhos não tem onde agir).
        if (_emptyMode && !App.Library.GalleryOpen)
        {
            if (key == Key.N && ctrl) { App.Library.NewBoard(); e.Handled = true; }
            return;
        }

        if (Keyboard.FocusedElement is TextBox or RichTextBox)
        {
            if (key == Key.Escape && (_menuOpen || _actionsOpen || _ctxOpen)) { CloseMenu(); CloseActions(); CloseContextTray(); e.Handled = true; }
            return;
        }
        if (Quick.IsOpen) return;

        if (_presenting)
        {
            switch (key)
            {
                case Key.Right or Key.Down or Key.PageDown or Key.Space or Key.Enter: GoScene(+1); e.Handled = true; return;
                case Key.Left or Key.Up or Key.PageUp or Key.Back: GoScene(-1); e.Handled = true; return;
                case Key.Escape: StopPresenting(); e.Handled = true; return;
            }
        }

        // Somente visualização: só navegação (Esc, zoom, busca, Espaço); nada que mude o quadro.
        if (_vm.ReadOnly && !(key is Key.Escape or Key.Space || (ctrl && key is Key.F or Key.N or Key.C or Key.D0 or Key.NumPad0
                or Key.D1 or Key.NumPad1 or Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract)))
            return;

        switch (key)
        {
            case Key.Escape:
                CloseMenu(); CloseActions(); CloseContextTray();
                if (Board.Tool != BoardTool.Select) Board.Tool = BoardTool.Select;
                else if (SearchBar.Visibility == Visibility.Visible) CloseSearch();
                else if (App.Library.GalleryOpen) App.Library.GalleryOpen = false;
                else { _vm.EndEdit(); _vm.ClearSelection(); }
                break;
            case Key.Delete or Key.Back when _vm.SelectionCount > 0:
                CloseMenu();
                _vm.RemoveSelected();
                break;
            case Key.A when ctrl: _vm.SelectAll(); break;
            case Key.Z when ctrl && shift: _vm.Redo(); break;
            case Key.Z when ctrl: _vm.Undo(); break;
            case Key.Y when ctrl: _vm.Redo(); break;
            case Key.C when ctrl: _vm.Copy(); break;
            case Key.X when ctrl: _vm.Cut(); break;
            case Key.V when ctrl: MediaService.Paste(_vm, Board.PointerWorld); break;
            case Key.D when ctrl && _vm.SelectionCount > 0: _vm.DuplicateMany(_vm.SelectedItems.ToList(), new Vector(30, 30)); break;
            case Key.G when ctrl && shift: _vm.Ungroup(); break;
            case Key.G when ctrl: _vm.GroupSelection(); break;
            case Key.L when ctrl && _vm.Selected is { } locked: _vm.ToggleLock(locked); break;
            case Key.F when ctrl: OpenSearch(); break;
            case Key.N when ctrl: App.Library.NewBoard(); break;
            case Key.OemCloseBrackets when ctrl: if (shift) _vm.ToTop(_vm.SelectedItems.ToList()); else foreach (var i in _vm.SelectedItems.ToList()) _vm.Step(i, +1); break;
            case Key.OemOpenBrackets when ctrl: if (shift) _vm.ToBottom(_vm.SelectedItems.ToList()); else foreach (var i in _vm.SelectedItems.ToList()) _vm.Step(i, -1); break;
            case Key.F2:
                if (_vm.Selected is { } edit)
                {
                    if (edit.Kind != ItemKind.Line) _vm.BeginEdit(edit, EditField.Text); // linhas: sem nome por enquanto
                }
                else StartRename();
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down when _vm.SelectionCount > 0:
                {
                    // Setas empurram a seleção 1 px (Shift: 10 px).
                    double step = shift ? 10 : 1;
                    var d = key switch { Key.Left => new Vector(-step, 0), Key.Right => new Vector(step, 0), Key.Up => new Vector(0, -step), _ => new Vector(0, step) };
                    _vm.Checkpoint(coalesce: true);
                    foreach (var i in _vm.SelectedItems.Where(i => !i.Locked).ToList()) { i.X += d.X; i.Y += d.Y; }
                    _vm.CommitMove();
                    break;
                }
            case Key.Space:
                if (!e.IsRepeat) Board.SpaceHeld = true; // Espaço + arrastar move o quadro
                break;
            case Key.D0 or Key.NumPad0 when ctrl: Board.ZoomReset(); break;
            case Key.D1 or Key.NumPad1 when ctrl: Board.Fit(); break;
            case Key.OemPlus or Key.Add when ctrl: Board.ZoomStep(+1); break;
            case Key.OemMinus or Key.Subtract when ctrl: Board.ZoomStep(-1); break;
            default: return;
        }
        e.Handled = true;
    }

    // ---------- janela ----------

    void OnDragArea(object sender, MouseButtonEventArgs e) => DragArea(e);

    /// <summary>Arrasta a janela (dois cliques maximizam).</summary>
    public void DragArea(MouseButtonEventArgs e)
    {
        if (_fullscreen) return;
        if (e.ClickCount == 2) ToggleMaximize();
        else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void OnMaximize(object sender, RoutedEventArgs e) => ToggleMaximize();
    void OnClose(object sender, RoutedEventArgs e) => CloseApp();

    bool _closing;

    /// <summary>Fecha o app, não importa o que esteja aberto (salva o que der antes).</summary>
    void CloseApp()
    {
        if (_closing) return;
        _closing = true;
        SaveAll();
        Application.Current.Shutdown();
    }

    // Salvar nunca impede de fechar: um erro aqui fica só registrado.
    void SaveAll()
    {
        try { EndRename(true); } catch (Exception ex) { App.Log(ex); }
        try { _vm.Flush(); } catch (Exception ex) { App.Log(ex); }
        try { Editor.Flush(); } catch (Exception ex) { App.Log(ex); }
    }

    void ToggleMaximize()
    {
        if (_fullscreen) SetFullscreen(false);
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    // Maximizada, a janela sem moldura passa um pouco da tela: compensa com uma margem.
    void ApplyWindowState()
    {
        bool max = WindowState == WindowState.Maximized;
        MaxIcon.Data = (Geometry)FindResource(max ? "IcWinRestore" : "IcWinMax");
        if (!max)
        {
            WindowRoot.Margin = default;
            return;
        }
        var hwnd = new WindowInteropHelper(this).Handle;
        uint dpi = GetDpiForWindow(hwnd);
        double scale = dpi / 96.0;
        double frame = (GetSystemMetricsForDpi(32, dpi) + GetSystemMetricsForDpi(92, dpi)) / scale; // SM_CXSIZEFRAME + SM_CXPADDEDBORDER
        WindowRoot.Margin = new Thickness(frame);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
}
