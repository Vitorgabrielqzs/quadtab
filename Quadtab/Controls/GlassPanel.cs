using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Quadtab.Behaviors;
using Quadtab.Rendering;

namespace Quadtab.Controls;

/// <summary>
/// Painel de vidro (Liquid Glass) em camadas:
/// fundo desfocado → cor do vidro → brilho interno (segue o mouse) → conteúdo → borda de luz.
/// A sombra é desenhada pelo próprio painel, sem Effect.
/// </summary>
public class GlassPanel : Decorator
{
    // ---------- propriedades ----------

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(GlassPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((GlassPanel)d).OnShapeChanged()));

    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Brush), typeof(GlassPanel),
        new FrameworkPropertyMetadata(null, (d, e) => ((GlassPanel)d)._fill.SetFill((Brush?)e.NewValue)));

    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(Thickness), typeof(GlassPanel), new FrameworkPropertyMetadata(default(Thickness), FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ShadowLayersProperty = DependencyProperty.Register(
        nameof(ShadowLayers), typeof(string), typeof(GlassPanel), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RimColorProperty = DependencyProperty.Register(
        nameof(RimColor), typeof(Color), typeof(GlassPanel), new FrameworkPropertyMetadata(Color.FromArgb(242, 255, 255, 255), (d, _) => ((GlassPanel)d).OnShapeChanged()));

    public static readonly DependencyProperty Rim2ColorProperty = DependencyProperty.Register(
        nameof(Rim2Color), typeof(Color), typeof(GlassPanel), new FrameworkPropertyMetadata(Color.FromArgb(128, 255, 255, 255), (d, _) => ((GlassPanel)d).OnShapeChanged()));

    public static readonly DependencyProperty GlowAlphaProperty = DependencyProperty.Register(
        nameof(GlowAlpha), typeof(double), typeof(GlassPanel), new FrameworkPropertyMetadata(0.34, (d, e) => ((GlassPanel)d).SetGlowAlpha((double)e.NewValue)));

    public static readonly DependencyProperty ClipContentProperty = DependencyProperty.Register(
        nameof(ClipContent), typeof(bool), typeof(GlassPanel), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>Elemento que aparece desfocado através do vidro.</summary>
    public static readonly DependencyProperty BackdropSourceProperty = DependencyProperty.Register(
        nameof(BackdropSource), typeof(Visual), typeof(GlassPanel), new FrameworkPropertyMetadata(null, (d, _) => ((GlassPanel)d).RebuildBackdrop()));

    /// <summary>Raio do desfoque (0 = sem desfoque).</summary>
    public static readonly DependencyProperty BackdropBlurProperty = DependencyProperty.Register(
        nameof(BackdropBlur), typeof(double), typeof(GlassPanel), new FrameworkPropertyMetadata(0.0, (d, _) => ((GlassPanel)d).RebuildBackdrop()));

    public static readonly DependencyProperty BackdropSaturationProperty = DependencyProperty.Register(
        nameof(BackdropSaturation), typeof(double), typeof(GlassPanel), new FrameworkPropertyMetadata(1.0, (d, _) => ((GlassPanel)d).RebuildBackdrop()));

    /// <summary>Cor sólida por baixo do desfoque (evita bordas transparentes).</summary>
    public static readonly DependencyProperty BackdropBaseProperty = DependencyProperty.Register(
        nameof(BackdropBase), typeof(Brush), typeof(GlassPanel), new FrameworkPropertyMetadata(null, (d, _) => ((GlassPanel)d).RebuildBackdrop()));

    /// <summary>Fundo já pronto (pré-desfocado), em vez de BackdropSource.</summary>
    public static readonly DependencyProperty BackdropBrushProperty = DependencyProperty.Register(
        nameof(BackdropBrush), typeof(Brush), typeof(GlassPanel), new FrameworkPropertyMetadata(null, (d, e) => ((GlassPanel)d).OnBackdropBrushChanged((Brush?)e.NewValue)));

    public static readonly DependencyProperty GlowEnabledProperty = DependencyProperty.Register(
        nameof(GlowEnabled), typeof(bool), typeof(GlassPanel), new FrameworkPropertyMetadata(true));

    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    public Brush? Background { get => (Brush?)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }
    public string? ShadowLayers { get => (string?)GetValue(ShadowLayersProperty); set => SetValue(ShadowLayersProperty, value); }
    public Color RimColor { get => (Color)GetValue(RimColorProperty); set => SetValue(RimColorProperty, value); }
    public Color Rim2Color { get => (Color)GetValue(Rim2ColorProperty); set => SetValue(Rim2ColorProperty, value); }
    public double GlowAlpha { get => (double)GetValue(GlowAlphaProperty); set => SetValue(GlowAlphaProperty, value); }
    public bool ClipContent { get => (bool)GetValue(ClipContentProperty); set => SetValue(ClipContentProperty, value); }
    public Visual? BackdropSource { get => (Visual?)GetValue(BackdropSourceProperty); set => SetValue(BackdropSourceProperty, value); }
    public double BackdropBlur { get => (double)GetValue(BackdropBlurProperty); set => SetValue(BackdropBlurProperty, value); }
    public double BackdropSaturation { get => (double)GetValue(BackdropSaturationProperty); set => SetValue(BackdropSaturationProperty, value); }
    public Brush? BackdropBase { get => (Brush?)GetValue(BackdropBaseProperty); set => SetValue(BackdropBaseProperty, value); }
    public Brush? BackdropBrush { get => (Brush?)GetValue(BackdropBrushProperty); set => SetValue(BackdropBrushProperty, value); }
    public bool GlowEnabled { get => (bool)GetValue(GlowEnabledProperty); set => SetValue(GlowEnabledProperty, value); }

    // ---------- camadas ----------

    readonly RoundLayer _fill = new() { IsHitTestVisible = true };
    readonly RoundLayer _glow = new() { Opacity = 0 };
    readonly RoundLayer _rimStatic = new() { StrokeThickness = 1.3 };
    readonly RoundLayer _rimDynamic = new() { StrokeThickness = 1.3, Opacity = 0.2 };
    readonly RadialGradientBrush _glowBrush;
    readonly RadialGradientBrush _rimBrush;
    BackdropLayer? _backdrop;
    VisualBrush? _visualBrush;
    bool _sourceInTree;
    Rect _lastViewbox;
    Visual? _windowRoot;
    Size _lastSize;

    internal double GlowTarget, GlowValue;
    internal bool PointerFar = true;

    public GlassPanel()
    {
        var far = new Point(-9999, -9999);
        _glowBrush = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, RadiusX = 300, RadiusY = 300, Center = far, GradientOrigin = far };
        _glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(87, 255, 255, 255), 0));
        _glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.65));
        _glow.SetFill(_glowBrush);

        _rimBrush = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, RadiusX = 190, RadiusY = 190, Center = far, GradientOrigin = far };
        _rimBrush.GradientStops.Add(new GradientStop(Colors.White, 0));
        _rimBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.72));
        _rimDynamic.SetStroke(_rimBrush);

        foreach (var l in new UIElement[] { _fill, _glow, _rimStatic, _rimDynamic }) AddVisualChild(l);

        Loaded += (_, _) =>
        {
            _windowRoot = Window.GetWindow(this)?.Content as Visual;
            if (GlowEnabled) GlassGlow.Register(this);
            RefreshBackdrop();
        };
        Unloaded += (_, _) => GlassGlow.Unregister(this);
    }

    // ---------- árvore visual: [fundo] [vidro] [brilho] conteúdo [borda] [borda viva] ----------

    protected override int VisualChildrenCount => 4 + (_backdrop is null ? 0 : 1) + (Child is null ? 0 : 1);

    protected override Visual GetVisualChild(int index)
    {
        if (_backdrop is not null)
        {
            if (index == 0) return _backdrop;
            index--;
        }
        if (index == 0) return _fill;
        if (index == 1) return _glow;
        if (Child is not null)
        {
            if (index == 2) return Child;
            index--;
        }
        return index == 2 ? _rimStatic : _rimDynamic;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var p = Padding;
        double ph = p.Left + p.Right, pv = p.Top + p.Bottom;
        var size = default(Size);
        if (Child is not null)
        {
            Child.Measure(new Size(Math.Max(0, constraint.Width - ph), Math.Max(0, constraint.Height - pv)));
            size = Child.DesiredSize;
        }
        _backdrop?.Measure(constraint);
        return new Size(size.Width + ph, size.Height + pv);
    }

    protected override Size ArrangeOverride(Size s)
    {
        var full = new Rect(s);
        _backdrop?.Arrange(full);
        _fill.Arrange(full);
        _glow.Arrange(full);
        _rimStatic.Arrange(full);
        _rimDynamic.Arrange(full);

        var p = Padding;
        if (Child is not null)
        {
            Child.Arrange(new Rect(p.Left, p.Top, Math.Max(0, s.Width - p.Left - p.Right), Math.Max(0, s.Height - p.Top - p.Bottom)));
            Child.Clip = ClipContent
                ? Frozen(new RectangleGeometry(new Rect(-p.Left, -p.Top, s.Width, s.Height), Radius(s), Radius(s)))
                : null;
        }
        if (s != _lastSize)
        {
            _lastSize = s;
            OnShapeChanged(s); // RenderSize ainda não foi atualizado aqui
        }
        return s;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var layers = ShadowSpec.Parse(ShadowLayers);
        if (layers.Length > 0 && ActualWidth > 0)
            ShadowCache.Draw(dc, new Rect(RenderSize), Radius(RenderSize), layers, hollow: true);
    }

    double Radius(Size s) => Math.Max(0, Math.Min(CornerRadius, Math.Min(s.Width, s.Height) / 2));

    void OnShapeChanged() => OnShapeChanged(_lastSize);

    void OnShapeChanged(Size s)
    {
        double r = Radius(s);
        foreach (var l in new[] { _fill, _glow, _rimStatic, _rimDynamic }) l.SetRadius(r);

        // Borda fixa a 150°: Rim no canto superior esquerdo → transparente → Rim2 no inferior direito.
        var clear = Color.FromArgb(0, 255, 255, 255);
        _rimStatic.SetStroke(BoardSurfaces.CssGradient(150, new Rect(s),
            (RimColor, 0), (clear, 0.28), (clear, 0.66), (Rim2Color, 1)));

        _backdrop?.SetShape(s, r);
        RefreshBackdrop();
    }

    void SetGlowAlpha(double a) =>
        _glowBrush.GradientStops[0].Color = Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), 255, 255, 255);

    // ---------- brilho que segue o mouse (chamado pelo GlassGlow) ----------

    internal void SetPointer(Point local, double prox, bool far)
    {
        if (far && PointerFar) return;
        PointerFar = far;
        _glowBrush.Center = _glowBrush.GradientOrigin = local;
        _rimBrush.Center = _rimBrush.GradientOrigin = local;
        _rimDynamic.Opacity = 0.2 + 0.8 * prox;
    }

    internal void ApplyGlow(double value) => _glow.Opacity = value;

    // ---------- fundo desfocado ----------

    void OnBackdropBrushChanged(Brush? brush)
    {
        // Só trocou o pincel (ex.: bloco arrastado): reaproveita a camada.
        if (brush is not null && _backdrop is not null && _visualBrush is null) _backdrop.SetBrush(brush);
        else RebuildBackdrop();
    }

    void RebuildBackdrop()
    {
        if (_backdrop is not null)
        {
            RemoveVisualChild(_backdrop);
            _backdrop = null;
            LayoutUpdated -= OnLayoutUpdated;
        }
        _visualBrush = null;

        if (BackdropBrush is { } staticBrush)
        {
            _backdrop = new BackdropLayer(staticBrush, null, 0, BackdropSaturation);
        }
        else if (BackdropSource is { } src)
        {
            _sourceInTree = VisualTreeHelper.GetParent(src) is not null;
            _visualBrush = new VisualBrush(src)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill,
                TileMode = TileMode.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
            };
            _lastViewbox = Rect.Empty;
            _backdrop = new BackdropLayer(_visualBrush, BackdropBase, BackdropBlur, BackdropSaturation);
            LayoutUpdated += OnLayoutUpdated;
        }

        if (_backdrop is not null)
        {
            AddVisualChild(_backdrop);
            _backdrop.SetShape(_lastSize, Radius(_lastSize));
        }
        InvalidateMeasure();
        RefreshBackdrop();
    }

    void OnLayoutUpdated(object? sender, EventArgs e) => RefreshBackdrop();

    /// <summary>Recalcula que pedaço da fonte aparece atrás do vidro (só se mudou).</summary>
    public void RefreshBackdrop()
    {
        if (_visualBrush is null || BackdropSource is not Visual src || !IsVisible) return;
        double m = _backdrop!.Margin;
        var local = new Rect(-m, -m, _lastSize.Width + 2 * m, _lastSize.Height + 2 * m);
        Rect vb;
        try
        {
            if (_sourceInTree) vb = TransformToVisual(src).TransformBounds(local);
            else if (_windowRoot is not null) vb = TransformToAncestor(_windowRoot).TransformBounds(local);
            else return;
        }
        catch (InvalidOperationException) { return; }

        if (vb == _lastViewbox) return;
        _lastViewbox = vb;
        _visualBrush.Viewbox = vb;
        _visualBrush.Viewport = local;
    }

    static Geometry Frozen(Geometry g) { g.Freeze(); return g; }

    // ---------- camadas internas ----------

    /// <summary>Retângulo arredondado simples (preenchimento ou contorno).</summary>
    sealed class RoundLayer : FrameworkElement
    {
        Brush? _fill, _stroke;
        double _radius;
        public double StrokeThickness;

        public RoundLayer() => IsHitTestVisible = false;

        public void SetFill(Brush? b) { _fill = b; InvalidateVisual(); }
        public void SetStroke(Brush? b) { _stroke = b; InvalidateVisual(); }
        public void SetRadius(double r) { if (r != _radius) { _radius = r; InvalidateVisual(); } }

        protected override Size MeasureOverride(Size availableSize) => default;

        protected override void OnRender(DrawingContext dc)
        {
            var rect = new Rect(RenderSize);
            if (rect.Width <= 0 || rect.Height <= 0) return;
            if (_fill is not null) dc.DrawRoundedRectangle(_fill, null, rect, _radius, _radius);
            if (_stroke is not null && StrokeThickness > 0)
            {
                double h = StrokeThickness / 2;
                rect.Inflate(-h, -h);
                double r = Math.Max(0, _radius - h);
                var pen = new Pen(_stroke, StrokeThickness);
                if (pen.CanFreeze) pen.Freeze();
                dc.DrawRoundedRectangle(null, pen, rect, r, r);
            }
        }
    }

    /// <summary>Recorte arredondado (+ saturação) por fora; desfoque por dentro, maior que o painel.</summary>
    sealed class BackdropLayer : FrameworkElement
    {
        readonly Content _content;

        public BackdropLayer(Brush brush, Brush? baseBrush, double blur, double saturation)
        {
            IsHitTestVisible = false;
            Margin = blur > 0 ? Math.Ceiling(blur) : 0;
            _content = new Content(brush, baseBrush, Margin);
            if (blur > 0) _content.Effect = new BlurEffect { Radius = blur, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
            if (Math.Abs(saturation - 1) > 0.01) Effect = new SaturateEffect { Amount = saturation };
            AddVisualChild(_content);
        }

        public new double Margin { get; }

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _content;

        protected override Size MeasureOverride(Size availableSize) { _content.Measure(availableSize); return default; }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _content.Arrange(new Rect(finalSize));
            return finalSize;
        }

        public void SetShape(Size s, double r)
        {
            var g = new RectangleGeometry(new Rect(s), r, r);
            g.Freeze();
            Clip = g;
        }

        public void SetBrush(Brush b) { _content.Brush = b; _content.InvalidateVisual(); }

        sealed class Content(Brush brush, Brush? baseBrush, double margin) : FrameworkElement
        {
            public Brush Brush = brush;

            protected override Size MeasureOverride(Size availableSize) => default;

            protected override void OnRender(DrawingContext dc)
            {
                var r = new Rect(-margin, -margin, ActualWidth + 2 * margin, ActualHeight + 2 * margin);
                if (baseBrush is not null) dc.DrawRectangle(baseBrush, null, r);
                dc.DrawRectangle(Brush, null, r);
            }
        }
    }
}
