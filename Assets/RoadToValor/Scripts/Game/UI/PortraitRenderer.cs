using System.Collections.Generic;
using RoadToValor.Core;
using UnityEngine;

namespace RoadToValor.Game
{
    /// <summary>
    /// Renders unit card portraits from the actual 3D models (placeholder or real) into textures, one per frame pair,
    /// plus a live rotating preview for the unit codex. Uses an off-screen camera on an isolated layer far from the map.
    /// Works with URP and Built-in because the camera simply renders into a RenderTexture during the normal frame.
    /// </summary>
    public sealed class PortraitRenderer : MonoBehaviour
    {
        public const int Layer = 31;
        private static readonly Vector3 Stage = new Vector3(6000f, 0f, 6000f);
        private static readonly Vector3 LiveStage = new Vector3(6200f, 0f, 6000f);

        private UnitDatabase _units;
        private Camera _cam, _liveCam;
        private RenderTexture _rt;
        public RenderTexture LiveTexture;
        private readonly Dictionary<int, Texture2D> _cache = new Dictionary<int, Texture2D>();
        private readonly Queue<int> _queue = new Queue<int>();
        private readonly HashSet<int> _queued = new HashSet<int>();
        private int _current = -1;
        private GameObject _model, _liveModel;
        private int _frames;
        private int _liveDef = -1;
        private Light _light;

        public void Init(UnitDatabase units)
        {
            _units = units;
            _rt = new RenderTexture(160, 160, 24, RenderTextureFormat.ARGB32) { name = "RTV_PortraitRT" };
            _rt.Create();
            LiveTexture = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32) { name = "RTV_LiveRT" };
            LiveTexture.Create();
            _cam = MakeCam("PortraitCam", _rt);
            _liveCam = MakeCam("LivePreviewCam", LiveTexture);
            var lgo = new GameObject("PortraitLight");
            lgo.transform.SetParent(transform, false);
            _light = lgo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.range = 60f;
            _light.intensity = 1.2f;
            _light.cullingMask = 1 << Layer;
            lgo.transform.position = Stage + new Vector3(6f, 10f, 8f);
        }

        private Camera MakeCam(string name, RenderTexture target)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var c = go.AddComponent<Camera>();
            c.cullingMask = 1 << Layer;
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color(0.09f, 0.1f, 0.11f, 1f);
            c.fieldOfView = 24f;
            c.nearClipPlane = 0.3f;
            c.farClipPlane = 300f;
            c.targetTexture = target;
            c.depth = -50f;
            c.enabled = false;
            c.allowHDR = false;
            c.allowMSAA = false;
            return c;
        }

        public Texture2D Get(int defId)
        {
            if (_cache.TryGetValue(defId, out var t)) return t;
            if (_units != null && _units.Get(defId) != null && _queued.Add(defId)) _queue.Enqueue(defId);
            return null;
        }

        public void Prewarm(IEnumerable<int> ids) { foreach (int id in ids) Get(id); }

        private static void SetLayer(GameObject go)
        {
            go.layer = Layer;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
        }

        private static Bounds BoundsOf(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 2f);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        private GameObject Spawn(int defId, Vector3 at)
        {
            var def = _units.Get(defId);
            var go = UnitModelFactory.Build(def, Team.Player);
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(0f, 200f, 0f);
            var rig = go.GetComponent<UnitRig>();
            if (rig != null && rig.Kind == RigKind.Soldier && rig.Arms != null) rig.Arms.localRotation = Quaternion.Euler(5f, 0, 0);
            if (rig != null && rig.Extra != null && rig.Extra.name == "Parachute") rig.Extra.gameObject.SetActive(false);
            SetLayer(go);
            return go;
        }

        private static void Frame(Camera cam, GameObject model)
        {
            var b = BoundsOf(model);
            float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
            float dist = size / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * 1.15f + 0.5f;
            Vector3 dir = new Vector3(0.55f, 0.42f, -1f).normalized;
            cam.transform.position = b.center + dir * dist;
            cam.transform.LookAt(b.center);
        }

        public Texture LivePreview(int defId)
        {
            if (_liveDef != defId)
            {
                if (_liveModel != null) Destroy(_liveModel);
                _liveDef = defId;
                _liveModel = Spawn(defId, LiveStage);
                Frame(_liveCam, _liveModel);
                _liveModel.transform.rotation = Quaternion.identity;
            }
            _liveCam.enabled = true;
            return LiveTexture;
        }

        public void StopLive()
        {
            if (_liveCam != null) _liveCam.enabled = false;
            if (_liveModel != null) Destroy(_liveModel);
            _liveModel = null;
            _liveDef = -1;
        }

        private void Update()
        {
            if (_liveModel != null) _liveModel.transform.Rotate(0f, 35f * Time.unscaledDeltaTime, 0f);
            if (_light != null && _liveModel != null && _current < 0) _light.transform.position = LiveStage + new Vector3(6f, 10f, -8f);

            if (_current >= 0)
            {
                _frames++;
                if (_frames < 2) return;
                var tex = new Texture2D(_rt.width, _rt.height, TextureFormat.RGBA32, false) { name = "Portrait_" + _current };
                var prev = RenderTexture.active;
                RenderTexture.active = _rt;
                tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
                tex.Apply(false, false);
                RenderTexture.active = prev;
                _cache[_current] = tex;
                Destroy(_model);
                _model = null;
                _current = -1;
                _cam.enabled = false;
                return;
            }
            if (_queue.Count > 0)
            {
                _current = _queue.Dequeue();
                _model = Spawn(_current, Stage);
                Frame(_cam, _model);
                if (_light != null) _light.transform.position = Stage + new Vector3(6f, 10f, -8f);
                _cam.enabled = true;
                _frames = 0;
            }
        }
    }
}
