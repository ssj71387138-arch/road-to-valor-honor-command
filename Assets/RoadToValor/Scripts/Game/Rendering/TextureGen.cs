using UnityEngine;

namespace RoadToValor.Game
{
    /// <summary>Procedural, tileable textures for terrain layers, particles and UI (no external assets needed).</summary>
    public static class TextureGen
    {
        private static Texture2D _soft;
        private static Texture2D _white;

        public static Texture2D White
        {
            get
            {
                if (_white != null) return _white;
                _white = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "RTV_White" };
                _white.SetPixels32(new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) });
                _white.Apply();
                return _white;
            }
        }

        public static Texture2D SoftCircle
        {
            get
            {
                if (_soft != null) return _soft;
                const int s = 64;
                _soft = new Texture2D(s, s, TextureFormat.RGBA32, false) { name = "RTV_Soft", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[s * s];
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a * (3f - 2f * a);
                        px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255));
                    }
                _soft.SetPixels32(px);
                _soft.Apply();
                return _soft;
            }
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695040);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Periodic value noise (tileable with the given period in cells).</summary>
        private static float PNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int xa = ((x0 % period) + period) % period, xb = (xa + 1) % period;
            int ya = ((y0 % period) + period) % period, yb = (ya + 1) % period;
            float a = Hash(xa, ya, seed), b = Hash(xb, ya, seed), c = Hash(xa, yb, seed), d = Hash(xb, yb, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        public static float Fbm(float u, float v, int basePeriod, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int period = basePeriod;
            for (int o = 0; o < octaves; o++)
            {
                sum += PNoise(u * period, v * period, period, seed + o * 17) * amp;
                norm += amp;
                amp *= 0.5f;
                period *= 2;
            }
            return sum / norm;
        }

        public enum Surface { Snow, Mud, FrozenDirt, Rock, Swamp, Road, Concrete, Sand }

        public static Texture2D TerrainTexture(Surface kind, int size = 256)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "RTV_" + kind, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new Color[size * size];
            int seed = (int)kind * 101 + 7;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;
                    float n = Fbm(u, v, 4, 5, seed);
                    float d = Fbm(u, v, 16, 3, seed + 3);
                    float g = Hash(x, y, seed + 9);
                    Color c;
                    switch (kind)
                    {
                        case Surface.Snow:
                            c = Color.Lerp(new Color(0.78f, 0.82f, 0.88f), new Color(0.95f, 0.96f, 0.99f), n);
                            c *= 0.96f + d * 0.06f;
                            if (g > 0.996f) c = new Color(1f, 1f, 1f);
                            break;
                        case Surface.Mud:
                        {
                            c = Color.Lerp(new Color(0.18f, 0.14f, 0.1f), new Color(0.34f, 0.27f, 0.19f), n);
                            float puddle = Mathf.SmoothStep(0.62f, 0.7f, Fbm(u, v, 3, 3, seed + 21));
                            c = Color.Lerp(c, new Color(0.12f, 0.12f, 0.12f), puddle * 0.7f);
                            float rut = Mathf.Abs(Mathf.Sin((u * 6f + d * 0.6f) * Mathf.PI * 2f));
                            c *= 0.88f + rut * 0.12f + (g - 0.5f) * 0.08f;
                            break;
                        }
                        case Surface.FrozenDirt:
                            c = Color.Lerp(new Color(0.27f, 0.23f, 0.19f), new Color(0.43f, 0.39f, 0.34f), n);
                            if (d > 0.62f) c = Color.Lerp(c, new Color(0.78f, 0.8f, 0.84f), (d - 0.62f) * 2.2f);
                            c *= 0.93f + (g - 0.5f) * 0.14f;
                            break;
                        case Surface.Rock:
                        {
                            c = Color.Lerp(new Color(0.3f, 0.3f, 0.31f), new Color(0.52f, 0.51f, 0.5f), n);
                            float crack = Mathf.SmoothStep(0.48f, 0.5f, Mathf.Abs(Fbm(u, v, 6, 4, seed + 5) - 0.5f) + 0.48f);
                            c *= 0.75f + crack * 0.25f;
                            break;
                        }
                        case Surface.Swamp:
                            c = Color.Lerp(new Color(0.12f, 0.14f, 0.08f), new Color(0.27f, 0.27f, 0.15f), n);
                            c = Color.Lerp(c, new Color(0.09f, 0.11f, 0.1f), Mathf.SmoothStep(0.55f, 0.7f, d));
                            break;
                        case Surface.Road:
                        {
                            c = Color.Lerp(new Color(0.29f, 0.26f, 0.22f), new Color(0.42f, 0.39f, 0.34f), n);
                            if (g > 0.85f) c *= 1.15f;
                            float track = Mathf.Abs(u - 0.3f) < 0.05f || Mathf.Abs(u - 0.7f) < 0.05f ? 0.85f : 1f;
                            c *= track;
                            break;
                        }
                        case Surface.Concrete:
                        {
                            c = Color.Lerp(new Color(0.4f, 0.4f, 0.39f), new Color(0.56f, 0.56f, 0.55f), n);
                            bool seam = (x % (size / 4)) < 2 || (y % (size / 4)) < 2;
                            if (seam) c *= 0.7f;
                            c *= 0.94f + (g - 0.5f) * 0.1f;
                            break;
                        }
                        default:
                            c = Color.Lerp(new Color(0.52f, 0.47f, 0.37f), new Color(0.68f, 0.63f, 0.5f), n);
                            break;
                    }
                    c.a = 1f;
                    px[y * size + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply(true, false);
            return tex;
        }
    }
}
