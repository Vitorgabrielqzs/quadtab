using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Quadtab.Rendering;

namespace Quadtab.Behaviors;

/// <summary>Ao clicar, o elemento afunda para 93% e volta com efeito mola (0.5 s).</summary>
public static class Press
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(Press), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;
        if ((bool)e.NewValue)
        {
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            if (el.RenderTransform is not ScaleTransform) el.RenderTransform = new ScaleTransform();
            el.PreviewMouseLeftButtonDown += Down;
            el.PreviewMouseLeftButtonUp += Up;
            el.MouseLeave += Up;
        }
        else
        {
            el.PreviewMouseLeftButtonDown -= Down;
            el.PreviewMouseLeftButtonUp -= Up;
            el.MouseLeave -= Up;
        }
    }

    static void Down(object sender, MouseButtonEventArgs e) => To(sender, 0.93);
    static void Up(object sender, MouseEventArgs e) => To(sender, 1);

    static void To(object sender, double scale)
    {
        if (sender is not UIElement { RenderTransform: ScaleTransform st }) return;
        if (st.IsFrozen) return;
        if (scale == 1 && st.ScaleX == 1 && !st.HasAnimatedProperties) return;
        Anim.To(st, ScaleTransform.ScaleXProperty, scale, 0.5, Anim.Tap);
        Anim.To(st, ScaleTransform.ScaleYProperty, scale, 0.5, Anim.Tap);
    }
}
