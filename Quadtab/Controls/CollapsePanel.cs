using System.Windows;
using System.Windows.Controls;

namespace Quadtab.Controls;

// Aqui ficam os painéis que recolhem o conteúdo na vertical (título do quadro e seções da lateral).

/// <summary>Aqui usamos a classe HeightFold como base: mede o filho inteiro e mostra só uma fração da altura dele.</summary>
public abstract class HeightFold : Decorator
{
    protected HeightFold() => ClipToBounds = true;

    /// <summary>Fração da altura mostrada (0 = fechado, 1 = aberto).</summary>
    protected abstract double Fraction { get; }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null) return default;
        Child.Measure(new Size(constraint.Width, double.PositiveInfinity));
        return new Size(Child.DesiredSize.Width, Child.DesiredSize.Height * Math.Max(0, Fraction));
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Child?.Arrange(new Rect(0, 0, arrangeSize.Width, Child.DesiredSize.Height));
        return arrangeSize;
    }
}

/// <summary>Aqui usamos a classe CollapsePanel para recolher o conteúdo: Progress 1 = altura toda, 0 = altura zero (animável).</summary>
public sealed class CollapsePanel : HeightFold
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(CollapsePanel),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    protected override double Fraction => Progress;
}

/// <summary>
/// Aqui usamos a classe Fold para uma seção que abre e fecha (estilo toggle do Notion): IsOpen anima a altura com mola (0.35 s).
/// </summary>
public sealed class Fold : HeightFold
{
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(Fold), new FrameworkPropertyMetadata(false, (d, e) => ((Fold)d).Animate((bool)e.NewValue)));

    static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        "Progress", typeof(double), typeof(Fold), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    static readonly Rendering.CubicBezierEase Spring = new(.3, 1.15, .4, 1);
    bool _loaded;

    public Fold()
    {
        // Ao carregar, já começa aberto ou fechado (sem animar).
        Loaded += (_, _) =>
        {
            _loaded = true;
            SetValue(ProgressProperty, IsOpen ? 1.0 : 0.0);
            if (Child is not null) Child.Visibility = IsOpen ? Visibility.Visible : Visibility.Collapsed;
        };
    }

    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }

    protected override double Fraction => (double)GetValue(ProgressProperty);

    // Anima a altura; fechado, o filho some de vez (não ocupa tempo de layout).
    void Animate(bool open)
    {
        if (!_loaded) { SetValue(ProgressProperty, open ? 1.0 : 0.0); return; }
        if (Child is not null && open) Child.Visibility = Visibility.Visible;
        Rendering.Anim.To(this, ProgressProperty, open ? 1 : 0, 0.35, Spring, () =>
        {
            if (!IsOpen && Child is not null) Child.Visibility = Visibility.Collapsed;
        });
    }
}
