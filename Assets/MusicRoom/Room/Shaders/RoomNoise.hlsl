// The fire noise (hash/noise/fbm) and the frost noise (h21/vn) of spec 07, in HLSL float precision (spec 07 section
// 4.1: half breaks the hashes on mobile GPUs). The spec's fract -> frac, mix -> lerp.
#ifndef MUSICROOM_ROOM_NOISE_INCLUDED
#define MUSICROOM_ROOM_NOISE_INCLUDED

float FireHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float FireNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = FireHash(i), b = FireHash(i + float2(1.0, 0.0)), c = FireHash(i + float2(0.0, 1.0)), d = FireHash(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float FireFbm(float2 p)
{
    float s = 0.0, a = 0.5;
    [unroll] for (int i = 0; i < 4; i++)
    {
        s += a * FireNoise(p);
        p = p * 2.03 + float2(1.7, 9.2);
        a *= 0.5;
    }
    return s;
}

float FrostHash(float2 p)
{
    p = frac(p * float2(233.34, 851.73));
    p += dot(p, p + 23.45);
    return frac(p.x * p.y);
}

float FrostNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(FrostHash(i), FrostHash(i + float2(1.0, 0.0)), f.x), lerp(FrostHash(i + float2(0.0, 1.0)), FrostHash(i + float2(1.0, 1.0)), f.x), f.y);
}

// The ground drift hash (127.1 / 311.7 / 19.19)
float GroundHash(float2 p)
{
    p = frac(p * float2(127.1, 311.7));
    p += dot(p, p + 19.19);
    return frac(p.x * p.y);
}

float GroundNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(GroundHash(i), GroundHash(i + float2(1.0, 0.0)), f.x), lerp(GroundHash(i + float2(0.0, 1.0)), GroundHash(i + float2(1.0, 1.0)), f.x), f.y);
}

#endif
