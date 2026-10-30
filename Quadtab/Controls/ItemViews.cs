using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Quadtab.Behaviors;
using Quadtab.Models;
using Quadtab.Rendering;
using Quadtab.ViewModels;

namespace Quadtab.Controls;

/// <summary>Base das telas de item feitas em código: acompanha o DataContext (o item pode ser reaproveitado).</summary>
public abstract class ItemViewBase : Grid
{
    protected BoardItemViewModel? Item { get; private set; }

    protected ItemViewBase()
    {
        DataContextChanged += (_, e) =>
        {
            if (Item is not null) Item.PropertyChanged -= OnItemChanged;
            Item = e.NewValue as BoardItemViewModel;
            if (Item is not null) Item.PropertyChanged += OnItemChanged;
            Rebuild();
        };
    }

    void OnItemChanged(object? s, PropertyChangedEventArgs e) => OnItem(e.PropertyName ?? "");

    protected abstract void Rebuild();
    protected virtual void OnItem(string property) { }

    /// <summary>Abre o arquivo ou link com o programa padrão do Windows.</summary>
    public static void OpenExternal(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
    }
}

/// <summary>Caixa de texto livre: fonte, tamanho, cor, alinhamento, negrito, itálico e lista.</summary>
public sealed class TextBoxView : ItemViewBase
{
    public TextBoxView()
    {
        var bg = new Border { CornerRadius = new CornerRadius(8) };
        bg.SetBinding(Border.BackgroundProperty, "FillBrush");
        bg.SetBinding(Border.BorderBrushProperty, "StrokeBrush");
        bg.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("StrokeWidth") { Converter = new ThicknessOf() });
        Children.Add(bg);

        var text = new TextBlock { Margin = new Thickness(10, 6, 10, 6), TextWrapping = TextWrapping.Wrap };
        UI.SetField(text, "text");
        Bind(text, TextBlock.TextProperty, "StyledText");
        BindFont(text);
        text.SetBinding(TextBlock.ForegroundProperty, "TextBrush");
        text.SetBinding(TextBlock.TextAlignmentProperty, "TextAlign");
        text.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("IsEditingText") { Converter = (System.Windows.Data.IValueConverter)Application.Current.FindResource("Hidden") });
        Children.Add(text);

        var edit = new TextBox { Style = (Style)Application.Current.FindResource("EditBox"), Margin = new Thickness(6, 2, 6, 2) };
        EditBox.SetMode(edit, "multi");
        edit.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("Text") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
        BindFont(edit);
        edit.SetBinding(TextBox.ForegroundProperty, "TextBrush");
        edit.SetBinding(TextBox.TextAlignmentProperty, "TextAlign");
        edit.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("IsEditingText") { Converter = (System.Windows.Data.IValueConverter)Application.Current.FindResource("Visible") });
        Children.Add(edit);

        SetBinding(WidthProperty, "ItemWidth");
        SetBinding(MinHeightProperty, "ItemMinHeight");
        SetBinding(OpacityProperty, "ItemOpacity");
        Background = Brushes.Transparent;
    }

    static void Bind(FrameworkElement el, DependencyProperty dp, string path) => el.SetBinding(dp, path);

    internal static void BindFont(FrameworkElement el)
    {
        el.SetBinding(TextElement.FontFamilyProperty, "FontFamilyValue");
        el.SetBinding(TextElement.FontSizeProperty, "FontSizeValue");
        el.SetBinding(TextElement.FontWeightProperty, "FontWeightValue");
        el.SetBinding(TextElement.FontStyleProperty, "FontStyleValue");
    }

    protected override void Rebuild() { }
}

/// <summary>Converte um número em espessura de borda.</summary>
public sealed class ThicknessOf : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c) => new Thickness(value is double d ? d : 0);
    public object ConvertBack(object value, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Forma da biblioteca, com preenchimento, borda, opacidade e texto dentro.</summary>
public sealed class ShapeView : ItemViewBase
{
    public ShapeView()
    {
        var path = new System.Windows.Shapes.Path { Stretch = Stretch.Fill, StrokeLineJoin = PenLineJoin.Round };
        path.SetBinding(System.Windows.Shapes.Path.DataProperty, "ShapeGeometry");
        path.SetBinding(System.Windows.Shapes.Shape.FillProperty, "FillBrush");
        path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, "StrokeBrush");
        path.SetBinding(System.Windows.Shapes.Shape.StrokeThicknessProperty, "StrokeWidth");
        Children.Add(path);

        var text = new TextBlock { Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        UI.SetField(text, "text");
        text.SetBinding(TextBlock.TextProperty, "Text");
        TextBoxView.BindFont(text);
        text.SetBinding(TextBlock.ForegroundProperty, "TextBrush");
        text.SetBinding(TextBlock.TextAlignmentProperty, "TextAlign");
        text.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("IsEditingText") { Converter = (System.Windows.Data.IValueConverter)Application.Current.FindResource("Hidden") });
        Children.Add(text);

        var edit = new TextBox { Style = (Style)Application.Current.FindResource("EditBox"), Margin = new Thickness(14), VerticalAlignment = VerticalAlignment.Center };
        EditBox.SetMode(edit, "note");
        edit.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("Text") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
        TextBoxView.BindFont(edit);
        edit.SetBinding(TextBox.TextAlignmentProperty, "TextAlign");
        edit.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("IsEditingText") { Converter = (System.Windows.Data.IValueConverter)Application.Current.FindResource("Visible") });
        Children.Add(edit);

        SetBinding(WidthProperty, "ItemWidth");
        SetBinding(HeightProperty, "ItemHeight");
        SetBinding(OpacityProperty, "ItemOpacity");
    }

    protected override void Rebuild() { }
}

/// <summary>
/// Linha, seta ou conector (reta, curva ou em ângulo), com pontas configuráveis e, como no Canvas do
/// Obsidian, um rótulo opcional no meio (2 cliques na linha escrevem nele).
/// </summary>
public sealed class LineView : ItemViewBase
{
    public const double Pad = 24;
    readonly LineVisual _visual = new();
    readonly Canvas _labelLayer = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    readonly Border _label;
    readonly TextBlock _labelText;
    readonly TextBox _labelEdit;

    public LineView()
    {
        Children.Add(_visual);
        _labelText = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = LabelInk, TextAlignment = TextAlignment.Center, MaxWidth = 220, TextWrapping = TextWrapping.Wrap };
        UI.SetField(_labelText, "text");
        _labelText.SetBinding(TextBlock.TextProperty, "Text");
        _labelText.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("IsEditingText") { Converter = (System.Windows.Data.IValueConverter)Application.Current.FindResource("Hidden") });
        _labelEdit = new TextBox { Style = (Style)Application.Current.FindResource("EditBox"), FontSize = 13, FontWeight = FontWeights.SemiBold, MinWidth = 80, TextAlignment = TextAlignment.Center };
        EditBox.SetMode(_labelEdit, "note");
        _labelEdit.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("Text") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
        _labelEdit.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("IsEditingText") { Converter = (System.Windows.Data.IValueConverter)Application.Current.FindResource("Visible") });
        var stack = new Grid();
        stack.Children.Add(_labelText);
        stack.Children.Add(_labelEdit);
        _label = new Border { Background = LabelFill, CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 3, 7, 3), Child = stack };
        _label.SizeChanged += (_, _) => PlaceLabel();
        _labelLayer.Children.Add(_label);
        Children.Add(_labelLayer);
    }

    static readonly Brush LabelFill = Frozen(Color.FromArgb(235, 255, 255, 255));
    static readonly Brush LabelInk = Frozen(Color.FromRgb(0x1D, 0x21, 0x30));
    static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    protected override void Rebuild()
    {
        _visual.SetItem(Item);
        PlaceLabel();
    }

    protected override void OnItem(string p)
    {
        if (p is nameof(BoardItemViewModel.Line) or nameof(BoardItemViewModel.Style) or nameof(BoardItemViewModel.X) or nameof(BoardItemViewModel.Y))
        {
            _visual.Refresh();
            PlaceLabel();
        }
        else if (p is nameof(BoardItemViewModel.Text) or nameof(BoardItemViewModel.Editing)) PlaceLabel();
    }

    /// <summary>Rótulo centrado no meio do caminho (some quando está vazio e fora da edição).</summary>
    void PlaceLabel()
    {
        if (Item?.Line is not { } l) return;
        bool show = Item.IsEditingText || !string.IsNullOrWhiteSpace(Item.Text);
        _label.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        Build(l, new Vector(Item.X, Item.Y)).GetFlattenedPathGeometry().GetPointAtFractionLength(0.5, out var mid, out _);
        Canvas.SetLeft(_label, Math.Round(mid.X - _label.ActualWidth / 2));
        Canvas.SetTop(_label, Math.Round(mid.Y - _label.ActualHeight / 2));
    }

    /// <summary>Pontos da linha em coordenadas do mundo.</summary>
    public static (Point A, Point B) Ends(LineData l) => (new Point(l.De.X, l.De.Y), new Point(l.Para.X, l.Para.Y));

    /// <summary>Caminho da linha (reta, curva ou ângulo) em coordenadas locais.</summary>
    public static Geometry Build(LineData l, Vector origin)
    {
        var (a, b) = Ends(l);
        a -= origin; b -= origin;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(a, false, false);
            switch (l.Tipo)
            {
                case "curva":
                    var na = Normal(l.De.Lado, b - a);
                    var nb = Normal(l.Para.Lado, a - b);
                    double d = Math.Max(40, (b - a).Length / 2.5);
                    c.BezierTo(a + na * d, b + nb * d, b, true, true);
                    break;
                case "angulo":
                    bool horizontal = l.De.Lado is "esquerda" or "direita" || (l.De.Lado is null && Math.Abs(b.X - a.X) > Math.Abs(b.Y - a.Y));
                    if (horizontal)
                    {
                        double mx = (a.X + b.X) / 2;
                        c.LineTo(new Point(mx, a.Y), true, true);
                        c.LineTo(new Point(mx, b.Y), true, true);
                    }
                    else
                    {
                        double my = (a.Y + b.Y) / 2;
                        c.LineTo(new Point(a.X, my), true, true);
                        c.LineTo(new Point(b.X, my), true, true);
                    }
                    c.LineTo(b, true, true);
                    break;
                default:
                    c.LineTo(b, true, true);
                    break;
            }
        }
        g.Freeze();
        return g;
    }

    static Vector Normal(string? side, Vector fallback) => side switch
    {
        "cima" => new Vector(0, -1),
        "baixo" => new Vector(0, 1),
        "esquerda" => new Vector(-1, 0),
        "direita" => new Vector(1, 0),
        _ => Math.Abs(fallback.X) > Math.Abs(fallback.Y) ? new Vector(Math.Sign(fallback.X), 0) : new Vector(0, Math.Sign(fallback.Y)),
    };

    sealed class LineVisual : FrameworkElement
    {
        BoardItemViewModel? _item;

        public void SetItem(BoardItemViewModel? item)
        {
            _item = item;
            Refresh();
        }

        public void Refresh()
        {
            InvalidateMeasure();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (_item?.Line is not { } l) return default;
            var (a, b) = Ends(l);
            return new Size(Math.Abs(b.X - a.X) + 2 * Pad, Math.Abs(b.Y - a.Y) + 2 * Pad);
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (_item?.Line is not { } l) return;
            var origin = new Vector(_item.X, _item.Y);
            var geo = Build(l, origin);
            var brush = _item.StrokeBrush;
            double w = Math.Max(1, _item.StrokeWidth);
            var pen = new Pen(brush, w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            pen.Freeze();
            // Faixa invisível larga para ficar fácil de clicar.
            var hit = new Pen(Brushes.Transparent, Math.Max(14, w + 10));
            hit.Freeze();
            dc.DrawGeometry(null, hit, geo);
            dc.PushOpacity(_item.ItemOpacity);
            dc.DrawGeometry(null, pen, geo);

            var flat = geo.GetFlattenedPathGeometry();
            var fig = flat.Figures.FirstOrDefault();
            if (fig is not null)
            {
                var pts = new List<Point> { fig.StartPoint };
                foreach (var seg in fig.Segments)
                    if (seg is PolyLineSegment pl) pts.AddRange(pl.Points);
                    else if (seg is LineSegment ls) pts.Add(ls.Point);
                if (pts.Count >= 2)
                {
                    Head(dc, l.Para.Ponta, pts[^1], pts[^2], brush, w);
                    Head(dc, l.De.Ponta, pts[0], pts[1], brush, w);
                }
            }
            dc.Pop();
        }

        static void Head(DrawingContext dc, string kind, Point tip, Point prev, Brush brush, double w)
        {
            var dir = tip - prev;
            if (dir.Length < 0.01) return;
            dir.Normalize();
            if (kind == "seta")
            {
                double s = 6 + w * 2.2;
                var n = new Vector(-dir.Y, dir.X);
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(tip + dir * w * 0.5, true, true);
                    c.LineTo(tip - dir * s + n * s * 0.55, true, true);
                    c.LineTo(tip - dir * s * 0.72, true, true);
                    c.LineTo(tip - dir * s - n * s * 0.55, true, true);
                }
                g.Freeze();
                dc.DrawGeometry(brush, null, g);
            }
            else if (kind == "bolinha") dc.DrawEllipse(brush, null, tip, 3 + w, 3 + w);
        }
    }
}

/// <summary>Desenho à mão: traços suavizados de caneta, lápis e marcador.</summary>
public sealed class DrawingView : ItemViewBase
{
    readonly InkVisual _ink = new();

    public DrawingView()
    {
        Children.Add(_ink);
        SetBinding(OpacityProperty, "ItemOpacity");
    }

    protected override void Rebuild() => _ink.SetItem(Item);

    protected override void OnItem(string p)
    {
        if (p is nameof(BoardItemViewModel.Strokes) or nameof(BoardItemViewModel.W) or nameof(BoardItemViewModel.H)) _ink.SetItem(Item);
    }

    /// <summary>Caminho suave por pontos (curvas entre os pontos médios).</summary>
    public static Geometry Smooth(IReadOnlyList<double> p)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            int n = p.Count / 2;
            if (n == 0) return g;
            var first = new Point(p[0], p[1]);
            c.BeginFigure(first, false, false);
            if (n == 1) c.LineTo(first + new Vector(0.1, 0), true, true);
            for (int i = 1; i < n - 1; i++)
            {
                var cur = new Point(p[i * 2], p[i * 2 + 1]);
                var next = new Point(p[i * 2 + 2], p[i * 2 + 3]);
                c.QuadraticBezierTo(cur, new Point((cur.X + next.X) / 2, (cur.Y + next.Y) / 2), true, true);
            }
            if (n > 1) c.LineTo(new Point(p[^2], p[^1]), true, true);
        }
        g.Freeze();
        return g;
    }

    public static Pen PenFor(InkStroke s)
    {
        var color = (Color)ColorConverter.ConvertFromString(s.Cor);
        var pen = s.Ferramenta switch
        {
            "marcador" => new Pen(ShadowCache.BrushOf(Color.FromArgb(110, color.R, color.G, color.B)), s.Espessura * 3)
            { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square, LineJoin = PenLineJoin.Round },
            "lapis" => new Pen(ShadowCache.BrushOf(Color.FromArgb(200, color.R, color.G, color.B)), Math.Max(1, s.Espessura * 0.6))
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round },
            _ => new Pen(ShadowCache.BrushOf(color), s.Espessura) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round },
        };
        pen.Freeze();
        return pen;
    }

    sealed class InkVisual : FrameworkElement
    {
        BoardItemViewModel? _item;
        List<(Geometry G, Pen P)> _strokes = [];
        Rect _bounds;

        public void SetItem(BoardItemViewModel? item)
        {
            _item = item;
            _strokes = item?.Strokes?.Select(s => (Smooth(s.Pontos), PenFor(s))).ToList() ?? [];
            _bounds = Rect.Empty;
            foreach (var (g, p) in _strokes) _bounds.Union(g.GetRenderBounds(p));
            if (_bounds.IsEmpty) _bounds = new Rect(0, 0, 1, 1);
            if (item is not null) item.InkBase = new Size(_bounds.Right, _bounds.Bottom);
            InvalidateMeasure();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (_item is null) return default;
            return new Size(Math.Max(1, _bounds.Right * _item.DrawScaleX), Math.Max(1, _bounds.Bottom * _item.DrawScaleY));
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (_item is null) return;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize)); // a área toda responde ao clique
            dc.PushTransform(new ScaleTransform(_item.DrawScaleX, _item.DrawScaleY));
            foreach (var (g, p) in _strokes) dc.DrawGeometry(null, p, g);
            dc.Pop();
        }
    }
}

/// <summary>Imagem colada: recorte e cantos arredondados.</summary>
public sealed class ImageView : ItemViewBase
{
    readonly Border _img = new();
    readonly ImageBrush _brush = new() { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox };

    public ImageView()
    {
        Children.Add(new Shadow { Layers = "0 10 22 -8 #59321900, 0 1 3 0 #26000000", CornerRadius = 4 });
        _img.Background = _brush;
        Children.Add(_img);
        SetBinding(WidthProperty, "ItemWidth");
        SetBinding(HeightProperty, "ItemHeight");
        SetBinding(OpacityProperty, "ItemOpacity");
    }

    protected override void Rebuild()
    {
        if (Item is null) return;
        _brush.ImageSource = Load(Item.FileFullPath, 1600);
        Apply();
    }

    protected override void OnItem(string p)
    {
        if (p is nameof(BoardItemViewModel.Crop) or nameof(BoardItemViewModel.Radius)) Apply();
        if (p == nameof(BoardItemViewModel.FilePath)) Rebuild();
    }

    void Apply()
    {
        if (Item is null) return;
        var c = Item.Crop;
        _brush.Viewbox = new Rect(c[0], c[1], Math.Max(0.02, 1 - c[0] - c[2]), Math.Max(0.02, 1 - c[1] - c[3]));
        _img.CornerRadius = new CornerRadius(Item.Radius);
        ((Shadow)Children[0]).CornerRadius = Item.Radius;
    }

    public static BitmapSource? Load(string? path, int decodeWidth)
    {
        if (path is null || !File.Exists(path)) return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.UriSource = new Uri(path);
            img.DecodePixelWidth = decodeWidth;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; }
    }
}

/// <summary>Vídeo: clicar no botão toca e pausa.</summary>
public sealed class VideoView : ItemViewBase
{
    readonly MediaElement _media = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Stop, Stretch = Stretch.UniformToFill, ScrubbingEnabled = true };
    readonly Border _play;
    bool _playing;

    public VideoView()
    {
        var clip = new Border { CornerRadius = new CornerRadius(14), Background = Brushes.Black, ClipToBounds = true, Child = _media };
        Children.Add(new Shadow { Layers = "0 10 22 -8 #59321900", CornerRadius = 14 });
        Children.Add(clip);
        _play = new Border
        {
            Width = 54, Height = 54, CornerRadius = new CornerRadius(27), Background = ShadowCache.BrushOf(Color.FromArgb(150, 0, 0, 0)),
            Child = new Icon { Data = (Geometry)Application.Current.FindResource("IcPlay"), Foreground = Brushes.White, Size = 22 },
            Cursor = Cursors.Hand,
        };
        _play.MouseLeftButtonDown += (_, e) => { Toggle(); e.Handled = true; };
        Children.Add(_play);
        _media.MediaOpened += (_, _) => { _media.Play(); _media.Pause(); _media.Position = TimeSpan.FromMilliseconds(1); };
        _media.MediaEnded += (_, _) => { _media.Stop(); _playing = false; _play.Opacity = 1; };
        SetBinding(WidthProperty, "ItemWidth");
        SetBinding(HeightProperty, "ItemHeight");
    }

    void Toggle()
    {
        if (_playing) _media.Pause(); else _media.Play();
        _playing = !_playing;
        _play.Opacity = _playing ? 0.25 : 1;
    }

    protected override void Rebuild()
    {
        if (Item?.FileFullPath is { } p && File.Exists(p)) _media.Source = new Uri(p);
    }
}

/// <summary>Cartão de arquivo (PDF e outros): ícone, nome e prévia da 1ª página do PDF.</summary>
public sealed class FileView : ItemViewBase
{
    readonly Border _preview = new() { CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 10) };
    readonly TextBlock _name = new() { FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = ShadowCache.BrushOf(Color.FromRgb(0x1D, 0x21, 0x30)) };
    readonly TextBlock _ext = new() { FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(8, 2, 8, 2) };

    public FileView()
    {
        Children.Add(new Shadow { Layers = "0 12 24 -10 #66321900, 0 1 3 0 #1F000000", CornerRadius = 16 });
        var card = new Border { CornerRadius = new CornerRadius(16), Background = ShadowCache.BrushOf(Color.FromRgb(0xFB, 0xFA, 0xF5)), Padding = new Thickness(12) };
        var dock = new DockPanel();
        var foot = new DockPanel();
        DockPanel.SetDock(foot, Dock.Bottom);
        var badge = new Border { CornerRadius = new CornerRadius(6), Background = ShadowCache.BrushOf(Color.FromRgb(0xE5, 0x31, 0x2B)), Child = _ext, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        foot.Children.Add(badge);
        foot.Children.Add(_name);
        dock.Children.Add(foot);
        dock.Children.Add(_preview);
        card.Child = dock;
        Children.Add(card);
        SetBinding(WidthProperty, "ItemWidth");
        SetBinding(HeightProperty, "ItemHeight");
    }

    protected override void Rebuild()
    {
        if (Item is null) return;
        _name.Text = Item.FileName;
        _ext.Text = Item.FileExtension.Length > 0 ? Item.FileExtension : "ARQ";
        var img = ImageView.Load(Item.PreviewImageFull, 600);
        _preview.Background = img is null
            ? ShadowCache.BrushOf(Color.FromArgb(18, 0, 0, 0))
            : new ImageBrush(img) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Top };
        _preview.Child = img is null
            ? new Icon { Data = (Geometry)Application.Current.FindResource("IcFile"), Size = 48, Foreground = ShadowCache.BrushOf(Color.FromRgb(0x64, 0x6A, 0x79)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            : null;
    }

    protected override void OnItem(string p)
    {
        if (p is nameof(BoardItemViewModel.PreviewImage) or nameof(BoardItemViewModel.FilePath)) Rebuild();
    }
}

/// <summary>Cartão de link: imagem de prévia, título e site.</summary>
public sealed class LinkView : ItemViewBase
{
    readonly Border _image = new() { Height = 168, CornerRadius = new CornerRadius(14, 14, 0, 0) };
    readonly TextBlock _title = new() { FontSize = 15, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, MaxHeight = 42, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = ShadowCache.BrushOf(Color.FromRgb(0x1D, 0x21, 0x30)) };
    readonly TextBlock _site = new() { FontSize = 12.5, Foreground = ShadowCache.BrushOf(Color.FromRgb(0x64, 0x6A, 0x79)), Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };

    public LinkView()
    {
        Children.Add(new Shadow { Layers = "0 12 24 -10 #66321900, 0 1 3 0 #1F000000", CornerRadius = 14 });
        var stack = new StackPanel();
        stack.Children.Add(_image);
        var body = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };
        var row = new DockPanel();
        var icon = new Icon { Data = (Geometry)Application.Current.FindResource("IcLink"), Size = 16, Foreground = ShadowCache.BrushOf(Color.FromRgb(0x0A, 0x84, 0xFF)), Margin = new Thickness(0, 0, 6, 0) };
        row.Children.Add(icon);
        row.Children.Add(_site);
        body.Children.Add(_title);
        body.Children.Add(row);
        stack.Children.Add(body);
        Children.Add(new Border { CornerRadius = new CornerRadius(14), Background = Brushes.White, Child = stack, ClipToBounds = true });
        SetBinding(WidthProperty, "ItemWidth");
    }

    protected override void Rebuild()
    {
        if (Item is null) return;
        _title.Text = string.IsNullOrWhiteSpace(Item.Title) ? Item.Url : Item.Title;
        _site.Text = Item.Site ?? Item.Url;
        var img = ImageView.Load(Item.PreviewImageFull, 700);
        _image.Visibility = img is null ? Visibility.Collapsed : Visibility.Visible;
        _image.Background = img is null ? null : new ImageBrush(img) { Stretch = Stretch.UniformToFill };
    }

    protected override void OnItem(string p)
    {
        if (p is nameof(BoardItemViewModel.Title) or nameof(BoardItemViewModel.Site) or nameof(BoardItemViewModel.PreviewImage) or nameof(BoardItemViewModel.Url)) Rebuild();
    }
}

/// <summary>Tabela com linhas e colunas editáveis (dois cliques numa célula).</summary>
public sealed class TableView : ItemViewBase
{
    readonly Grid _grid = new();
    readonly Border _frame = new() { CornerRadius = new CornerRadius(10), ClipToBounds = true };
    (int R, int C)? _editing;

    public TableView()
    {
        Children.Add(new Shadow { Layers = "0 10 22 -10 #4D321900", CornerRadius = 10 });
        _frame.Child = _grid;
        Children.Add(_frame);
        SetBinding(WidthProperty, "ItemWidth");
        SetBinding(OpacityProperty, "ItemOpacity");
    }

    protected override void OnItem(string p)
    {
        if (p is nameof(BoardItemViewModel.Table) or nameof(BoardItemViewModel.Style)) Rebuild();
    }

    protected override void Rebuild()
    {
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();
        if (Item?.Table is not { Count: > 0 } t) return;
        int cols = t.Max(r => r.Count);
        _frame.Background = Item.FillBrush;
        _frame.BorderBrush = Item.StrokeBrush;
        _frame.BorderThickness = new Thickness(Item.StrokeWidth);
        for (int c = 0; c < cols; c++) _grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int r = 0; r < t.Count; r++)
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = 38 });
            for (int c = 0; c < cols; c++)
            {
                string text = c < t[r].Count ? t[r][c] : "";
                var cell = new Border
                {
                    BorderBrush = Item.StrokeBrush,
                    BorderThickness = new Thickness(c > 0 ? Item.StrokeWidth : 0, r > 0 ? Item.StrokeWidth : 0, 0, 0),
                    Background = r == 0 ? ShadowCache.BrushOf(Color.FromArgb(14, 0, 0, 0)) : Brushes.Transparent,
                    Padding = new Thickness(10, 7, 10, 7),
                    Tag = (r, c),
                };
                if (_editing == (r, c))
                {
                    var tb = new TextBox { Style = (Style)Application.Current.FindResource("EditBox"), Text = text, AcceptsReturn = false };
                    TextBoxView.BindFont(tb);
                    int rr = r, cc = c;
                    tb.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
                    tb.KeyDown += (_, e) =>
                    {
                        if (e.Key is Key.Enter or Key.Escape) { Commit(rr, cc, tb.Text, null); e.Handled = true; }
                        else if (e.Key == Key.Tab) { Commit(rr, cc, tb.Text, cc + 1 < cols ? (rr, cc + 1) : rr + 1 < t.Count ? (rr + 1, 0) : null); e.Handled = true; }
                    };
                    tb.LostKeyboardFocus += (_, _) => { if (_editing == (rr, cc)) Commit(rr, cc, tb.Text, null); };
                    cell.Child = tb;
                }
                else
                {
                    var tbk = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontWeight = r == 0 ? FontWeights.SemiBold : FontWeights.Normal };
                    TextBoxView.BindFont(tbk);
                    if (r == 0) tbk.FontWeight = FontWeights.SemiBold;
                    tbk.SetBinding(TextBlock.ForegroundProperty, "TextBrush");
                    cell.Child = tbk;
                }
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                _grid.Children.Add(cell);
            }
        }
    }

    /// <summary>Dois cliques: edita a célula sob o ponto (coordenadas desta tela).</summary>
    public void EditAt(Point p)
    {
        foreach (Border cell in _grid.Children)
        {
            var r = new Rect(cell.TranslatePoint(default, this), cell.RenderSize);
            if (r.Contains(p) && cell.Tag is (int row, int col))
            {
                App.ViewModel.Checkpoint();
                _editing = (row, col);
                Rebuild();
                return;
            }
        }
    }

    void Commit(int r, int c, string text, (int, int)? next)
    {
        if (Item?.Table is not { } t) return;
        _editing = next;
        var copy = t.Select(row => row.ToList()).ToList();
        while (copy[r].Count <= c) copy[r].Add("");
        copy[r][c] = text;
        Item.Table = copy;
        if (next is null) Rebuild();
    }
}
