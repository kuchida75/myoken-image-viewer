sampler2D Input : register(s0);
float2 Adjustments : register(c0);
float2 Texel : register(c1);
float Threshold : register(c2);
float4 Derivatives : register(c3);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 step = max(Texel, abs(Derivatives.xy) + abs(Derivatives.zw));
    float4 center = tex2D(Input, uv);
    float4 left = tex2D(Input, saturate(uv - float2(step.x, 0)));
    float4 right = tex2D(Input, saturate(uv + float2(step.x, 0)));
    float4 up = tex2D(Input, saturate(uv - float2(0, step.y)));
    float4 down = tex2D(Input, saturate(uv + float2(0, step.y)));
    float3 rgb = center.rgb / max(center.a, 0.0001);
    float4 blur = (left + right + up + down) * 0.25;
    float3 average = blur.rgb / max(blur.a, 0.0001);
    float detail = dot(rgb - average, float3(0.2126, 0.7152, 0.0722));
    float mask = saturate(min(min(left.a, right.a), min(up.a, down.a)) * 16 - 15);
    float correction = sign(detail) * max(abs(detail) - Threshold, 0) * Adjustments.y * mask;
    rgb = saturate(rgb + clamp(correction, -0.035, 0.035));
    rgb = rgb * Adjustments.x / (1 + rgb * (Adjustments.x - 1));
    return float4(rgb * center.a, center.a);
}
