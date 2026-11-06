using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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

    MainWindow? Shell => Window.GetWindow(this) as MainWindow;
    LibraryViewModel Lib => App.Library;

    void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == sender) Shell?.DragArea(e);
    }

    // ---------- ações ----------

    void OnNewBoard(object sender, RoutedEventArgs e) => Lib.NewBoard();
    void OnNewFolder(object sender, RoutedEventArgs e) => Lib.CreateFolder(boards: Lib.Checked);
    void OnDesigns(object sender, RoutedEventArgs e) => Shell?.ShowDesigns(DesignsButton);
    void OnSearch(object sender, RoutedEventArgs e) => Shell?.ShowSearch();
    void OnTrash(object sender, RoutedEventArgs e) => Lib.ShowGalleryCommand.Execute("apagados");

    // ---------- linhas ----------

    static SideNode? NodeOf(object sender) => (sender as FrameworkElement)?.Tag as SideNode;

    void OnNodeClick(object sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is not { IsRenaming: false } n) return;
        // Ctrl+clique marca vários quadros (para arrastar ou mover juntos); clique simples desmarca.
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && n.Kind == SideKind.Board)
        {
            Lib.ToggleChecked(n);
            return;
        }
        Lib.ClearChecked();
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
        if (what == "vazia") return;
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
        if (NodeOf(sender) is { CanRename: true } n) Lib.StartRename(n.Path);
        e.Handled = true;
    }

    void OnNodeKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && NodeOf(sender) is { CanRename: true } n)
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

    /// <summary>Quadros que uma ação vale: os marcados (se este estiver entre eles) ou só este.</summary>
    List<string> TargetsOf(SideNode n)
    {
        var marked = Lib.Checked;
        return n.IsChecked && marked.Count > 1 ? marked.ToList() : [n.Path];
    }

    /// <summary>Nome de uma pasta de quadros para os menus (subpastas com "›").</summary>
    static string FolderLabel(string folder) => StorageService.FolderKey(folder).Replace("\\", " › ");

    List<ActionItem> ActionsFor(SideNode n) => n.Kind switch
    {
        SideKind.Board => BoardActions(n),
        SideKind.Page =>
        [
            new("Abrir", "IcPage", () => App.OpenPage(n.Path)) { Tip = "Abre a nota ao lado do quadro" },
            new("Renomear", "IcPencil", () => Lib.StartRename(n.Path)) { Tip = "Muda o nome da nota e dos links para ela · F2" },
            new(n.IsPinned ? "Desafixar" : "Fixar", "IcPin", () => Lib.TogglePinned(n.Path)) { Tip = n.PinTip },
            new(n.IsImportant ? "Tirar dos importantes" : "Marcar como importante", "IcStar", () => Lib.ToggleFavorite(n.Path)) { Tip = n.StarTip },
            new("Excluir", "IcTrash", () => App.DeleteNote(n.Path), Danger: true, Separator: true) { Tip = "Apaga a nota de vez" },
        ],
        SideKind.Folder when n.IsBoardFolder =>
        [
            new("Novo quadro aqui", "IcPlus", () => Lib.NewBoard(n.Path)) { Tip = "Cria um quadro dentro desta pasta" },
            new("Nova pasta aqui", "IcFolderPlus", () => Lib.CreateFolder(n.Path)) { Tip = "Cria uma pasta dentro desta" },
            new("Renomear", "IcPencil", () => Lib.StartRename(n.Path)) { Tip = "Muda o nome da pasta · F2" },
            new("Mostrar no Explorador", "IcFolder", () => ItemViewBase.OpenExternal(n.Path)) { Tip = "Abre a pasta no Explorador de Arquivos" },
            new("Excluir pasta", "IcTrash", () => Lib.DeleteFolder(n.Path), Danger: true, Separator: true) { Tip = "Apaga a pasta; os quadros dela vão para Apagados recentemente" },
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

    /// <summary>Menu de um quadro: abrir, renomear..., e a hierarquia (retirar da pasta, mover, subir, descer).</summary>
    List<ActionItem> BoardActions(SideNode n)
    {
        var targets = TargetsOf(n);
        bool many = targets.Count > 1;
        string what = many ? $"os {targets.Count} quadros" : "o quadro";
        var folder = Path.GetDirectoryName(n.Path)!;
        bool inFolder = StorageService.FolderKey(n.Path).Length > 0;
        var list = new List<ActionItem>();
        if (!many)
        {
            list.Add(new("Abrir", "IcBoard", () => App.OpenBoard(n.Path)) { Tip = "Abre este quadro" });
            list.Add(new("Renomear", "IcPencil", () => Lib.StartRename(n.Path)) { Tip = "Muda o nome do quadro · F2" });
            list.Add(new("Duplicar", "IcDuplicate", () => Lib.Duplicate(n.Path)) { Tip = "Cria uma cópia deste quadro" });
            list.Add(new(n.IsPinned ? "Desafixar" : "Fixar", "IcPin", () => Lib.TogglePinned(n.Path)) { Tip = n.PinTip });
            list.Add(new(n.IsImportant ? "Tirar dos importantes" : "Marcar como importante", "IcStar", () => Lib.ToggleFavorite(n.Path)) { Tip = n.StarTip });
        }
        else list.Add(ActionItem.Header($"{targets.Count} quadros marcados (Ctrl+clique)"));

        // Hierarquia
        bool first = true;
        ActionItem Sep(ActionItem a) { var r = first ? a with { Separator = !many || list.Count > 1 } : a; first = false; return r; }
        if (inFolder)
        {
            list.Add(Sep(new ActionItem($"Retirar da pasta {Path.GetFileName(folder)}", "IcFolderOut", () => Lib.MoveInto(targets, StorageService.BoardsDir))
                { Tip = $"Tira {what} da pasta e põe na lista principal" }));
            if (!many)
            {
                var order = StorageService.FolderBoards(folder);
                int i = order.FindIndex(f => string.Equals(f, n.Path, StringComparison.OrdinalIgnoreCase));
                if (i > 0) list.Add(new("Subir na pasta", "IcArrowUp", () => Lib.Step(n.Path, -1)) { Tip = "Passa para cima do quadro anterior" });
                if (i >= 0 && i < order.Count - 1) list.Add(new("Descer na pasta", "IcArrowDown", () => Lib.Step(n.Path, +1)) { Tip = "Passa para baixo do próximo quadro" });
            }
        }
        foreach (var f in StorageService.BoardFolders().OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
        {
            var full = Path.Combine(StorageService.BoardsDir, f);
            if (string.Equals(full, folder, StringComparison.OrdinalIgnoreCase) && !many) continue;
            list.Add(Sep(new ActionItem($"Mover para a pasta {FolderLabel(full)}", "IcFolder", () => Lib.MoveInto(targets, full))
                { Tip = $"Põe {what} no fim desta pasta" }));
        }
        list.Add(Sep(new ActionItem("Nova pasta com " + (many ? "estes quadros" : "este quadro"), "IcFolderPlus", () => Lib.CreateFolder(boards: targets))
            { Tip = $"Cria uma pasta e põe {what} dentro dela" }));
        if (many) list.Add(new("Desmarcar todos", "IcClose", Lib.ClearChecked) { Tip = "Tira a marca dos quadros marcados" });
        else list.Add(new("Excluir", "IcTrash", () => Lib.Trash(n.Path), Danger: true, Separator: true) { Tip = "Manda o quadro para Apagados recentemente" });
        return list;
    }

    // ---------- arrastar quadros: para dentro de pastas, para fora e para mudar a ordem ----------

    const string DragFormat = "QuadtabBoards";

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
        if (!board) return;
        // Arrastando um quadro marcado, vão todos os marcados (na ordem em que foram marcados).
        var paths = n.Kind == SideKind.Board ? TargetsOf(n) : [n.Path];
        try { DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DragFormat, string.Join("\n", paths)), DragDropEffects.Move); }
        catch (Exception ex) { App.Log(ex); }
        finally { ClearDropMark(); }
    }

    static List<string>? DraggedBoards(DragEventArgs e) =>
        e.Data.GetData(DragFormat) is string s ? s.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList() : null;

    /// <summary>Onde soltar cai: dentro da pasta, ou antes/depois de um quadro.</summary>
    enum DropAt { None, Into, Before, After }

    static DropAt DropKind(SideNode target, DragEventArgs e, FrameworkElement row, List<string> dragged)
    {
        if (target.Kind == SideKind.Folder)
        {
            // Uma pasta nunca vai para dentro de si mesma (aqui só quadros são arrastados, mas por garantia).
            return dragged.Any(d => string.Equals(d, target.Path, StringComparison.OrdinalIgnoreCase)) ? DropAt.None : DropAt.Into;
        }
        if (target.Kind != SideKind.Board || target.Section != "quadros") return DropAt.None;
        if (dragged.Count == 1 && string.Equals(dragged[0], target.Path, StringComparison.OrdinalIgnoreCase)) return DropAt.None;
        // Na lista principal (fora de pastas) a ordem é a do último aberto: soltar ali só tira da pasta.
        if (StorageService.FolderKey(target.Path).Length == 0) return DropAt.Into;
        return e.GetPosition(row).Y < row.ActualHeight / 2 ? DropAt.Before : DropAt.After;
    }

    void OnNodeDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (DraggedBoards(e) is { } dragged && NodeOf(sender) is { } target && sender is FrameworkElement row)
        {
            var at = DropKind(target, e, row, dragged);
            if (at != DropAt.None)
            {
                e.Effects = DragDropEffects.Move;
                // Quadro solto na lista principal: o destaque fica na lista inteira (sem linha de posição).
                ShowDropMark(row, target.Kind == SideKind.Board && StorageService.FolderKey(target.Path).Length == 0 ? DropAt.Into : at);
            }
            else ClearDropMark();
        }
        e.Handled = true;
    }

    void OnNodeDragLeave(object sender, DragEventArgs e) => ClearDropMark();

    void OnNodeDrop(object sender, DragEventArgs e)
    {
        ClearDropMark();
        if (DraggedBoards(e) is not { } dragged || NodeOf(sender) is not { } target || sender is not FrameworkElement row) return;
        e.Handled = true;
        var at = DropKind(target, e, row, dragged);
        // Depois do soltar: o arraste do Windows termina antes de a lista ser refeita.
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                switch (at)
                {
                    case DropAt.Into when target.Kind == SideKind.Folder:
                        if (target.IsBoardFolder) Lib.MoveInto(dragged, target.Path);
                        else foreach (var d in dragged) Lib.Move(d, target.Path);
                        break;
                    case DropAt.Into: // quadro da lista principal: os arrastados saem das pastas
                        Lib.MoveInto(dragged, StorageService.BoardsDir);
                        break;
                    case DropAt.Before or DropAt.After:
                        Lib.MoveInto(dragged, Path.GetDirectoryName(target.Path)!, target.Path, after: at == DropAt.After);
                        break;
                }
            }
            catch (Exception ex) { App.Log(ex); }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    void OnRootDrop(object sender, DragEventArgs e)
    {
        ClearDropMark();
        if (e.Handled || DraggedBoards(e) is not { } dragged) return;
        e.Handled = true;
        Dispatcher.BeginInvoke(() => Lib.MoveInto(dragged, StorageService.BoardsDir), System.Windows.Threading.DispatcherPriority.Background);
    }

    // marca de onde o quadro vai cair: linha azul (antes/depois) ou fundo azul (dentro da pasta)
    DropMark? _mark;

    void ShowDropMark(FrameworkElement row, DropAt at)
    {
        if (_mark is { } m && m.AdornedElement == row) { m.Set(at); return; }
        ClearDropMark();
        if (AdornerLayer.GetAdornerLayer(row) is not { } layer) return;
        _mark = new DropMark(row);
        _mark.Set(at);
        layer.Add(_mark);
    }

    void ClearDropMark()
    {
        if (_mark is null) return;
        AdornerLayer.GetAdornerLayer(_mark.AdornedElement)?.Remove(_mark);
        _mark = null;
    }

    sealed class DropMark(UIElement row) : Adorner(row)
    {
        DropAt _at;
        static readonly Brush Accent = Freeze(new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)));
        static readonly Brush Soft = Freeze(new SolidColorBrush(Color.FromArgb(0x30, 0x0A, 0x84, 0xFF)));
        static readonly Pen Ring = Freeze(new Pen(Accent, 1.5));

        static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

        public void Set(DropAt at)
        {
            if (_at == at) return;
            _at = at;
            IsHitTestVisible = false;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var size = AdornedElement.RenderSize;
            switch (_at)
            {
                case DropAt.Into:
                    dc.DrawRoundedRectangle(Soft, Ring, new Rect(0.75, 0.75, size.Width - 1.5, size.Height - 1.5), 10, 10);
                    break;
                case DropAt.Before or DropAt.After:
                    double y = _at == DropAt.Before ? 0 : size.Height;
                    dc.DrawRoundedRectangle(Accent, null, new Rect(6, y - 1.5, size.Width - 12, 3), 1.5, 1.5);
                    dc.DrawEllipse(Brushes.White, new Pen(Accent, 2), new Point(6, y), 3.5, 3.5);
                    break;
            }
        }
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

    // ---------- página do Cofre ----------

    /// <summary>Troca a lista pela página do Cofre (e volta, clicando de novo ou no "‹").</summary>
    void OnVault(object sender, RoutedEventArgs e)
    {
        bool open = VaultPage.Visibility != Visibility.Visible;
        VaultPage.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        MainPage.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        // A página que entra desliza 16 px e ganha opacidade.
        FrameworkElement shown = open ? VaultPage : MainPage;
        var slide = new TranslateTransform(open ? 16 : -16, 0);
        shown.RenderTransform = slide;
        Anim.Set(shown, OpacityProperty, 0.0);
        Anim.To(shown, OpacityProperty, 1, 0.25, Anim.Ease);
        Anim.To(slide, TranslateTransform.XProperty, 0, 0.45, Anim.Panel);
        if (open) Lib.Refresh();
        // Com a página aberta, o botão fica marcado (pílula de destaque).
        if (open) VaultButton.SetResourceReference(BackgroundProperty, "Fill2Brush");
        else VaultButton.Background = Brushes.Transparent;
    }

    void OnVaultExplorer(object sender, RoutedEventArgs e) => ItemViewBase.OpenExternal(StorageService.Root);

    void OnSettings(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement b || Shell is not { } shell) return;
        shell.ShowSettings(b.TranslatePoint(new Point(0, -8), shell.Root), b);
    }
}
