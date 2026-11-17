// The snowy window joins in (the playback show; Room/Decor/WindowShow.cs drives it): the shader side. Globals written by
// WindowShow once a frame while a playback shows, and once more as exact zeros when it rests, so at rest every function
// here returns exactly what the plain window drew before:
//   _WindowSwirl   (gust 0..1, eddy phase rad, whirl rad, lift m)        the snow's gusts (Snow.shader)
//   _WindowAurora  (level 0..1, clock s, ripple 0..1, 0)                  the northern lights (NightSky)
//   _WindowAuroraA / B / C (linear rgb, flare 0..1)                       the three curtains: low, middle, high notes
//   _WindowSnowTint (linear rgb, 0)                                      the aurora's light on the flakes
// WindowSwirl is mirrored on the CPU by WindowShowModel.SwirlOffset (EditMode tests). Float precision throughout.
#ifndef MUSICROOM_WINDOW_SHOW_INCLUDED
#define MUSICROOM_WINDOW_SHOW_INCLUDED

#include "RoomNoise.hlsl"

float4 _WindowSwirl, _WindowAurora, _WindowAuroraA, _WindowAuroraB, _WindowAuroraC, _WindowSnowTint;

#define WINDOW_WHIRL_Y 0.1
// The aurora's overall brightness (linear light at a full curtain).
#define WINDOW_AURORA_GAIN 0.45

// A flake at (x, y) in the window frame, d metres behind the glass, seed = (size, sway, rate, phase): small eddies (each
// flake circles its own place), a lift, and a whirl about the middle of the field, all scaled by the gust.
float2 WindowSwirl(float2 xy, float d, float4 seed)
{
    float g = _WindowSwirl.x;
    if (g <= 0.0) return xy;
    float ph = seed.w * 6.2831853;
    float sp = _WindowSwirl.y * lerp(0.7, 1.6, seed.z) + ph * 1.3;
    float er = g * lerp(0.04, 0.2, seed.y) * (1.0 + 0.05 * d);
    float x = xy.x + er * cos(sp);
    float y = xy.y + er * 0.8 * sin(sp) + _WindowSwirl.w * lerp(0.4, 1.0, seed.x);
    float r = 0.55 + 0.12 * d;
    float2 q = float2(x, y - WINDOW_WHIRL_Y);
    float ang = _WindowSwirl.z * exp(-dot(q, q) / (r * r));
    float s = sin(ang), c = cos(ang);
    return float2(c * q.x - s * q.y, s * q.x + c * q.y + WINDOW_WHIRL_Y);
}

// The northern lights at a point of the sky, p in metres (x along the horizon, y up; the curtains' lower edges hang at
// about y 3..5.5 and fade upward over a few metres): linear light to add, 0 when the show rests. Three curtains, each
// folding slowly, rippling on the beat, made of fine vertical rays, brighter in drifting patches, its own colour at the
// lower edge leaning to the next curtain's colour higher up.
float3 WindowAurora(float2 p)
{
    float level = _WindowAurora.x;
    if (level <= 0.0) return float3(0.0, 0.0, 0.0);
    float t = _WindowAurora.y, rip = _WindowAurora.z;
    float3 sum = float3(0.0, 0.0, 0.0);
    [unroll] for (int k = 0; k < 3; k++)
    {
        float4 band = k == 0 ? _WindowAuroraA : (k == 1 ? _WindowAuroraB : _WindowAuroraC);
        float3 upper = k == 0 ? _WindowAuroraB.rgb : (k == 1 ? _WindowAuroraC.rgb : _WindowAuroraB.rgb);
        float fk = (float)k;
        float x = p.x * (0.11 + 0.03 * fk) + fk * 3.7;
        float fold = 0.9 * sin(x * 1.7 + t * (0.13 + 0.05 * fk)) + 0.5 * sin(x * 3.9 - t * (0.23 + 0.04 * fk) + fk);
        fold += rip * 0.5 * sin(x * 7.0 - t * 5.0 + fk * 2.0);
        float baseY = 3.1 + 1.0 * fk + fold * (0.55 + 0.15 * fk);
        float h = p.y - baseY;
        float height = 1.8 + 0.7 * fk;
        float prof = smoothstep(-0.3, 0.12, h) * exp(-max(h, 0.0) / height);
        float r = FireNoise(float2(x * 14.0 + fold * 2.5, t * 0.35 + fk * 5.3));
        float rays = 0.4 + 0.6 * r * r;
        float patch = smoothstep(0.2, 0.75, FireNoise(float2(x * 2.2 - t * 0.07, fk * 4.1 + 0.5)));
        float a = prof * rays * (0.25 + 0.75 * patch) * (0.35 + 0.65 * band.w);
        float3 col = lerp(band.rgb, upper * 0.8, saturate(h / (height * 1.6)));
        sum += col * a;
    }
    return sum * (level * WINDOW_AURORA_GAIN);
}

#endif
