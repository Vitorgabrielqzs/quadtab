using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Quadtab.Services;
using Quadtab.ViewModels;

namespace Quadtab.Controls;

/// <summary>Galeria "Todos os quadros" (e Recentes, Favoritos, Apagados recentemente).</summary>
public partial class Gallery : UserControl
{
    public Gallery() => InitializeComponent();

    LibraryViewModel Lib => App.Library;

    void OnNewBoard(object sender, RoutedEventArgs e) => Lib.NewBoard();

    void OnCardClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GalleryCard { IsTrash: false } c) App.OpenBoard(c.Path);
    }

    void OnCardRightClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not GalleryCard { IsTrash: false } c || Window.GetWindow(this) is not MainWindow shell) return;
        shell.ShowActions(e.GetPosition(shell.Root),
        [
            new("Abrir", "IcBoard", () => App.OpenBoard(c.Path)) { Tip = "Abre este quadro" },
            new("Renomear", "IcPencil", () => { Lib.GalleryOpen = false; Lib.StartRename(c.Path); }) { Tip = "Muda o nome do quadro" },
            new("Duplicar", "IcDuplicate", () => Lib.Duplicate(c.Path)) { Tip = "Cria uma cópia deste quadro" },
            new(StorageService.IsPinned(c.Path) ? "Desafixar" : "Fixar", "IcPin", () => Lib.TogglePinned(c.Path)) { Tip = "Fixa ou desafixa no topo da barra lateral" },
            new(c.IsFavorite ? "Tirar dos importantes" : "Marcar como importante", "IcStar", () => Lib.ToggleFavorite(c.Path)) { Tip = "Marca ou desmarca como importante" },
            new("Apagar", "IcTrash", () => Lib.Trash(c.Path), Danger: true, Separator: true) { Tip = "Manda o quadro para Apagados recentemente" },
        ]);
        e.Handled = true;
    }

    void OnRestore(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GalleryCard { Trash: { } t }) Lib.Restore(t);
        e.Handled = true;
    }

    void OnDeleteForever(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GalleryCard { Trash: { } t }) Lib.DeleteForever(t);
        e.Handled = true;
    }
}
