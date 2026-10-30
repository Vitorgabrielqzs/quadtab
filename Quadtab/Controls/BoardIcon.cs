using System.Windows;
using System.Windows.Media;
using Quadtab.Models;
using Quadtab.Rendering;

namespace Quadtab.Controls;

/// <summary>Ícone de cada quadro: o mini quadro do ícone do app, na cor do modelo do quadro.</summary>
public sealed class BoardIcon : FrameworkElement
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(BoardModel), typeof(BoardIcon), new FrameworkPropertyMetadata(BoardModel.Cortica, FrameworkPropertyMetadataOptions.AffectsRender));

    public BoardIcon()
    {
        Width = Height = 22;
        IsHitTestVisible = false;
    }

    public BoardModel Model { get => (BoardModel)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }

    static readonly Dictionary<BoardModel, (Brush Frame, Brush Surface)> Looks = new()
    {
        [BoardModel.Cortica] = (Grad(0xD9B48A, 0xB88752), ColorMath.Brush(Palette.C(0xC4925A))),
        [BoardModel.Branco] = (Grad(0xF5F6F8, 0xBBC1CA), ColorMath.Brush(Palette.C(0xF9FAFB))),
        [BoardModel.Lousa] = (Grad(0x9A6C43, 0x54361D), ColorMath.Brush(Palette.C(0x2C4838))),
        [BoardModel.Pontilhado] = (Grad(0xF6F6F2, 0xCFCFC6), ColorMath.Brush(Palette.C(0xFBFBF8))),
    };

    static readonly Brush Shadow = ColorMath.Brush(Color.FromArgb(50, 0, 0, 0));
    static readonly (Brush Paper, double X, double Y, double R)[] Notes =
    [
        (ColorMath.Brush(Palette.C(0xFFE27A)), 5, 5, -8),
        (ColorMath.Brush(Palette.C(0xFFB1C6)), 11, 6.5, 7),
        (ColorMath.Brush(Palette.C(0xA6D3FF)), 7.5, 11.5, -3),
    ];

    static Brush Grad(uint a, uint b)
    {
        var g = new LinearGradientBrush(Palette.C(a), Palette.C(b), new Point(0.3, 0), new Point(0.7, 1));
        g.Freeze();
        return g;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var (frame, surface) = Looks[Model];
        double s = ActualWidth / 22;
        dc.PushTransform(new ScaleTransform(s, s));
        dc.DrawRoundedRectangle(frame, null, new Rect(0, 0, 22, 22), 6, 6);
        dc.DrawRoundedRectangle(surface, null, new Rect(2.5, 2.5, 17, 17), 3.5, 3.5);
        foreach (var (paper, x, y, r) in Notes)
        {
            dc.PushTransform(new RotateTransform(r, x + 3.25, y + 3.25));
            dc.DrawRoundedRectangle(Shadow, null, new Rect(x, y + 0.6, 6.5, 6.5), 1, 1);
            dc.DrawRoundedRectangle(paper, null, new Rect(x, y, 6.5, 6.5), 1, 1);
            dc.Pop();
        }
        dc.Pop();
    }
}
