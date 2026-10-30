using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Quadtab.Controls;

public partial class PageNote : UserControl
{
    public PageNote()
    {
        InitializeComponent();
        // A sombra não aparece no canto cortado da orelha dobrada.
        Root.SizeChanged += (_, e) =>
        {
            double w = e.NewSize.Width, h = e.NewSize.Height;
            var corner = new StreamGeometry();
            using (var c = corner.Open())
            {
                c.BeginFigure(new Point(w - 28, -1), true, true);
                c.LineTo(new Point(w + 1, -1), false, false);
                c.LineTo(new Point(w + 1, 28), false, false);
            }
            var clip = new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(-60, -60, w + 120, h + 120)), corner);
            clip.Freeze();
            PageShadow.Clip = clip;
        };
    }
}
