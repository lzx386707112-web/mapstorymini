// Generated from original weatherfx.cpp by tools/export_weather_lightning.py.
// Re-run the exporter when the source SHA in docs/weather-source.json changes.
public static class NativeWeatherLightning
{
    readonly struct LightningPattern
    {
        public readonly uint windowMs, earliestMs, pulseMs, quietEvery, clusterEvery;
        public readonly float minBrightness;
        public readonly int kind;
        public LightningPattern(uint w,uint e,uint p,uint q,uint c,float b,int k)
        {windowMs=w;earliestMs=e;pulseMs=p;quietEvery=q;clusterEvery=c;minBrightness=b;kind=k;}
    }
static uint LightningHash(uint x) {
    x ^= x >> 16;
    x *= 0x7FEB352Du;
    x ^= x >> 15;
    x *= 0x846CA68Bu;
    x ^= x >> 16;
    return x;
}

static LightningPattern LightningPatternForRegion(int palette) {
    switch (palette) {
        // Every palette has its own weather recipe. The visual silhouette is shared
        // where appropriate, but cadence, silence, clustering and intensity are all
        // regional so no two places storm with the same rhythm.
        case 0:          return new LightningPattern( 19000u, 2000u, 1150u, 5u, 0u, 0.84f, 6 );
        case 1:             return new LightningPattern( 22500u, 1600u, 1150u, 4u, 2u, 0.76f, 6 );
        case 2:  return new LightningPattern( 17500u, 1000u, 1100u, 6u, 5u, 0.72f, 0 );
        case 3:          return new LightningPattern( 18000u, 1500u, 1200u, 5u, 0u, 0.76f, 3 );
        case 4:           return new LightningPattern( 25000u, 3000u,  380u, 3u, 0u, 0.88f, 7 );
        case 5:     return new LightningPattern( 11000u,  800u,  640u, 9u, 2u, 0.84f, 4 );
        case 6:            return new LightningPattern(  9500u,  500u,  640u, 8u, 2u, 0.80f, 4 );
        case 7:            return new LightningPattern( 21000u, 2000u, 1500u, 4u, 3u, 0.72f, 5 );
        case 8:          return new LightningPattern( 20000u, 1400u, 1200u, 6u, 0u, 0.72f, 3 );
        case 9:           return new LightningPattern( 28000u, 2500u,  380u, 4u, 0u, 0.78f, 7 );
        case 10:       return new LightningPattern( 17000u, 1500u, 1200u, 5u, 2u, 0.82f, 3 );
        case 11:        return new LightningPattern( 16000u, 1000u,  780u, 6u, 2u, 0.70f, 2 );
        case 12:           return new LightningPattern( 19500u, 2200u, 1200u, 4u, 0u, 0.78f, 3 );
        case 13:        return new LightningPattern( 19000u, 1800u, 1500u, 5u, 3u, 0.76f, 5 );
        case 14:          return new LightningPattern( 12000u,  700u,  780u, 6u, 2u, 0.85f, 2 );
        case 15:           return new LightningPattern( 18000u, 1200u, 1100u, 5u, 4u, 0.70f, 0 );
        case 16:     return new LightningPattern( 14000u, 1000u,  780u, 7u, 3u, 0.78f, 2 );
        case 17:          return new LightningPattern( 23000u, 2000u,  380u, 3u, 0u, 0.80f, 7 );
        case 18:         return new LightningPattern( 13000u,  750u,  780u, 7u, 2u, 0.82f, 2 );
        case 19:          return new LightningPattern( 15000u,  500u, 1250u, 5u, 2u, 0.80f, 1 );
        case 20:            return new LightningPattern( 23000u, 2500u, 1500u, 3u, 2u, 0.72f, 5 );
        case 21:   return new LightningPattern( 26000u, 3000u, 1500u, 3u, 0u, 0.68f, 5 );
        case 22:     return new LightningPattern( 17500u, 1200u, 1200u, 5u, 2u, 0.80f, 3 );
        case 23:    return new LightningPattern( 10500u,  700u,  640u, 8u, 2u, 0.86f, 4 );
        case 24:          return new LightningPattern( 15500u, 1000u,  780u, 5u, 3u, 0.74f, 2 );
        case 25:          return new LightningPattern( 16500u,  900u, 1100u, 6u, 4u, 0.76f, 0 );
        default:                                 return new LightningPattern( 12000u,  700u, 1100u, 7u, 4u, 0.75f, 0 );
    }
}

static float LightningEnvelope(int kind, uint t, uint variation) {
    switch (kind) {
        case 1:
            // Henesys has several genuinely different storm silhouettes, chosen once
            // per strike. This avoids the old clockwork double-flash feeling.
            switch (variation & 3u) {
                case 0u: // One clean, isolated flash.
                    if (t < 95u)  return 1.00f - 0.30f * (float)t / 95.0f;
                    if (t < 590u) return 0.42f * (1.0f - (float)(t - 95u) / 495.0f);
                    return 0.0f;
                case 1u: // A short ragged burst that rolls under the clouds.
                    if (t < 45u)  return 1.00f - 0.30f * (float)t / 45.0f;
                    if (t < 145u) return 0.0f;
                    if (t < 210u) return 0.72f - 0.24f * (float)(t - 145u) / 65.0f;
                    if (t < 320u) return 0.0f;
                    if (t < 410u) return 0.90f - 0.36f * (float)(t - 320u) / 90.0f;
                    if (t < 700u) return 0.28f * (1.0f - (float)(t - 410u) / 290.0f);
                    return 0.0f;
                case 2u: // Long, distant sheet lightning before one bright return.
                    if (t < 170u) return 0.88f - 0.20f * (float)t / 170.0f;
                    if (t < 700u) return 0.58f - 0.17f * (float)(t - 170u) / 530.0f;
                    if (t < 840u) return 0.80f - 0.26f * (float)(t - 700u) / 140.0f;
                    return 0.30f * (1.0f - (float)(t - 840u) / 410.0f);
                default: // A compact double, but with a quieter tail than the classic.
                    if (t < 65u)  return 0.96f - 0.24f * (float)t / 65.0f;
                    if (t < 245u) return 0.34f - 0.14f * (float)(t - 65u) / 180.0f;
                    if (t < 360u) return 0.84f - 0.32f * (float)(t - 245u) / 115.0f;
                    if (t < 880u) return 0.30f * (1.0f - (float)(t - 360u) / 520.0f);
                    return 0.0f;
            }

        case 2:
            // Three quick strikes under a long wet afterglow.
            if (t < 55u)  return 1.00f - 0.22f * (float)t / 55.0f;
            if (t < 145u) return 0.0f;
            if (t < 205u) return 0.82f - 0.20f * (float)(t - 145u) / 60.0f;
            if (t < 315u) return 0.0f;
            if (t < 390u) return 0.94f - 0.32f * (float)(t - 315u) / 75.0f;
            return 0.30f * (1.0f - (float)(t - 390u) / 390.0f);

        case 3:
            // A flash diffused by foliage: one strong entry, then a long green-black glow.
            if (t < 150u) return 0.88f - 0.20f * (float)t / 150.0f;
            if (t < 680u) return 0.46f - 0.16f * (float)(t - 150u) / 530.0f;
            if (t < 800u) return 0.62f - 0.24f * (float)(t - 680u) / 120.0f;
            return 0.30f * (1.0f - (float)(t - 800u) / 400.0f);

        case 4:
            // Buildings and wet pavement read as a rapid shutter of reflected light.
            if (t < 48u)  return 1.00f - 0.24f * (float)t / 48.0f;
            if (t < 105u) return 0.0f;
            if (t < 160u) return 0.78f - 0.20f * (float)(t - 105u) / 55.0f;
            if (t < 225u) return 0.0f;
            if (t < 295u) return 0.92f - 0.34f * (float)(t - 225u) / 70.0f;
            return 0.34f * (1.0f - (float)(t - 295u) / 345.0f);

        case 5:
            // Distant sheet lightning holds the whole high sky pale before returning.
            if (t < 160u) return 0.92f - 0.20f * (float)t / 160.0f;
            if (t < 760u) return 0.46f - 0.13f * (float)(t - 160u) / 600.0f;
            if (t < 940u) return 0.76f - 0.22f * (float)(t - 760u) / 180.0f;
            return 0.42f * (1.0f - (float)(t - 940u) / 560.0f);

        case 6:
            // Snow cloud turns one bolt into a wide, slow sheet of white.
            if (t < 220u) return 0.96f - 0.18f * (float)t / 220.0f;
            if (t < 760u) return 0.58f - 0.22f * (float)(t - 220u) / 540.0f;
            return 0.34f * (1.0f - (float)(t - 760u) / 390.0f);

        case 7:
            // Short, dry crack with almost no lingering wet-air glow.
            if (t < 65u)  return 1.00f - 0.38f * (float)t / 65.0f;
            if (t < 150u) return 0.18f * (1.0f - (float)(t - 65u) / 85.0f);
            if (t < 220u) return 0.70f * (1.0f - (float)(t - 150u) / 70.0f);
            return 0.10f * (1.0f - (float)(t - 220u) / 160.0f);

        default: // 0
            // The default also varies between an isolated strike, the original double,
            // and a broader return flash; regional patterns above keep their own look.
            if ((variation % 3u) == 0u) {
                if (t < 85u)  return 1.00f - 0.32f * (float)t / 85.0f;
                if (t < 540u) return 0.40f * (1.0f - (float)(t - 85u) / 455.0f);
                return 0.0f;
            }
            if ((variation % 3u) == 1u) {
                if (t < 70u)  return 1.00f - 0.26f * (float)t / 70.0f;
                if (t < 170u) return 0.38f - 0.13f * (float)(t - 70u) / 100.0f;
                if (t < 270u) return 0.88f - 0.28f * (float)(t - 170u) / 100.0f;
                if (t < 520u) return 0.44f * (1.0f - (float)(t - 270u) / 250.0f);
                return 0.0f;
            }
            if (t < 150u) return 0.90f - 0.22f * (float)t / 150.0f;
            if (t < 620u) return 0.52f - 0.18f * (float)(t - 150u) / 470.0f;
            if (t < 770u) return 0.82f - 0.30f * (float)(t - 620u) / 150.0f;
            return 0.30f * (1.0f - (float)(t - 770u) / 330.0f);
    }
}

static float LightningEvent(LightningPattern p, uint within, uint h) {
    uint span = p.windowMs - p.earliestMs - p.pulseMs;
    uint at = p.earliestMs + ((h >> 4) % span);
    if (within < at || within - at >= p.pulseMs) {
        return 0.0f;
    }
    // No two storm windows feel equally intense, even when they choose the same silhouette.
    float strength = p.minBrightness
                         + (1.0f - p.minBrightness) * (float)((h >> 24) & 0xFFu) / 255.0f;
    return LightningEnvelope(p.kind, within - at, h >> 12) * strength;
}

public static float Evaluate(uint ageMs, uint seed, int palette) {
    LightningPattern p = LightningPatternForRegion(palette);
    uint window = ageMs / p.windowMs;
    uint h = LightningHash(seed ^ (window * 0x9E3779B9u));
    if (p.quietEvery > 0u && h % p.quietEvery == 0u) {
        return 0.0f;
    }
    uint within = ageMs % p.windowMs;
    float level = LightningEvent(p, within, h);
    // Some fronts roll through in clusters. The independent second event breaks the
    // one-flash-per-window rhythm without sacrificing shared client synchronisation.
    if (p.clusterEvery > 0u && ((h >> 8) % p.clusterEvery) == 0u) {
        float followUp = LightningEvent(p, within, LightningHash(h ^ 0xA511E9B3u));
        if (followUp > level) {
            level = followUp;
        }
    }
    return level;
}

}
