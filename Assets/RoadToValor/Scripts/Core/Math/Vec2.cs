using System;

namespace RoadToValor.Core
{
    /// <summary>
    /// 2D vector on the ground plane. X = east(+)/west(-), Z = north(+)/south(-).
    /// The simulation is 2.5D: heights are derived from MapData when needed.
    /// Engine-agnostic (no UnityEngine dependency) so the simulation can be unit tested and
    /// later run on a dedicated server for multiplayer.
    /// </summary>
    [Serializable]
    public struct Vec2 : IEquatable<Vec2>
    {
        public float X;
        public float Z;

        public Vec2(float x, float z) { X = x; Z = z; }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);

        public float Length => MathF.Sqrt(X * X + Z * Z);
        public float SqrLength => X * X + Z * Z;

        public Vec2 Normalized
        {
            get
            {
                float l = Length;
                return l > 1e-6f ? new Vec2(X / l, Z / l) : Zero;
            }
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Z + b.Z);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Z - b.Z);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Z);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Z * s);
        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.X * s, a.Z * s);
        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Z / s);
        public static bool operator ==(Vec2 a, Vec2 b) => a.X == b.X && a.Z == b.Z;
        public static bool operator !=(Vec2 a, Vec2 b) => !(a == b);

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Z * b.Z;
        public static float Cross(Vec2 a, Vec2 b) => a.X * b.Z - a.Z * b.X;
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;
        public static float DistanceSq(Vec2 a, Vec2 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return dx * dx + dz * dz;
        }
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => new Vec2(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t);

        /// <summary>Yaw angle (radians) where 0 = +Z (north), positive = clockwise (towards +X). Matches Unity's Y rotation.</summary>
        public static Vec2 FromYaw(float yaw) => new Vec2(MathF.Sin(yaw), MathF.Cos(yaw));
        public float ToYaw() => MathF.Atan2(X, Z);

        /// <summary>Rotate by a yaw angle (clockwise from north).</summary>
        public Vec2 RotateYaw(float yaw)
        {
            float s = MathF.Sin(yaw), c = MathF.Cos(yaw);
            // local (x right, z forward) -> world
            return new Vec2(X * c + Z * s, -X * s + Z * c);
        }

        public Vec2 PerpRight => new Vec2(Z, -X);

        public bool Equals(Vec2 other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is Vec2 v && Equals(v);
        public override int GetHashCode() => unchecked((X.GetHashCode() * 397) ^ Z.GetHashCode());
        public override string ToString() => "(" + X.ToString("0.0") + ", " + Z.ToString("0.0") + ")";
    }

    public static class MathUtil
    {
        public const float Pi = 3.14159265358979f;
        public const float TwoPi = Pi * 2f;
        public const float Deg2Rad = Pi / 180f;
        public const float Rad2Deg = 180f / Pi;

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float InverseLerp(float a, float b, float v) => Math.Abs(b - a) < 1e-6f ? 0f : Clamp01((v - a) / (b - a));
        public static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = InverseLerp(edge0, edge1, x);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Shortest signed difference between two yaw angles (radians), in [-pi, pi].</summary>
        public static float DeltaAngle(float from, float to)
        {
            float d = (to - from) % TwoPi;
            if (d > Pi) d -= TwoPi;
            if (d < -Pi) d += TwoPi;
            return d;
        }

        public static float MoveTowardsAngle(float current, float target, float maxDelta)
        {
            float d = DeltaAngle(current, target);
            if (Math.Abs(d) <= maxDelta) return target;
            return current + Math.Sign(d) * maxDelta;
        }

        /// <summary>Distance from p to segment ab; t = normalized position along the segment.</summary>
        public static float DistancePointSegment(Vec2 p, Vec2 a, Vec2 b, out float t)
        {
            Vec2 ab = b - a;
            float len2 = ab.SqrLength;
            if (len2 < 1e-6f) { t = 0f; return Vec2.Distance(p, a); }
            t = Clamp01(Vec2.Dot(p - a, ab) / len2);
            return Vec2.Distance(p, a + ab * t);
        }

        public static float DistanceToPolyline(Vec2 p, System.Collections.Generic.IList<Vec2> pts)
        {
            float best = float.MaxValue;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                float d = DistancePointSegment(p, pts[i], pts[i + 1], out _);
                if (d < best) best = d;
            }
            return best;
        }
    }
}
