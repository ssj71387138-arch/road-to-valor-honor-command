using System;

namespace RoadToValor.Core
{
    /// <summary>Deterministic xorshift RNG. Same seed => same map and same simulation (lockstep friendly).</summary>
    public sealed class Rng
    {
        private ulong _s;

        public Rng(int seed)
        {
            _s = unchecked((ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL);
            if (_s == 0) _s = 0x2545F4914F6CDD1DUL;
            for (int i = 0; i < 4; i++) NextUInt();
        }

        public uint NextUInt()
        {
            unchecked
            {
                _s ^= _s << 13;
                _s ^= _s >> 7;
                _s ^= _s << 17;
                return (uint)(_s >> 32);
            }
        }

        /// <summary>[0, 1)</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        public float Range(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>[min, maxExclusive)</summary>
        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min) return min;
            return min + (int)(NextUInt() % (uint)(maxExclusive - min));
        }

        public bool Chance(float p) => NextFloat() < p;

        public Vec2 InsideCircle(float radius)
        {
            float a = NextFloat() * MathUtil.TwoPi;
            float r = MathF.Sqrt(NextFloat()) * radius;
            return new Vec2(MathF.Sin(a) * r, MathF.Cos(a) * r);
        }
    }

    /// <summary>Deterministic value noise / fbm used by the map generator.</summary>
    public static class Noise
    {
        private static float Hash(int x, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + z * 668265263 + seed * 1442695040);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        public static float Value(float x, float z, int seed)
        {
            int x0 = (int)MathF.Floor(x), z0 = (int)MathF.Floor(z);
            float fx = x - x0, fz = z - z0;
            float sx = fx * fx * (3f - 2f * fx);
            float sz = fz * fz * (3f - 2f * fz);
            float a = Hash(x0, z0, seed), b = Hash(x0 + 1, z0, seed);
            float c = Hash(x0, z0 + 1, seed), d = Hash(x0 + 1, z0 + 1, seed);
            float ab = a + (b - a) * sx;
            float cd = c + (d - c) * sx;
            return ab + (cd - ab) * sz;
        }

        /// <summary>Fractal noise in [0,1].</summary>
        public static float Fbm(float x, float z, int seed, int octaves)
        {
            float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value(x * freq, z * freq, seed + i * 31) * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2.03f;
            }
            return sum / norm;
        }
    }
}
