using System.Windows.Controls;

namespace Quadtab.Controls;

/// <summary>Aqui usamos a classe PostIt para desenhar o post-it: papel, faixa adesiva, sombra dupla e fixação.</summary>
public partial class PostIt : UserControl
{
    // A sombra fica parada: mouse em cima ou arrastando, o post-it não "descola" (CLAUDE.md: nada se mexe no hover).
    public PostIt()
    {
        InitializeComponent();
        Root.SizeChanged += (_, e) =>
        {
            // As sombras ocupam 48% da largura e 32% da altura do papel.
            ShadowLeft.Width = ShadowRight.Width = e.NewSize.Width * 0.48;
            ShadowLeft.Height = ShadowRight.Height = e.NewSize.Height * 0.32;
        };
    }
}
