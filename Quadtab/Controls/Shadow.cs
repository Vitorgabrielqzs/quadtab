using System.Windows;
using System.Windows.Media;
using Quadtab.Rendering;

namespace Quadtab.Controls;

/// <summary>
/// Desenha sombras no formato do CSS (box-shadow) do tamanho do próprio elemento, sem usar Effect.
/// Fica atrás da caixa que faz sombra, na mesma célula do Grid.
/// </summary>
public sealed class Shadow : FrameworkElement
{
    public static readonly DependencyProperty LayersProperty = DependencyProperty.Register(
        nameof(Layers), typeof(string), typeof(Shadow), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(Shadow), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HollowProperty = DependencyProperty.Register(
        nameof(Hollow), typeof(bool), typeof(Shadow), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public Shadow()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
    }

    /// <summary>Ex.: "0 14 36 0 #1A1E243C, 0 1 3 0 #0F1E243C" (x y desfoque espalhamento cor).</summary>
    public string? Layers { get => (string?)GetValue(LayersProperty); set => SetValue(LayersProperty, value); }

    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    /// <summary>Não pinta embaixo da caixa (para caixas translúcidas).</summary>
    public bool Hollow { get => (bool)GetValue(HollowProperty); set => SetValue(HollowProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => default;

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        ShadowCache.Draw(dc, new Rect(RenderSize), CornerRadius, ShadowSpec.Parse(Layers), Hollow);
    }
}
