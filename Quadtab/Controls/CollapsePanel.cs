using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Quadtab.Controls;

/// <summary>Recolhe o conteúdo na vertical: Progress 1 = altura toda, 0 = altura zero (animável).</summary>
public sealed class CollapsePanel : Decorator
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(CollapsePanel),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public CollapsePanel() => ClipToBounds = true;

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null) return default;
        Child.Measure(new Size(constraint.Width, double.PositiveInfinity));
        return new Size(Child.DesiredSize.Width, Math.Max(0, Child.DesiredSize.Height * Progress));
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Child?.Arrange(new Rect(0, 0, arrangeSize.Width, Child.DesiredSize.Height));
        return arrangeSize;
    }
}

/// <summary>
/// Seção que abre e fecha (estilo toggle do Notion): IsOpen anima a altura com mola (0.35 s).
/// </summary>
public sealed class Fold : Decorator
{
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(Fold), new FrameworkPropertyMetadata(false, (d, e) => ((Fold)d).Animate((bool)e.NewValue)));

    static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        "Progress", typeof(double), typeof(Fold), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    static readonly Rendering.CubicBezierEase Spring = new(.3, 1.15, .4, 1);
    bool _loaded;

    public Fold()
    {
        ClipToBounds = true;
        Loaded += (_, _) =>
        {
            _loaded = true;
            SetValue(ProgressProperty, IsOpen ? 1.0 : 0.0);
            if (Child is not null) Child.Visibility = IsOpen ? Visibility.Visible : Visibility.Collapsed;
        };
    }

    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }

    void Animate(bool open)
    {
        if (!_loaded)
        {
            SetValue(ProgressProperty, open ? 1.0 : 0.0);
            return;
        }
        if (Child is not null && open) Child.Visibility = Visibility.Visible;
        Rendering.Anim.To(this, ProgressProperty, open ? 1 : 0, 0.35, Spring, () =>
        {
            if (!IsOpen && Child is not null) Child.Visibility = Visibility.Collapsed;
        });
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null) return default;
        Child.Measure(new Size(constraint.Width, double.PositiveInfinity));
        double p = Math.Max(0, (double)GetValue(ProgressProperty));
        return new Size(Child.DesiredSize.Width, Child.DesiredSize.Height * p);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Child?.Arrange(new Rect(0, 0, arrangeSize.Width, Child.DesiredSize.Height));
        return arrangeSize;
    }
}
