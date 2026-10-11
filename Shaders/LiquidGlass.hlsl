// Liquid Glass pixel shader for WPF (ps_3_0).
// 运行时由 d3dcompiler_47.dll 编译，无需预编译 .ps 文件。
//
// s0: 隐式输入（玻璃形状所在的矩形，只用来确定区域）
// s1: 抓取到的窗口背后画面（已预模糊），比窗口大，四周带 padding

sampler2D implicitInput : register(s0);
sampler2D backdrop      : register(s1);

float4 ddxDdy      : register(c0);   // WPF 自动填充：(ddx.u, ddx.v, ddy.u, ddy.v)
float  radius      : register(c1);   // 圆角，设备像素
float  refraction  : register(c2);   // 边缘最大位移，设备像素（负数=反向）
float  edgeWidth   : register(c3);   // 折射带宽度，设备像素
float  chroma      : register(c4);   // 色散 0..1
float4 tint        : register(c5);   // 着色颜色
float  tintAmount  : register(c6);   // 着色强度
float  highlight   : register(c7);   // 高光强度
float  lightAngle  : register(c8);   // 光源角度（弧度）
float  saturation  : register(c9);   // 饱和度
float2 padLT       : register(c10);  // 窗口在背景纹理中的左/上 padding（uv）
float2 padRB       : register(c11);  // 右/下 padding（uv）
float  brightness  : register(c12);  // 亮度倍数
float  useBackdrop : register(c13);  // 1 = 自绘折射；0 = 只画高光（系统亚克力模式）

float sdRoundRect(float2 p, float2 halfSize, float r)
{
    float2 q = abs(p) - halfSize + r;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

float3 sampleBackdrop(float2 uvWin)
{
    float2 uvTex = padLT + uvWin * (1.0 - padLT - padRB);
    return tex2D(backdrop, saturate(uvTex)).rgb;
}

float4 main(float2 uv : TEXCOORD0) : COLOR
{
    float2 size = float2(1.0 / ddxDdy.x, 1.0 / ddxDdy.w);
    float2 halfSize = size * 0.5 - 0.5;
    float r = min(radius, min(halfSize.x, halfSize.y));
    float2 p = (uv - 0.5) * size;

    float d = sdRoundRect(p, halfSize, r);
    float alpha = saturate(0.5 - d);

    // 形状法线（指向外侧）
    float2 e = float2(1.0, 0.0);
    float2 n = float2(
        sdRoundRect(p + e.xy, halfSize, r) - sdRoundRect(p - e.xy, halfSize, r),
        sdRoundRect(p + e.yx, halfSize, r) - sdRoundRect(p - e.yx, halfSize, r));
    n = n / max(length(n), 1e-4);

    // depth: 0 在边缘，1 在折射带以内
    float depth = saturate(-d / max(edgeWidth, 1.0));
    float edge = 1.0 - depth;
    // 类似凸透镜的剖面：越靠边缘斜率越大
    float disp = edge * edge * (3.0 - 2.0 * edge);
    disp = disp * disp;

    // 光照
    float2 L = float2(cos(lightAngle), -sin(lightAngle));
    float facing = dot(n, L);
    float rimBand = saturate(1.0 - (-d) / 2.2);           // 最外 ~2px 亮边
    float innerBand = pow(saturate(1.0 - (-d) / (edgeWidth * 0.9 + 1.0)), 3.0);
    float spec = rimBand * (0.55 * pow(saturate(facing), 1.2) + 0.28 * pow(saturate(-facing), 2.0) + 0.10);
    spec += innerBand * 0.22 * pow(saturate(facing), 2.0);
    float gloss = 0.05 * saturate(1.0 - uv.y * 1.6);       // 顶部柔和反光
    float lightAdd = highlight * (spec + gloss);

    // 折射：向内取样 => 边缘处像凸透镜一样放大/弯折
    float2 offPx = -n * refraction * disp;
    float2 uvOff = offPx / size;

    float ca = chroma * disp;
    float3 c;
    c.r = sampleBackdrop(uv + uvOff * (1.0 + ca)).r;
    c.g = sampleBackdrop(uv + uvOff).g;
    c.b = sampleBackdrop(uv + uvOff * (1.0 - ca)).b;

    float lum = dot(c, float3(0.2126, 0.7152, 0.0722));
    c = lerp(lum.xxx, c, saturation);
    c *= brightness;
    c = lerp(c, tint.rgb, tintAmount);

    // 边缘轻微提亮（菲涅尔）
    c += disp * 0.06 * highlight;
    c += lightAdd;

    c = saturate(c);
    float4 glass = float4(c * alpha, alpha);

    // 系统亚克力模式：只输出高光（预乘白色）叠加在 DWM 背景上
    float a = saturate(lightAdd) * alpha;
    float4 rimOnly = float4(a, a, a, a);

    // 不用 if/return 分支，避免在动态分支里做纹理采样
    return lerp(rimOnly, glass, step(0.5, useBackdrop));
}
