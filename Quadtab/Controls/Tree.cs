using System.Windows;
using System.Windows.Media;

namespace Quadtab.Controls;

/// <summary>Atalhos para subir na árvore visual.</summary>
public static class Tree
{
    public static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T t) return t;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    /// <summary>Primeiro valor de UI.Field encontrado subindo a partir do elemento clicado.</summary>
    public static string? FindField(DependencyObject? d, DependencyObject stopAt)
    {
        while (d is not null && d != stopAt)
        {
            if (Behaviors.UI.GetField(d) is { } f) return f;
            d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }
}
