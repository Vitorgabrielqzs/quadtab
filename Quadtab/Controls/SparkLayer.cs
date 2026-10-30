using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Quadtab.Controls;

/// <summary>
/// Faíscas ao clicar: 8 a 12 tracinhos saem da ponta do ponteiro em todas as direções, encolhem e somem
/// em ~400 ms. Camada transparente por cima de tudo, que não recebe cliques. No máximo 3 explosões ao
/// mesmo tempo, com as partículas reaproveitadas.
/// </summary>
public sealed class SparkLayer : Canvas
{
    const int MaxBursts = 3, MaxSparks = 12;
    const double Life = 0.4;

    sealed class Spark
    {
        public readonly Line Shape = new() { StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false };
        public double Angle, Speed, Length, Width;
    }

    sealed class Burst
    {
        public readonly Spark[] Sparks = Enumerable.Range(0, MaxSparks).Select(_ => new Spark()).ToArray();
        public Point Origin;
        public int Count;
        public double Age = -1; // < 0 = livre
    }

    static readonly Random Rng = new();
    static readonly Brush[] Colors = CreateColors();
    static int _lastColor;
    readonly Burst[] _bursts = Enumerable.Range(0, MaxBursts).Select(_ => new Burst()).ToArray();
    bool _running;
    TimeSpan _last;

    public SparkLayer()
    {
        IsHitTestVisible = false;
        foreach (var b in _bursts)
            foreach (var s in b.Sparks)
            {
                s.Shape.Visibility = Visibility.Collapsed;
                Children.Add(s.Shape);
            }
    }

    /// <summary>Ligado em Ajustes; o Windows com "efeitos de animação" desligado também desliga.</summary>
    public bool Enabled { get; set; } = true;

    // 15 cores: cada clique sorteia uma, sempre diferente da anterior.
    static Brush[] CreateColors()
    {
        var list = new[]
            {
                "#FFFF453A", "#FFFF7A66", "#FFFF9F0A", "#FFFFD60A", "#FFA4E635",
                "#FF30D158", "#FF5FE3C0", "#FF40C8E0", "#FF5AC8FA", "#FF0A84FF",
                "#FF5E5CE6", "#FFBF5AF2", "#FFD6A4FF", "#FFFF6FB1", "#FFFF2D55",
            }
            .Select(h => (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString(h))).ToArray();
        foreach (var b in list) b.Freeze();
        return list;
    }

    static Brush NextColor()
    {
        _lastColor = (_lastColor + 1 + Rng.Next(Colors.Length - 1)) % Colors.Length;
        return Colors[_lastColor];
    }

    public void Fire(Point at)
    {
        if (!Enabled || !SystemParameters.ClientAreaAnimation) return;
        // Usa uma explosão livre; se as 3 estão ocupadas, reaproveita a mais antiga.
        var burst = _bursts.FirstOrDefault(b => b.Age < 0) ?? _bursts.MaxBy(b => b.Age)!;
        burst.Origin = at;
        burst.Age = 0;
        burst.Count = Rng.Next(8, 13);
        double turn = Rng.NextDouble() * Math.PI * 2;
        var color = NextColor();
        for (int i = 0; i < MaxSparks; i++)
        {
            var s = burst.Sparks[i];
            if (i >= burst.Count) { s.Shape.Visibility = Visibility.Collapsed; continue; }
            s.Angle = turn + i * Math.PI * 2 / burst.Count + (Rng.NextDouble() - 0.5) * 0.35;
            s.Speed = 26 + Rng.NextDouble() * 18;   // distância percorrida (px)
            s.Length = 7 + Rng.NextDouble() * 5;
            s.Width = 2.6 + Rng.NextDouble() * 0.9;
            s.Shape.Stroke = color;
            s.Shape.Visibility = Visibility.Visible;
        }
        Step(burst);
        if (!_running)
        {
            _running = true;
            _last = TimeSpan.Zero;
            CompositionTarget.Rendering += OnFrame;
        }
    }

    void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        double dt = _last == TimeSpan.Zero ? 1 / 120.0 : Math.Min(0.05, (now - _last).TotalSeconds);
        if (now == _last) return;
        _last = now;
        bool any = false;
        foreach (var b in _bursts)
        {
            if (b.Age < 0) continue;
            b.Age += dt;
            if (b.Age >= Life)
            {
                b.Age = -1;
                foreach (var s in b.Sparks) s.Shape.Visibility = Visibility.Collapsed;
                continue;
            }
            any = true;
            Step(b);
        }
        if (!any)
        {
            _running = false;
            CompositionTarget.Rendering -= OnFrame;
        }
    }

    static void Step(Burst b)
    {
        double t = b.Age / Life;
        double ease = 1 - Math.Pow(1 - t, 3);   // sai rápido e desacelera
        double fade = 1 - t * t;
        for (int i = 0; i < b.Count; i++)
        {
            var s = b.Sparks[i];
            double cos = Math.Cos(s.Angle), sin = Math.Sin(s.Angle);
            double start = 4 + s.Speed * ease;
            double len = s.Length * (1 - t * 0.85);  // encolhe
            s.Shape.X1 = b.Origin.X + cos * start;
            s.Shape.Y1 = b.Origin.Y + sin * start;
            s.Shape.X2 = b.Origin.X + cos * (start + len);
            s.Shape.Y2 = b.Origin.Y + sin * (start + len);
            s.Shape.StrokeThickness = s.Width * (1 - t * 0.5);
            s.Shape.Opacity = fade;
        }
    }
}
