using RoadToValor.Core;
using UnityEngine;

namespace RoadToValor.Game
{
    /// <summary>Conversions between the engine-independent simulation types and Unity types.</summary>
    public static class Conv
    {
        public static Vector3 ToV3(this Vec2 v, float y = 0f) => new Vector3(v.X, y, v.Z);
        public static Vec2 ToVec2(this Vector3 v) => new Vec2(v.x, v.z);
        public static float YawDeg(float yawRad) => yawRad * Mathf.Rad2Deg;

        public static Color TeamColor(Team t)
        {
            switch (t)
            {
                case Team.Player: return new Color(0.22f, 0.55f, 1f);
                case Team.Enemy: return new Color(0.95f, 0.22f, 0.18f);
                default: return new Color(0.72f, 0.72f, 0.7f);
            }
        }

        public static Color WithAlpha(this Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
