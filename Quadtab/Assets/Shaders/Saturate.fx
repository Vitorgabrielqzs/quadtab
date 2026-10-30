// Saturação (igual ao filtro CSS saturate): amount = 1 não muda nada, 1.9 = 190%.
// Compilado com: fxc /T ps_2_0 /E main /Fo Saturate.ps Saturate.fx
sampler2D input : register(s0);
float amount : register(c0);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 c = tex2D(input, uv);
    float l = dot(c.rgb, float3(0.213, 0.715, 0.072));
    float3 rgb = l + amount * (c.rgb - l);
    return float4(clamp(rgb, 0, c.a), c.a);
}
