using RoadToValor.Core;
using UnityEngine;

namespace RoadToValor.Game
{
    /// <summary>
    /// Quarter-view RTS camera: WASD / arrow keys / screen-edge scrolling, mouse-wheel zoom (pitch eases with zoom),
    /// Space to jump to the selection, minimap jumps, and clamped bounds so the 1200m field must be explored.
    /// </summary>
    public sealed class RtsCamera : MonoBehaviour
    {
        public Camera Cam;
        public MapData Map;
        public Vector3 Focus;            // ground point the camera looks at
        public float Zoom = 0.45f;       // 0 = closest, 1 = farthest
        public float MinDistance = 32f;
        public float MaxDistance = 330f;
        public float MinPitch = 40f;
        public float MaxPitch = 63f;
        public float Yaw;                // fixed north-up like a MOBA (minimap stays aligned)
        public float EdgePixels = 10f;
        public bool EdgeScroll = true;
        public bool InputEnabled = true;
        public bool Orbit;               // lobby preview mode
        public float BoundsX = 580f;
        public float BoundsZMin = -600f;
        public float BoundsZMax = 560f;
        public System.Func<GameKey, bool> KeyFilter; // lets selection logic reserve A/S taps

        private Vector3 _focusTarget;
        private float _zoomTarget;
        private float _orbitAngle;

        public void Init(Camera cam, MapData map)
        {
            Cam = cam;
            Map = map;
            _zoomTarget = Zoom;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 2600f;
            cam.fieldOfView = 42f;
        }

        public void SetFocus(Vector3 p, bool instant = false)
        {
            p.x = Mathf.Clamp(p.x, -BoundsX, BoundsX);
            p.z = Mathf.Clamp(p.z, BoundsZMin, BoundsZMax);
            _focusTarget = p;
            if (instant) Focus = p;
        }

        public void SetZoom(float z, bool instant = false)
        {
            _zoomTarget = Mathf.Clamp01(z);
            if (instant) Zoom = _zoomTarget;
        }

        private bool Key(GameKey k) => GameInput.Held(k) && (KeyFilter == null || KeyFilter(k));

        private void LateUpdate()
        {
            if (Cam == null || Map == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            if (Orbit)
            {
                _orbitAngle += dt * 4f;
                Yaw = _orbitAngle;
            }
            else if (InputEnabled)
            {
                Vector2 move = Vector2.zero;
                if (Key(GameKey.W) || GameInput.Held(GameKey.Up)) move.y += 1f;
                if (Key(GameKey.S) || GameInput.Held(GameKey.Down)) move.y -= 1f;
                if (Key(GameKey.A) || GameInput.Held(GameKey.Left)) move.x -= 1f;
                if (Key(GameKey.D) || GameInput.Held(GameKey.Right)) move.x += 1f;
                if (EdgeScroll && Application.isFocused)
                {
                    Vector2 m = GameInput.MousePosition;
                    if (m.x >= 0 && m.y >= 0 && m.x <= Screen.width && m.y <= Screen.height)
                    {
                        if (m.x < EdgePixels) move.x -= 1f;
                        if (m.x > Screen.width - EdgePixels) move.x += 1f;
                        if (m.y < EdgePixels) move.y -= 1f;
                        if (m.y > Screen.height - EdgePixels) move.y += 1f;
                    }
                }
                if (move.sqrMagnitude > 1f) move.Normalize();
                float dist = Mathf.Lerp(MinDistance, MaxDistance, Zoom);
                float speed = Mathf.Lerp(45f, 330f, Zoom) * (GameInput.Shift ? 2f : 1f);
                Quaternion yawRot = Quaternion.Euler(0f, Yaw, 0f);
                Vector3 delta = yawRot * new Vector3(move.x, 0f, move.y) * speed * dt;
                SetFocus(_focusTarget + delta);

                float scroll = GameInput.Scroll;
                if (Mathf.Abs(scroll) > 0.01f) _zoomTarget = Mathf.Clamp01(_zoomTarget - scroll * 0.08f);
                _ = dist;
            }

            Focus = Vector3.Lerp(Focus, _focusTarget, 1f - Mathf.Exp(-dt * 12f));
            Zoom = Mathf.Lerp(Zoom, _zoomTarget, 1f - Mathf.Exp(-dt * 10f));
            float ground = Mathf.Max(Map.SampleHeight(Focus.x, Focus.z), Map.WaterLevel);
            Focus.y = Mathf.Lerp(Focus.y, ground, 1f - Mathf.Exp(-dt * 6f));

            float d = Mathf.Lerp(MinDistance, MaxDistance, Zoom);
            float pitch = Mathf.Lerp(MinPitch, MaxPitch, Zoom);
            Quaternion rot = Quaternion.Euler(pitch, Yaw, 0f);
            Vector3 pos = Focus - rot * Vector3.forward * d;
            // keep above terrain
            float camGround = Mathf.Max(Map.SampleHeight(pos.x, pos.z), Map.WaterLevel);
            if (pos.y < camGround + 6f) pos.y = camGround + 6f;
            Cam.transform.SetPositionAndRotation(pos, rot);
        }

        /// <summary>Ray-marches the heightfield (no colliders needed). Returns false if the ray misses the map.</summary>
        public bool ScreenToGround(Vector2 screen, out Vector3 hit)
        {
            hit = Vector3.zero;
            if (Cam == null || Map == null) return false;
            Ray ray = Cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
            return RaycastGround(ray, Map, out hit);
        }

        public static bool RaycastGround(Ray ray, MapData map, out Vector3 hit)
        {
            hit = Vector3.zero;
            float t = 0f, step = 2f;
            Vector3 prev = ray.origin;
            float prevDiff = prev.y - Surface(map, prev);
            for (int i = 0; i < 2000; i++)
            {
                t += step;
                Vector3 p = ray.origin + ray.direction * t;
                if (Mathf.Abs(p.x) > map.Half + 200f || Mathf.Abs(p.z) > map.Half + 200f || p.y < map.Config.TerrainBottom - 5f) break;
                float diff = p.y - Surface(map, p);
                if (diff <= 0f)
                {
                    // bisection refine
                    float a = t - step, b = t;
                    for (int k = 0; k < 10; k++)
                    {
                        float m = (a + b) * 0.5f;
                        Vector3 mp = ray.origin + ray.direction * m;
                        if (mp.y - Surface(map, mp) > 0f) a = m; else b = m;
                    }
                    hit = ray.origin + ray.direction * b;
                    return true;
                }
                prevDiff = diff;
                step = Mathf.Clamp(diff * 0.5f, 0.5f, 12f);
            }
            _ = prevDiff;
            return false;
        }

        private static float Surface(MapData map, Vector3 p)
        {
            var v = new Vec2(p.x, p.z);
            return Mathf.Max(map.GroundHeight(v), map.WaterLevel);
        }

        /// <summary>World-space corners of the camera view on the ground (for the minimap frustum).</summary>
        public bool GroundCorners(Vector3[] corners)
        {
            if (Cam == null) return false;
            Vector2[] screen = { new Vector2(0, 0), new Vector2(Screen.width, 0), new Vector2(Screen.width, Screen.height), new Vector2(0, Screen.height) };
            for (int i = 0; i < 4; i++)
            {
                Ray r = Cam.ScreenPointToRay(new Vector3(screen[i].x, screen[i].y, 0));
                // intersect with plane at focus height (cheap and stable)
                float denom = r.direction.y;
                if (Mathf.Abs(denom) < 1e-4f) return false;
                float t = (Focus.y - r.origin.y) / denom;
                if (t < 0f) t = 3000f;
                corners[i] = r.origin + r.direction * Mathf.Min(t, 3000f);
            }
            return true;
        }
    }
}
