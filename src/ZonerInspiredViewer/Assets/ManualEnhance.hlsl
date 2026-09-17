sampler2D Input : register(s0);
sampler2D Tones : register(s1);
float2 Color : register(c0);
float Sharpness : register(c1);
float4 Derivatives : register(c2);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 center = tex2D(Input, uv);
    float3 rgb = center.rgb / max(center.a, 0.0001);
    float2 step = abs(Derivatives.xy) + abs(Derivatives.zw);
    float4 blur = (tex2D(Input, uv - float2(step.x, 0)) + tex2D(Input, uv + float2(step.x, 0))
        + tex2D(Input, uv - float2(0, step.y)) + tex2D(Input, uv + float2(0, step.y))) * 0.25;
    float luma = dot(rgb, float3(0.2126, 0.7152, 0.0722));
    float detail = dot(rgb * blur.a - blur.rgb, float3(0.2126, 0.7152, 0.0722));
    float sharp = detail * Sharpness * saturate(blur.a * 16 - 15);
    float tone = tex2D(Tones, float2(luma * (255.0 / 256.0) + (0.5 / 256.0), 0.5)).r;
    float value = saturate(tone + sharp);
    float3 chroma = rgb - luma;
    float hi = max(chroma.r, max(chroma.g, chroma.b));
    float lo = min(chroma.r, min(chroma.g, chroma.b));
    float muted = 1 - saturate((hi - lo) * 2);
    // Warm skin-like colors receive less positive vibrance; saturation remains a direct control.
    float skin = saturate((rgb.r - rgb.b) * 4);
    float vibrance = Color.x * muted * (1 - skin * 0.65);
    float gain = value / max(luma, 0.001) * max(0, 1 + Color.y + vibrance);
    gain = min(gain, min((1 - value) / max(hi, 0.001), value / max(-lo, 0.001)));
    return float4((value + chroma * gain) * center.a, center.a);
}
