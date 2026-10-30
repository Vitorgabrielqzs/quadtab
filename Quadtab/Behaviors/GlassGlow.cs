using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Quadtab.Controls;

namespace Quadtab.Behaviors;

/// <summary>
/// Faz a borda e o brilho do vidro seguirem o mouse.
/// prox = max(0, 1 − distância/170), com distância medida até a borda do painel (0 dentro dele).
/// Atualiza no máximo uma vez por quadro e só enquanto há algo mudando.
/// </summary>
public static class GlassGlow
{
    const double Reach = 170;
    const double FadeSeconds = 0.1; // suavização do brilho (equivale à transição de 0.3 s do CSS)

    static readonly List<GlassPanel> Panels = [];
    static FrameworkElement? _root;
    static Point _mouse;
    static bool _inside, _dirty, _ticking;
    static TimeSpan _lastFrame;

    /// <summary>Pausado enquanto a câmera ou um item se move (economiza tempo por quadro).</summary>
    public static bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value) return;
            _paused = value;
            if (!value) Kick();
        }
    }
    static bool _paused;

    public static void Attach(Window window)
    {
        _root = window.Content as FrameworkElement;
        window.PreviewMouseMove += (_, e) =>
        {
            if (_root is null) return;
            _mouse = e.GetPosition(_root);
            _inside = true;
            Kick();
        };
        window.MouseLeave += (_, _) => { _inside = false; Kick(); };
        window.Deactivated += (_, _) => { _inside = false; Kick(); };
    }

    internal static void Register(GlassPanel p)
    {
        if (!Panels.Contains(p)) Panels.Add(p);
        Kick();
    }

    internal static void Unregister(GlassPanel p) => Panels.Remove(p);

    /// <summary>Pede uma atualização no próximo quadro.</summary>
    public static void Kick()
    {
        _dirty = true;
        if (_ticking) return;
        _ticking = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    static void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return; // o WPF pode chamar duas vezes no mesmo quadro
        if (_paused)
        {
            CompositionTarget.Rendering -= OnRendering;
            _ticking = false;
            return;
        }
        double dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min(0.1, (now - _lastFrame).TotalSeconds);
        _lastFrame = now;

        bool moving = false;
        double k = 1 - Math.Exp(-dt / FadeSeconds);

        for (int i = Panels.Count - 1; i >= 0; i--)
        {
            var p = Panels[i];
            if (!p.IsVisible || _root is null) continue;

            if (_dirty)
            {
                try
                {
                    var t = p.TransformToAncestor(_root);
                    var b = t.TransformBounds(new Rect(p.RenderSize));
                    double dx = Math.Max(Math.Max(b.Left - _mouse.X, 0), _mouse.X - b.Right);
                    double dy = Math.Max(Math.Max(b.Top - _mouse.Y, 0), _mouse.Y - b.Bottom);
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    double prox = _inside ? Math.Max(0, 1 - dist / Reach) : 0;
                    bool far = dist > 320;
                    var inv = t.Inverse;
                    var local = inv is not null ? inv.Transform(_mouse) : _mouse;
                    p.SetPointer(local, prox, far && _inside);
                    p.GlowTarget = prox;
                }
                catch (InvalidOperationException) { continue; }
            }

            double diff = p.GlowTarget - p.GlowValue;
            if (Math.Abs(diff) > 0.003)
            {
                p.GlowValue += diff * k;
                moving = true;
            }
            else p.GlowValue = p.GlowTarget;
            p.ApplyGlow(p.GlowValue);
        }

        _dirty = false;
        if (!moving)
        {
            CompositionTarget.Rendering -= OnRendering;
            _ticking = false;
        }
    }
}
