using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Quadtab.Rendering;

namespace Quadtab.Controls;

public partial class PostIt : UserControl
{
    BoardItemContainer? _host;

    public PostIt()
    {
        InitializeComponent();
        Root.SizeChanged += (_, e) =>
        {
            // As sombras ocupam 48% da largura e 32% da altura do papel.
            ShadowLeft.Width = ShadowRight.Width = e.NewSize.Width * 0.48;
            ShadowLeft.Height = ShadowRight.Height = e.NewSize.Height * 0.32;
        };
        Loaded += (_, _) =>
        {
            _host = Tree.FindAncestor<BoardItemContainer>(this);
            if (_host is not null) _host.LiftChanged += OnLift;
        };
        Unloaded += (_, _) =>
        {
            if (_host is not null) _host.LiftChanged -= OnLift;
            _host = null;
        };
    }

    // Mouse em cima: a sombra desce (16 → 24) e clareia um pouco, como se o papel descolasse.
    void OnLift(bool lifted)
    {
        Anim.To(LiftMove, TranslateTransform.YProperty, lifted ? 8 : 0, 0.35, Anim.Ease);
        Anim.To(Lift, OpacityProperty, lifted ? 0.86 : 1, 0.35, Anim.Ease);
    }
}
