using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Quadtab.Rendering;

/// <summary>Saturação de cor feita na placa de vídeo (shader em Assets/Shaders/Saturate.fx).</summary>
public sealed class SaturateEffect : ShaderEffect
{
    static readonly PixelShader Shader = CreateShader();

    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty("Input", typeof(SaturateEffect), 0);

    public static readonly DependencyProperty AmountProperty = DependencyProperty.Register(
        nameof(Amount), typeof(double), typeof(SaturateEffect), new UIPropertyMetadata(1.0, PixelShaderConstantCallback(0)));

    public SaturateEffect()
    {
        PixelShader = Shader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(AmountProperty);
    }

    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public double Amount { get => (double)GetValue(AmountProperty); set => SetValue(AmountProperty, value); }

    static PixelShader CreateShader()
    {
        var ps = new PixelShader { UriSource = new Uri("pack://application:,,,/Assets/Shaders/Saturate.ps") };
        ps.Freeze();
        return ps;
    }
}
