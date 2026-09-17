sampler2D Input : register(s0);
sampler2D Mask : register(s1);
sampler2D Tones : register(s2);
float2 Texel : register(c1);
float4 Derivatives : register(c2);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 center = tex2D(Input, uv);
    float3 rgb = center.rgb / max(center.a, 0.0001);
    float3 masks = tex2D(Mask, uv).rgb;
    float2 step = max(Texel, abs(Derivatives.xy) + abs(Derivatives.zw));
    float4 blur = (tex2D(Input, uv - float2(step.x, 0)) + tex2D(Input, uv + float2(step.x, 0))
        + tex2D(Input, uv - float2(0, step.y)) + tex2D(Input, uv + float2(0, step.y)));
    float luma = dot(rgb, float3(0.2126, 0.7152, 0.0722));
    float x = luma * (255.0 / 256.0) + (0.5 / 256.0);
    float3 tones = tex2D(Tones, float2(x, masks.x * 0.5 + 0.25)).rgb;
    float detail = dot(rgb * blur.a - blur.rgb, float3(0.2126, 0.7152, 0.0722));
    float threshold = tones.b;
    float sharpen = (detail - clamp(detail, -threshold, threshold)) * tones.g * (1 - masks.y * 0.75);
    sharpen = clamp(sharpen, -0.025, 0.025) * saturate(blur.a * 4 - 15);
    float value = saturate(lerp(tones.r, luma, masks.y * 0.22) + sharpen);
    float3 chroma = rgb - luma;
    float maxChroma = max(chroma.r, max(chroma.g, chroma.b));
    float minChroma = min(chroma.r, min(chroma.g, chroma.b));
    float gain = value / max(luma, 0.001) * (1 + masks.z);
    // Compress chroma into available headroom, preserving hue instead of clipping individual channels.
    gain = min(gain, min((1 - value) / max(maxChroma, 0.001), value / max(-minChroma, 0.001)));
    return float4((value + chroma * gain) * center.a, center.a);
}
