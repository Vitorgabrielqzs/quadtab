using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Quadtab.Rendering;

namespace Quadtab.Controls;

/// <summary>
/// Texto com espaçamento entre letras (letter-spacing do CSS), que o TextBlock do WPF não tem.
/// Quebra linhas por palavra, aceita "\n", maiúsculas automáticas e sombras de texto leves.
/// </summary>
public sealed class TrackedText : FrameworkElement
{
    static readonly FrameworkPropertyMetadataOptions M = FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender;
    static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(TrackedText), new FrameworkPropertyMetadata("", M));
    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(TrackedText), new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, M | FrameworkPropertyMetadataOptions.Inherits));
    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(TrackedText), new FrameworkPropertyMetadata(14.0, M | FrameworkPropertyMetadataOptions.Inherits));
    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(typeof(TrackedText), new FrameworkPropertyMetadata(FontWeights.Normal, M | FrameworkPropertyMetadataOptions.Inherits));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(TrackedText), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));
    public static readonly DependencyProperty TrackingProperty = DependencyProperty.Register(nameof(Tracking), typeof(double), typeof(TrackedText), new FrameworkPropertyMetadata(0.0, M));
    public static readonly DependencyProperty LineHeightProperty = DependencyProperty.Register(nameof(LineHeight), typeof(double), typeof(TrackedText), new FrameworkPropertyMetadata(double.NaN, M));
    public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(nameof(TextAlignment), typeof(TextAlignment), typeof(TrackedText), new FrameworkPropertyMetadata(TextAlignment.Left, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty UppercaseProperty = DependencyProperty.Register(nameof(Uppercase), typeof(bool), typeof(TrackedText), new FrameworkPropertyMetadata(false, M));
    public static readonly DependencyProperty WrapProperty = DependencyProperty.Register(nameof(Wrap), typeof(bool), typeof(TrackedText), new FrameworkPropertyMetadata(true, M));
    public static readonly DependencyProperty TextShadowsProperty = DependencyProperty.Register(nameof(TextShadows), typeof(string), typeof(TrackedText), new FrameworkPropertyMetadata(null, M));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    /// <summary>Espaço extra entre letras, em "em" (0.03 = .03em).</summary>
    public double Tracking { get => (double)GetValue(TrackingProperty); set => SetValue(TrackingProperty, value); }
    public double LineHeight { get => (double)GetValue(LineHeightProperty); set => SetValue(LineHeightProperty, value); }
    public TextAlignment TextAlignment { get => (TextAlignment)GetValue(TextAlignmentProperty); set => SetValue(TextAlignmentProperty, value); }
    public bool Uppercase { get => (bool)GetValue(UppercaseProperty); set => SetValue(UppercaseProperty, value); }
    public bool Wrap { get => (bool)GetValue(WrapProperty); set => SetValue(WrapProperty, value); }
    /// <summary>Sombras no formato "x y desfoque espalhamento cor" separadas por vírgula.</summary>
    public string? TextShadows { get => (string?)GetValue(TextShadowsProperty); set => SetValue(TextShadowsProperty, value); }

    readonly List<(GlyphRun? Run, double Width)> _lines = [];
    readonly List<(double Dx, double Dy, Brush Brush)> _shadowPasses = [];
    FormattedText? _fallback;
    double _lineHeight;

    public TrackedText() => IsHitTestVisible = true;

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateMeasure();

    protected override Size MeasureOverride(Size available)
    {
        _lines.Clear();
        _fallback = null;
        BuildShadowPasses();

        var text = Text ?? "";
        if (Uppercase) text = text.ToUpper(PtBr);
        double size = FontSize;
        double maxW = Wrap && !double.IsInfinity(available.Width) ? Math.Max(1, available.Width) : double.PositiveInfinity;
        float ppd = (float)VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var tf = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
        if (!tf.TryGetGlyphTypeface(out var gt) || !HasAllGlyphs(gt, text))
        {
            // Caractere que a fonte não tem (ex.: emoji): usa o texto normal do WPF, sem espaçamento.
            _fallback = new FormattedText(text, PtBr, FlowDirection.LeftToRight, tf, size, Foreground, ppd)
            {
                TextAlignment = TextAlignment,
            };
            if (!double.IsNaN(LineHeight)) _fallback.LineHeight = LineHeight;
            if (!double.IsInfinity(maxW)) _fallback.MaxTextWidth = maxW;
            return new Size(Math.Min(_fallback.WidthIncludingTrailingWhitespace, maxW), _fallback.Height);
        }

        double track = Tracking * size;
        var map = gt.CharacterToGlyphMap;
        var adv = gt.AdvanceWidths;
        double Adv(char c) => adv[map[c]] * size + track;
        double WordWidth(string w) { double s = 0; foreach (var c in w) s += Adv(c); return s; }

        double content = gt.Height * size;
        _lineHeight = double.IsNaN(LineHeight) ? content : LineHeight;
        double baseline = (_lineHeight - content) / 2 + gt.Baseline * size;
        double spaceW = map.ContainsKey(' ') ? Adv(' ') : size * 0.3;

        var lineTexts = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var words = raw.TrimEnd('\r').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) { lineTexts.Add(""); continue; }
            var cur = new System.Text.StringBuilder();
            double curW = 0;
            // Pontos de quebra: espaços e logo depois de hífens ("AVENTURE-" / "SE").
            var tokens = new List<(string Text, bool Glue)>();
            foreach (var w in words)
            {
                int s = 0;
                for (int i = 1; i < w.Length - 1; i++)
                    if (w[i] == '-') { tokens.Add((w[s..(i + 1)], s > 0)); s = i + 1; }
                tokens.Add((w[s..], s > 0));
            }
            foreach (var (word, glue) in tokens)
            {
                double ww = WordWidth(word);
                double gap = glue ? 0 : spaceW;
                if (cur.Length > 0 && curW + gap + ww <= maxW)
                {
                    if (!glue) cur.Append(' ');
                    cur.Append(word);
                    curW += gap + ww;
                    continue;
                }
                if (cur.Length > 0) { lineTexts.Add(cur.ToString()); cur.Clear(); curW = 0; }
                if (ww <= maxW) { cur.Append(word); curW = ww; continue; }
                // Palavra maior que a linha: quebra no meio (overflow-wrap: break-word).
                foreach (var ch in word)
                {
                    double cw = Adv(ch);
                    if (cur.Length > 0 && curW + cw > maxW) { lineTexts.Add(cur.ToString()); cur.Clear(); curW = 0; }
                    cur.Append(ch);
                    curW += cw;
                }
            }
            if (cur.Length > 0) lineTexts.Add(cur.ToString());
        }

        double maxLine = 0;
        foreach (var lt in lineTexts)
        {
            if (lt.Length == 0) { _lines.Add((null, 0)); continue; }
            var glyphs = new ushort[lt.Length];
            var advances = new double[lt.Length];
            double w = 0;
            for (int i = 0; i < lt.Length; i++)
            {
                glyphs[i] = map[lt[i]];
                advances[i] = adv[glyphs[i]] * size + track;
                w += advances[i];
            }
            var run = new GlyphRun(gt, 0, false, size, ppd, glyphs, new Point(0, baseline), advances,
                                   null, null, null, null, null, null);
            _lines.Add((run, w));
            maxLine = Math.Max(maxLine, w);
        }
        return new Size(maxLine, _lines.Count * _lineHeight);
    }

    static bool HasAllGlyphs(GlyphTypeface gt, string text)
    {
        var map = gt.CharacterToGlyphMap;
        foreach (var c in text)
            if (c != '\n' && c != '\r' && !map.ContainsKey(c)) return false;
        return true;
    }

    void BuildShadowPasses()
    {
        _shadowPasses.Clear();
        var layers = ShadowSpec.Parse(TextShadows);
        for (int li = layers.Length - 1; li >= 0; li--)
        {
            var l = layers[li];
            double sigma = l.Blur / 2;
            if (sigma < 0.3)
            {
                _shadowPasses.Add((l.X, l.Y, ShadowCache.BrushOf(l.Color)));
                continue;
            }
            // Desfoque barato: várias cópias suaves em anéis ao redor (pesos gaussianos).
            var pts = new List<(double, double, double)> { (0, 0, 1) };
            for (int ring = 1; ring <= 2; ring++)
            {
                double rad = sigma * ring, wgt = Math.Exp(-ring * ring / 2.0);
                for (int k = 0; k < 8; k++)
                {
                    double a = k * Math.PI / 4 + (ring == 2 ? Math.PI / 8 : 0);
                    pts.Add((Math.Cos(a) * rad, Math.Sin(a) * rad, wgt));
                }
            }
            double total = pts.Sum(p => p.Item3);
            foreach (var (dx, dy, w) in pts)
                _shadowPasses.Add((l.X + dx, l.Y + dy, ShadowCache.BrushOf(ColorMath.WithAlpha(l.Color, l.Color.A / 255.0 * w / total * 1.6))));
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize)); // a caixa toda responde ao clique
        if (_fallback is not null)
        {
            foreach (var (dx, dy, b) in _shadowPasses)
            {
                _fallback.SetForegroundBrush(b);
                dc.DrawText(_fallback, new Point(dx, dy));
            }
            _fallback.SetForegroundBrush(Foreground);
            dc.DrawText(_fallback, new Point(0, 0));
            return;
        }

        foreach (var (dx, dy, b) in _shadowPasses) DrawLines(dc, width, dx, dy, b);
        DrawLines(dc, width, 0, 0, Foreground);
    }

    void DrawLines(DrawingContext dc, double width, double dx, double dy, Brush brush)
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            var (run, w) = _lines[i];
            if (run is null) continue;
            double x = TextAlignment switch
            {
                TextAlignment.Center => (width - w) / 2,
                TextAlignment.Right => width - w,
                _ => 0,
            };
            dc.PushTransform(new TranslateTransform(x + dx, i * _lineHeight + dy));
            dc.DrawGlyphRun(brush, run);
            dc.Pop();
        }
    }
}
