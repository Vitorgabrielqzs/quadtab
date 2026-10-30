using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Quadtab.Models;
using Quadtab.Rendering;
using Quadtab.ViewModels;

namespace Quadtab.Controls;

/// <summary>Escolhe a tela de cada tipo de item.</summary>
public sealed class ItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? PostIt { get; set; }
    public DataTemplate? Page { get; set; }
    public DataTemplate? Block { get; set; }
    public DataTemplate? Text { get; set; }
    public DataTemplate? TextBox { get; set; }
    public DataTemplate? Shape { get; set; }
    public DataTemplate? Line { get; set; }
    public DataTemplate? Drawing { get; set; }
    public DataTemplate? Image { get; set; }
    public DataTemplate? Video { get; set; }
    public DataTemplate? File { get; set; }
    public DataTemplate? Link { get; set; }
    public DataTemplate? Table { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        (item as BoardItemViewModel)?.Kind switch
        {
            ItemKind.Page => Page,
            ItemKind.Block => Block,
            ItemKind.Text => Text,
            ItemKind.TextBox => TextBox,
            ItemKind.Shape => Shape,
            ItemKind.Line => Line,
            ItemKind.Drawing => Drawing,
            ItemKind.Image => Image,
            ItemKind.Video => Video,
            ItemKind.File => File,
            ItemKind.Link => Link,
            ItemKind.Table => Table,
            _ => PostIt,
        };
}

/// <summary>
/// "Casca" de cada item no quadro: posição, camada, rotação e os estados
/// (mouse em cima, arrastando, selecionado, fora da busca).
/// </summary>
public sealed class BoardItemContainer : ContentControl
{
    readonly ScaleTransform _scale = new();
    readonly TranslateTransform _lift = new();
    readonly RotateTransform _rotate = new();

    /// <summary>Deslocamento durante o arraste (em unidades do mundo). A posição só é gravada ao soltar.</summary>
    public TranslateTransform DragOffset { get; } = new();
    BoardItemViewModel? _vm;
    bool _hover, _dragging;

    /// <summary>Avisa o item para "descolar" a sombra (mouse em cima).</summary>
    public event Action<bool>? LiftChanged;

    public BoardItemContainer()
    {
        Focusable = false;
        RenderTransformOrigin = new Point(0.5, 0.5);
        var g = new TransformGroup();
        g.Children.Add(_scale);
        g.Children.Add(_lift);
        g.Children.Add(_rotate);
        g.Children.Add(DragOffset);
        RenderTransform = g;
        Cursor = BoardCursors.Grab;
    }

    public BoardItemViewModel? Item => _vm;

    public bool IsLifted => _hover || _dragging;

    internal void Attach(BoardItemViewModel vm)
    {
        _vm = vm;
        // O container pode estar sendo reaproveitado: volta ao estado de repouso.
        _hover = _dragging = false;
        Anim.Set(_scale, ScaleTransform.ScaleXProperty, 1.0);
        Anim.Set(_scale, ScaleTransform.ScaleYProperty, 1.0);
        Anim.Set(_lift, TranslateTransform.YProperty, 0.0);
        Anim.Set(_rotate, RotateTransform.AngleProperty, vm.Rotation);
        DragOffset.X = DragOffset.Y = 0;
        Anim.Set(this, OpacityProperty, vm.IsDimmed ? 0.16 : 1.0);
        Cursor = BoardCursors.Grab;
        vm.PropertyChanged += OnVmChanged;
    }

    internal void Detach()
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = null;
    }

    void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BoardItemViewModel.IsDimmed) && _vm is not null)
            Anim.To(this, OpacityProperty, _vm.IsDimmed ? 0.16 : 1, 0.3, Anim.Ease);
        if (e.PropertyName == nameof(BoardItemViewModel.Rotation) && _vm is not null && !_hover && !_dragging)
            Anim.Set(_rotate, RotateTransform.AngleProperty, _vm.Rotation);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        UpdatePose();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        UpdatePose();
    }

    public void SetDragging(bool dragging)
    {
        if (_dragging == dragging) return;
        _dragging = dragging;
        Cursor = dragging ? BoardCursors.Grabbing : BoardCursors.Grab;
        UpdatePose();
    }

    void UpdatePose()
    {
        double r = _vm?.Rotation ?? 0;
        if (_dragging)
        {
            // Arrastando: sem atraso, escala 1.05 e rotação a 30%.
            Anim.Set(_scale, ScaleTransform.ScaleXProperty, 1.05);
            Anim.Set(_scale, ScaleTransform.ScaleYProperty, 1.05);
            Anim.Set(_lift, TranslateTransform.YProperty, 0.0);
            Anim.Set(_rotate, RotateTransform.AngleProperty, r * 0.3);
        }
        else
        {
            // Mouse em cima: rotação a 60%, sobe 3 px e cresce para 1.015 (mola de item).
            double s = _hover ? 1.015 : 1;
            Anim.To(_scale, ScaleTransform.ScaleXProperty, s, 0.45, Anim.Item);
            Anim.To(_scale, ScaleTransform.ScaleYProperty, s, 0.45, Anim.Item);
            Anim.To(_lift, TranslateTransform.YProperty, _hover ? -3 : 0, 0.45, Anim.Item);
            Anim.To(_rotate, RotateTransform.AngleProperty, _hover ? r * 0.6 : r, 0.45, Anim.Item);
        }
        LiftChanged?.Invoke(IsLifted);
    }
}
