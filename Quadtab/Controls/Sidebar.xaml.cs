using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Quadtab.Rendering;
using Quadtab.Services;
using Quadtab.ViewModels;

namespace Quadtab.Controls;

/// <summary>
/// Barra lateral no estilo da lateral do app Claude: ações no topo, quadros pelo último aberto (com os
/// itens de cada um recuados), Fixados, Importantes, Páginas e a árvore da pasta Cofre.
/// </summary>
public partial class Sidebar : UserControl
{
    Point _dragStart;
    object? _dragData;

    public Sidebar()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyThemeIcon(animate: false);
        ThemeService.Changed += () => ApplyThemeIcon(animate: true);
    }

    /// <summary>Primeira linha de quadro (conferência dos balões).</summary>
    internal FrameworkElement FindFirstRow()
    {
        static FrameworkElement? Walk(DependencyObject d)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var c = VisualTreeHelper.GetChild(d, i);
                if (c is Button { Tag: SideNode { Kind: SideKind.Board } } b) return b;
                if (Walk(c) is { } r) return r;
            }
            return null;
        }
        return Walk(this) ?? this;
    }

    MainWindow? Shell => Window.GetWindow(this) as MainWindow;
    LibraryViewModel Lib => App.Library;

    void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == sender) Shell?.DragArea(e);
    }

    // ---------- ações ----------

    void OnNewBoard(object sender, RoutedEventArgs e) => Lib.NewBoard();
    void OnSearch(object sender, RoutedEventArgs e) => Shell?.ShowSearch();
    void OnTrash(object sender, RoutedEventArgs e) => Lib.ShowGalleryCommand.Execute("apagados");

    // ---------- linhas ----------

    static SideNode? NodeOf(object sender) => (sender as FrameworkElement)?.Tag as SideNode;

    void OnNodeClick(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is not { IsRenaming: false } n) return;
        switch (n.Kind)
        {
            case SideKind.Board: App.OpenBoard(n.Path); break;
            case SideKind.Page or SideKind.Heading: App.OpenPage(n.Path); break;
            case SideKind.Item: App.FocusItem(n.Path, n.ItemId ?? 0); break;
            case SideKind.Folder: n.IsOpen = !n.IsOpen; break;
            case SideKind.File: OpenFile(n.Path); break;
            case SideKind.More: OnMore(n.Path); break;
        }
    }

    static void OpenFile(string path)
    {
        if (path.EndsWith(StorageService.BoardExt, StringComparison.OrdinalIgnoreCase)) App.OpenBoard(path);
        else if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) App.OpenPage(path);
        else ItemViewBase.OpenExternal(path);
    }

    void OnMore(string what)
    {
        if (what.StartsWith("abrir:", StringComparison.Ordinal)) App.OpenBoard(what[6..]);
        else if (what == "paginas") Shell?.ShowSearch();
        else Lib.ShowGalleryCommand.Execute(what);
    }

    void OnToggleNode(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } n) n.IsOpen = !n.IsOpen;
        e.Handled = true;
    }

    void OnNodeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (NodeOf(sender) is { HasActions: true } n) Lib.StartRename(n.Path);
        e.Handled = true;
    }

    void OnNodeKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && NodeOf(sender) is { HasActions: true } n)
        {
            Lib.StartRename(n.Path);
            e.Handled = true;
        }
    }

    void OnPin(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } n) Lib.TogglePinned(n.Path);
        e.Handled = true;
    }

    void OnStar(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } n) Lib.ToggleFavorite(n.Path);
        e.Handled = true;
    }

    void OnNodeMore(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } n && sender is FrameworkElement b && Shell is { } shell)
            shell.ShowActions(b.TranslatePoint(new Point(b.ActualWidth, 0), shell.Root), ActionsFor(n), b);
        e.Handled = true;
    }

    void OnNodeRightClick(object sender, MouseButtonEventArgs e)
    {
        if (NodeOf(sender) is not { } n || Shell is not { } shell) return;
        var items = ActionsFor(n);
        if (items.Count > 0) shell.ShowActions(e.GetPosition(shell.Root), items);
        e.Handled = true;
    }

    List<ActionItem> ActionsFor(SideNode n) => n.Kind switch
    {
        SideKind.Board =>
        [
            new("Abrir", "IcBoard", () => App.OpenBoard(n.Path)) { Tip = "Abre este quadro" },
            new("Renomear", "IcPencil", () => Lib.StartRename(n.Path)) { Tip = "Muda o nome do quadro · F2" },
            new("Duplicar", "IcDuplicate", () => Lib.Duplicate(n.Path)) { Tip = "Cria uma cópia deste quadro" },
            new(n.IsPinned ? "Desafixar" : "Fixar", "IcPin", () => Lib.TogglePinned(n.Path)) { Tip = n.PinTip },
            new(n.IsImportant ? "Tirar dos importantes" : "Marcar como importante", "IcStar", () => Lib.ToggleFavorite(n.Path)) { Tip = n.StarTip },
            new("Excluir", "IcTrash", () => Lib.Trash(n.Path), Danger: true, Separator: true) { Tip = "Manda o quadro para Apagados recentemente" },
        ],
        SideKind.Page =>
        [
            new("Abrir", "IcPage", () => App.OpenPage(n.Path)) { Tip = "Abre a página ao lado do quadro" },
            new("Renomear", "IcPencil", () => Lib.StartRename(n.Path)) { Tip = "Muda o nome da página e dos links para ela · F2" },
            new(n.IsPinned ? "Desafixar" : "Fixar", "IcPin", () => Lib.TogglePinned(n.Path)) { Tip = n.PinTip },
            new(n.IsImportant ? "Tirar dos importantes" : "Marcar como importante", "IcStar", () => Lib.ToggleFavorite(n.Path)) { Tip = n.StarTip },
            new("Excluir", "IcTrash", () => StorageService.TrashPage(n.Path), Danger: true, Separator: true) { Tip = "Manda a página para a lixeira" },
        ],
        SideKind.Folder =>
        [
            new("Novo quadro aqui", "IcPlus", () => Lib.NewBoard(n.Path)) { Tip = "Cria um quadro dentro desta pasta" },
            new("Nova pasta aqui", "IcFolderPlus", () => Lib.NewFolder(n.Path)) { Tip = "Cria uma pasta dentro desta" },
            new("Mostrar no Explorador", "IcFolder", () => ItemViewBase.OpenExternal(n.Path)) { Tip = "Abre a pasta no Explorador de Arquivos" },
        ],
        SideKind.File =>
        [
            new("Abrir", "IcFile", () => OpenFile(n.Path)) { Tip = "Abre o arquivo" },
            new("Mostrar no Explorador", "IcFolder", () => ItemViewBase.OpenExternal(Path.GetDirectoryName(n.Path)!)) { Tip = "Abre a pasta do arquivo no Explorador" },
        ],
        _ => [],
    };

    // arrastar um quadro do Cofre para uma pasta de quadros
    void OnNodeMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragData = NodeOf(sender);
    }

    void OnNodeMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragData is not SideNode n) return;
        if ((e.GetPosition(this) - _dragStart).Length < 6) return;
        _dragData = null;
        var board = n.Kind == SideKind.Board || (n.Kind == SideKind.File && n.Path.EndsWith(StorageService.BoardExt, StringComparison.OrdinalIgnoreCase));
        if (board) DragDrop.DoDragDrop((DependencyObject)sender, new DataObject("QuadtabBoard", n.Path), DragDropEffects.Move);
    }

    void OnNodeDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData("QuadtabBoard") is not string board || NodeOf(sender) is not { Kind: SideKind.Folder } target) return;
        Lib.Move(board, target.Path);
        e.Handled = true;
    }

    void OnRootDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData("QuadtabBoard") is string board) Lib.Move(board, StorageService.BoardsDir);
    }

    // renomear na linha
    void OnRenameVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } tb)
            Dispatcher.BeginInvoke(() => { tb.Focus(); tb.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    void OnRenameKey(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { Tag: SideNode n } tb) return;
        if (e.Key == Key.Enter) { Lib.CommitRename(n, tb.Text); e.Handled = true; }
        else if (e.Key == Key.Escape) { n.IsRenaming = false; e.Handled = true; }
    }

    void OnRenameLost(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { Tag: SideNode { IsRenaming: true } n } tb) Lib.CommitRename(n, tb.Text);
    }

    // ---------- rodapé: tema e Ajustes ----------

    void OnTheme(object sender, RoutedEventArgs e) => ThemeService.Toggle();

    /// <summary>Sol no tema escuro (vai para o claro) e lua no claro; troca com giro e mola.</summary>
    void ApplyThemeIcon(bool animate)
    {
        ThemeIcon.Data = (Geometry)FindResource(ThemeService.IsDark ? "IcSun" : "IcMoon");
        if (!animate) return;
        Anim.Set(ThemeTurn, System.Windows.Media.RotateTransform.AngleProperty, -90.0);
        Anim.Set(ThemeScale, ScaleTransform.ScaleXProperty, 0.6);
        Anim.Set(ThemeScale, ScaleTransform.ScaleYProperty, 0.6);
        Anim.To(ThemeTurn, System.Windows.Media.RotateTransform.AngleProperty, 0, 0.55, Anim.Tap);
        Anim.To(ThemeScale, ScaleTransform.ScaleXProperty, 1, 0.5, Anim.Tap);
        Anim.To(ThemeScale, ScaleTransform.ScaleYProperty, 1, 0.5, Anim.Tap);
    }

    void OnSettings(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement b || Shell is not { } shell) return;
        shell.ShowSettings(b.TranslatePoint(new Point(0, -8), shell.Root), b);
    }
}
