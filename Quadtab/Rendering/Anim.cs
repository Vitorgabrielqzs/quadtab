using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Quadtab.Rendering;

/// <summary>
/// Curva cubic-bezier igual à do CSS, aceitando valores acima de 1 (efeito mola que "passa do ponto").
/// O KeySpline do WPF não aceita valores fora de 0..1, por isso esta classe existe.
/// </summary>
public sealed class CubicBezierEase : EasingFunctionBase
{
    public static readonly DependencyProperty X1Property = DependencyProperty.Register(nameof(X1), typeof(double), typeof(CubicBezierEase));
    public static readonly DependencyProperty Y1Property = DependencyProperty.Register(nameof(Y1), typeof(double), typeof(CubicBezierEase));
    public static readonly DependencyProperty X2Property = DependencyProperty.Register(nameof(X2), typeof(double), typeof(CubicBezierEase));
    public static readonly DependencyProperty Y2Property = DependencyProperty.Register(nameof(Y2), typeof(double), typeof(CubicBezierEase));

    public CubicBezierEase() => EasingMode = EasingMode.EaseIn; // EaseIn = usa a curva exatamente como definida

    public CubicBezierEase(double x1, double y1, double x2, double y2) : this()
    {
        X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
        Freeze();
    }

    public double X1 { get => (double)GetValue(X1Property); set => SetValue(X1Property, value); }
    public double Y1 { get => (double)GetValue(Y1Property); set => SetValue(Y1Property, value); }
    public double X2 { get => (double)GetValue(X2Property); set => SetValue(X2Property, value); }
    public double Y2 { get => (double)GetValue(Y2Property); set => SetValue(Y2Property, value); }

    protected override double EaseInCore(double time)
    {
        if (time <= 0) return 0;
        if (time >= 1) return 1;
        double x1 = X1, x2 = X2;
        // Acha t tal que x(t) = time (Newton, com bisseção de reserva).
        double t = time;
        for (int i = 0; i < 6; i++)
        {
            double x = Bez(t, x1, x2) - time;
            double d = Deriv(t, x1, x2);
            if (Math.Abs(x) < 1e-5) return Bez(t, Y1, Y2);
            if (Math.Abs(d) < 1e-6) break;
            t -= x / d;
        }
        double lo = 0, hi = 1;
        t = time;
        for (int i = 0; i < 20; i++)
        {
            double x = Bez(t, x1, x2);
            if (Math.Abs(x - time) < 1e-5) break;
            if (x < time) lo = t; else hi = t;
            t = (lo + hi) / 2;
        }
        return Bez(t, Y1, Y2);
    }

    static double Bez(double t, double a, double b) => ((1 - 3 * b + 3 * a) * t + (3 * b - 6 * a)) * t * t + 3 * a * t;
    static double Deriv(double t, double a, double b) => 3 * (1 - 3 * b + 3 * a) * t * t + 2 * (3 * b - 6 * a) * t + 3 * a;

    protected override Freezable CreateInstanceCore() => new CubicBezierEase();
}

/// <summary>Curvas de movimento do design e atalhos para animar.</summary>
public static class Anim
{
    public static readonly CubicBezierEase Tap = new(.25, 1.7, .45, 1);        // 0.5 s  botão afunda e volta
    public static readonly CubicBezierEase Item = new(.3, 1.5, .5, 1);        // 0.45 s hover dos itens
    public static readonly CubicBezierEase Panel = new(.3, 1.4, .5, 1);       // 0.55 s bandeja
    public static readonly CubicBezierEase Pop = new(.3, 1.45, .5, 1);        // 0.4 s  menu
    public static readonly CubicBezierEase Segment = new(.3, 1.35, .5, 1);    // 0.5 s  controle segmentado
    public static readonly CubicBezierEase Collapse = new(.3, 1, .4, 1);      // 0.5 s  título recolhendo
    public static readonly CubicBezierEase CollapseMove = new(.3, 1.2, .4, 1);
    public static readonly CubicBezierEase Sidebar = new(.3, 1.15, .4, 1);    // 0.45 s lateral recolhendo
    public static readonly CubicBezierEase Inline = new(.3, 1.3, .5, 1);      // título pequeno surgindo
    public static readonly CubicBezierEase Mini = new(.3, 1.6, .5, 1);        // mini post-its da bandeja
    public static readonly CubicBezierEase Ease = new(.25, .1, .25, 1);       // "ease" do CSS

    /// <summary>Anima a propriedade até o valor, a partir de onde ela estiver agora.</summary>
    public static void To(IAnimatable target, DependencyProperty dp, double to, double seconds,
                          IEasingFunction? ease = null, Action? completed = null)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromSeconds(seconds)) { EasingFunction = ease };
        if (completed is not null) a.Completed += (_, _) => completed();
        target.BeginAnimation(dp, a, HandoffBehavior.SnapshotAndReplace);
    }

    public static void To(IAnimatable target, DependencyProperty dp, Thickness to, double seconds, IEasingFunction? ease = null)
    {
        var a = new ThicknessAnimation(to, TimeSpan.FromSeconds(seconds)) { EasingFunction = ease };
        target.BeginAnimation(dp, a, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Muda na hora, cancelando qualquer animação em andamento.</summary>
    public static void Set(IAnimatable target, DependencyProperty dp, object value)
    {
        target.BeginAnimation(dp, null);
        ((DependencyObject)target).SetValue(dp, value);
    }
}
