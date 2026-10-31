using System.Collections;
using System.Collections.Generic;
using RoadToValor.Core;
using UnityEngine;

namespace RoadToValor.Game
{
    /// <summary>
    /// Builds the 3D battlefield from MapData: Unity Terrain (heights + 8 procedural texture layers),
    /// water, ~2300 props merged into chunk meshes, bridges, zone borders/flags and the 48 build pads.
    /// If the scene already contains a Terrain named "BattlefieldTerrain" it is kept instead of the generated one.
    /// </summary>
    public sealed class WorldView
    {
        public Transform Root;
        public Terrain Terrain;
        public Texture2D MinimapTexture;
        public MapData Map;
        public string Status = "";

        private readonly List<ZoneVisual> _zones = new List<ZoneVisual>();
        private readonly List<PadVisual> _pads = new List<PadVisual>();

        private sealed class ZoneVisual
        {
            public ZoneDef Def;
            public Material BorderMat;
            public Material FlagMat;
            public Transform Flag;
            public Color Current;
        }

        private sealed class PadVisual
        {
            public Vec2 Pos;
            public Material FrameMat;
            public GameObject Rubble;
            public GameObject Highlight;
            public Material HighlightMat;
        }

        public static readonly Color[] LayerMinimapColors =
        {
            new Color(0.84f, 0.86f, 0.9f), new Color(0.3f, 0.24f, 0.18f), new Color(0.38f, 0.34f, 0.3f), new Color(0.42f, 0.42f, 0.42f),
            new Color(0.2f, 0.22f, 0.14f), new Color(0.55f, 0.5f, 0.4f), new Color(0.56f, 0.56f, 0.56f), new Color(0.62f, 0.57f, 0.45f),
        };

        public IEnumerator Build(MapData map, Transform parent)
        {
            Map = map;
            Root = new GameObject("Battlefield").transform;
            Root.SetParent(parent, false);

            Status = "지형 생성 중...";
            yield return null;
            float[,,] splat = map.ComputeSplat(512);
            BuildMinimap(map, splat);
            yield return null;

            var existing = GameObject.Find("BattlefieldTerrain");
            if (existing != null && existing.GetComponent<Terrain>() != null)
            {
                Terrain = existing.GetComponent<Terrain>();
                Debug.Log("[RTV] Using scene terrain 'BattlefieldTerrain' (navigation still uses the generated layout).");
            }
            else
            {
                Status = "지형 텍스처 생성 중...";
                yield return null;
                BuildTerrain(map, splat);
            }
            yield return null;

            Status = "강·해안 생성 중...";
            BuildWater(map);
            yield return null;

            Status = "전장 오브젝트 배치 중... (" + map.Props.Count + ")";
            yield return null;
            BuildProps(map);
            yield return null;

            Status = "교량·점령 구역 생성 중...";
            BuildBridges(map);
            BuildZones(map);
            yield return null;
            Status = "완료";
        }

        // ------------------------------------------------------------------ terrain

        private void BuildTerrain(MapData map, float[,,] splat)
        {
            var cfg = map.Config;
            int res = map.HeightRes;
            var td = new TerrainData();
            td.heightmapResolution = res;
            td.size = new Vector3(map.Size, cfg.TerrainMaxHeight, map.Size);
            var h = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    h[z, x] = Mathf.Clamp01((map.Heights[z * res + x] - cfg.TerrainBottom) / cfg.TerrainMaxHeight);
            td.SetHeights(0, 0, h);

            var kinds = new[] { TextureGen.Surface.Snow, TextureGen.Surface.Mud, TextureGen.Surface.FrozenDirt, TextureGen.Surface.Rock, TextureGen.Surface.Swamp, TextureGen.Surface.Road, TextureGen.Surface.Concrete, TextureGen.Surface.Sand };
            float[] tile = { 14f, 9f, 11f, 12f, 10f, 7f, 8f, 12f };
            float[] smooth = { 0.25f, 0.45f, 0.1f, 0.05f, 0.5f, 0.05f, 0.08f, 0.05f };
            var layers = new TerrainLayer[kinds.Length];
            for (int i = 0; i < kinds.Length; i++)
            {
                layers[i] = new TerrainLayer
                {
                    diffuseTexture = TextureGen.TerrainTexture(kinds[i]),
                    tileSize = new Vector2(tile[i], tile[i]),
                    smoothness = smooth[i],
                    metallic = 0f,
                };
                layers[i].name = "RTV_" + kinds[i];
            }
            td.alphamapResolution = splat.GetLength(0);
            td.terrainLayers = layers;
            td.SetAlphamaps(0, 0, splat);

            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "BattlefieldTerrain_Generated";
            go.transform.SetParent(Root, false);
            go.transform.position = new Vector3(-map.Half, cfg.TerrainBottom, -map.Half);
            Terrain = go.GetComponent<Terrain>();
            var mat = MaterialLibrary.CreateTerrainMaterial();
            if (mat != null) Terrain.materialTemplate = mat;
            Terrain.heightmapPixelError = 3f;
            Terrain.basemapDistance = 450f;
            Terrain.drawInstanced = true;
            Terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void BuildMinimap(MapData map, float[,,] splat)
        {
            const int size = 256;
            int sres = splat.GetLength(0);
            int layers = splat.GetLength(2);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "RTV_Minimap", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[size * size];
            float cell = map.Size / size;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int sx = x * sres / size, sy = y * sres / size;
                    Color c = Color.black;
                    for (int l = 0; l < layers; l++) c += LayerMinimapColors[l] * splat[sy, sx, l];
                    float wx = -map.Half + (x + 0.5f) * cell, wz = -map.Half + (y + 0.5f) * cell;
                    float hgt = map.SampleHeight(wx, wz);
                    float hx = map.SampleHeight(wx + cell, wz) - map.SampleHeight(wx - cell, wz);
                    float hz = map.SampleHeight(wx, wz + cell) - map.SampleHeight(wx, wz - cell);
                    float shade = Mathf.Clamp(1f + (-hx * 0.6f + hz * 0.6f) / (cell * 2f), 0.65f, 1.3f);
                    c *= shade;
                    if (hgt < map.WaterLevel) c = Color.Lerp(new Color(0.16f, 0.26f, 0.34f), new Color(0.08f, 0.14f, 0.2f), Mathf.Clamp01((map.WaterLevel - hgt) / 6f));
                    if (!map.InPlayableArea(new Vec2(wx, wz)) && hgt >= map.WaterLevel) c *= 0.6f;
                    foreach (var b in map.Layout.Bridges) if (b.Contains(new Vec2(wx, wz))) c = new Color(0.6f, 0.55f, 0.45f);
                    c.a = 1f;
                    px[y * size + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, false);
            MinimapTexture = tex;
        }

        private void BuildWater(MapData map)
        {
            var go = new GameObject("Water");
            go.transform.SetParent(Root, false);
            go.transform.position = new Vector3(0f, map.WaterLevel, 0f);
            var mb = new MeshBuilder();
            mb.Plane(Vector3.zero, new Vector2(map.Size * 2.2f, map.Size * 2.2f), Palette.White);
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("RTV_WaterPlane");
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MaterialLibrary.CreateWater();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ props

        private void BuildProps(MapData map)
        {
            const int chunks = 8;
            float chunkSize = map.Size / chunks;
            var builders = new MeshBuilder[chunks * chunks];
            foreach (var p in map.Props)
            {
                int cx = Mathf.Clamp((int)((p.Pos.X + map.Half) / chunkSize), 0, chunks - 1);
                int cz = Mathf.Clamp((int)((p.Pos.Z + map.Half) / chunkSize), 0, chunks - 1);
                int idx = cz * chunks + cx;
                if (builders[idx] == null) builders[idx] = new MeshBuilder();
                var src = PropMeshes.Get(p.Kind, p.Variant, p.Snowy);
                float y = p.Kind == PropKind.Ship ? map.WaterLevel - 1.5f : map.SampleHeight(p.Pos) - 0.05f;
                var m = Matrix4x4.TRS(new Vector3(p.Pos.X, y, p.Pos.Z), Quaternion.Euler(p.Tilt * Mathf.Rad2Deg, p.Yaw * Mathf.Rad2Deg, p.Tilt * 20f), Vector3.one * p.Scale);
                builders[idx].Append(src, m);
            }
            // Sandbag parapets along trench lines.
            var sandbags = PropMeshes.Get(PropKind.Sandbags, 0, false);
            foreach (var line in map.TrenchLines)
            {
                for (int i = 0; i < line.Count - 1; i++)
                {
                    Vec2 a = line[i], bpt = line[i + 1];
                    Vec2 dir = (bpt - a).Normalized;
                    float len = Vec2.Distance(a, bpt);
                    for (float t = 2f; t < len; t += 4.5f)
                    {
                        Vec2 p = a + dir * t + dir.PerpRight * (a.Z > 0 ? -2.2f : 2.2f);
                        int cx = Mathf.Clamp((int)((p.X + map.Half) / chunkSize), 0, chunks - 1);
                        int cz = Mathf.Clamp((int)((p.Z + map.Half) / chunkSize), 0, chunks - 1);
                        int idx = cz * chunks + cx;
                        if (builders[idx] == null) builders[idx] = new MeshBuilder();
                        builders[idx].Append(sandbags, Matrix4x4.TRS(new Vector3(p.X, map.SampleHeight(p), p.Z), Quaternion.Euler(0, dir.ToYaw() * Mathf.Rad2Deg + 90f, 0), Vector3.one));
                    }
                }
            }
            for (int i = 0; i < builders.Length; i++)
            {
                if (builders[i] == null || builders[i].VertexCount == 0) continue;
                var go = new GameObject("PropChunk_" + i);
                go.transform.SetParent(Root, false);
                go.AddComponent<MeshFilter>().sharedMesh = builders[i].ToMesh("RTV_PropChunk_" + i);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = MaterialLibrary.PaletteMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                go.isStatic = true;
            }
        }

        private void BuildBridges(MapData map)
        {
            float deck = map.BridgeDeckHeight;
            float bed = map.WaterLevel - 2.6f;
            foreach (var b in map.Layout.Bridges)
            {
                var mb = PropMeshes.Bridge(b.Length, b.Width, deck, bed, b.Width > 12f);
                var go = new GameObject("Bridge");
                go.transform.SetParent(Root, false);
                go.transform.position = new Vector3(b.Center.X, 0f, b.Center.Z);
                go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("RTV_Bridge");
                go.AddComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.PaletteMaterial;
            }
        }

        // ------------------------------------------------------------------ zones & pads

        private void BuildZones(MapData map)
        {
            foreach (var z in map.Layout.Zones)
            {
                var zv = new ZoneVisual { Def = z };
                // Border ribbon following the terrain, slightly hexagonal like the concept map.
                var go = new GameObject("ZoneBorder_" + z.Id);
                go.transform.SetParent(Root, false);
                var verts = new List<Vector3>();
                var tris = new List<int>();
                var uvs = new List<Vector2>();
                int seg = 72;
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    float r = z.Radius * (1f + 0.045f * Mathf.Cos(a * 6f));
                    Vector3 dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                    Vector3 outer = new Vector3(z.Center.X, 0, z.Center.Z) + dir * r;
                    Vector3 inner = new Vector3(z.Center.X, 0, z.Center.Z) + dir * (r - 1.6f);
                    outer.y = Mathf.Max(map.SampleHeight(outer.x, outer.z), map.WaterLevel) + 0.45f;
                    inner.y = Mathf.Max(map.SampleHeight(inner.x, inner.z), map.WaterLevel) + 0.45f;
                    verts.Add(inner); verts.Add(outer);
                    uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0));
                    if (i < seg)
                    {
                        int k = i * 2;
                        tris.Add(k); tris.Add(k + 1); tris.Add(k + 3);
                        tris.Add(k); tris.Add(k + 3); tris.Add(k + 2);
                    }
                }
                var mesh = new Mesh { name = "RTV_ZoneBorder_" + z.Id };
                mesh.SetVertices(verts);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                zv.BorderMat = MaterialLibrary.CreateUnlit(Conv.TeamColor(z.InitialOwner));
                mr.sharedMaterial = zv.BorderMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                // Capture flag at the zone centre.
                var flagRoot = new GameObject("ZoneFlag_" + z.Id).transform;
                flagRoot.SetParent(Root, false);
                Vec2 fp = z.Center + new Vec2(0f, 6f);
                flagRoot.position = new Vector3(fp.X, map.SampleHeight(fp), fp.Z);
                var pole = new GameObject("Pole");
                pole.transform.SetParent(flagRoot, false);
                pole.AddComponent<MeshFilter>().sharedMesh = UnitModelFactory.GetMesh("zone_pole", b =>
                {
                    b.Box(new Vector3(0, 0.3f, 0), new Vector3(1.6f, 0.6f, 1.6f), Palette.ConcreteDark);
                    b.Box(new Vector3(0, 6f, 0), new Vector3(0.18f, 12f, 0.18f), Palette.Gunmetal);
                    b.Sphere(new Vector3(0, 12.1f, 0), new Vector3(0.35f, 0.35f, 0.35f), Palette.Gold);
                });
                pole.AddComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.PaletteMaterial;
                var flag = new GameObject("Flag");
                flag.transform.SetParent(flagRoot, false);
                flag.transform.localPosition = new Vector3(0f, 11f, 0f);
                flag.AddComponent<MeshFilter>().sharedMesh = UnitModelFactory.GetMesh("zone_flag", b => b.Plane(new Vector3(1.4f, 0, 0), new Vector2(2.6f, 1.7f), Palette.White, new Vector3(0, 0, 90)));
                var fmr = flag.AddComponent<MeshRenderer>();
                zv.FlagMat = MaterialLibrary.CreateLit(Conv.TeamColor(z.InitialOwner));
                fmr.sharedMaterial = zv.FlagMat;
                zv.Flag = flag.transform;
                zv.Current = Conv.TeamColor(z.InitialOwner);
                _zones.Add(zv);

                foreach (var pad in z.PadPositions) BuildPad(map, pad);
            }
        }

        private void BuildPad(MapData map, Vec2 pos)
        {
            var pv = new PadVisual { Pos = pos };
            float y = map.SampleHeight(pos);
            var root = new GameObject("BuildPad");
            root.transform.SetParent(Root, false);
            root.transform.position = new Vector3(pos.X, y, pos.Z);
            var slab = new GameObject("Slab");
            slab.transform.SetParent(root.transform, false);
            slab.AddComponent<MeshFilter>().sharedMesh = UnitModelFactory.GetMesh("pad_slab", b =>
            {
                b.Box(new Vector3(0, 0.12f, 0), new Vector3(11f, 0.5f, 11f), Palette.Concrete);
                for (int i = 0; i < 4; i++)
                {
                    float a = i * 90f;
                    var d = Quaternion.Euler(0, a, 0) * new Vector3(4.9f, 0f, 4.9f);
                    b.Box(d + Vector3.up * 0.45f, new Vector3(0.5f, 0.3f, 0.5f), Palette.Hazard);
                }
            });
            slab.AddComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.PaletteMaterial;

            var frame = new GameObject("Frame");
            frame.transform.SetParent(root.transform, false);
            frame.transform.localPosition = new Vector3(0f, 0.42f, 0f);
            frame.AddComponent<MeshFilter>().sharedMesh = UnitModelFactory.GetMesh("pad_frame", b =>
            {
                b.Box(new Vector3(0, 0, 5.3f), new Vector3(10.6f, 0.06f, 0.35f), Palette.White);
                b.Box(new Vector3(0, 0, -5.3f), new Vector3(10.6f, 0.06f, 0.35f), Palette.White);
                b.Box(new Vector3(5.3f, 0, 0), new Vector3(0.35f, 0.06f, 10.6f), Palette.White);
                b.Box(new Vector3(-5.3f, 0, 0), new Vector3(0.35f, 0.06f, 10.6f), Palette.White);
            });
            var fmr = frame.AddComponent<MeshRenderer>();
            pv.FrameMat = MaterialLibrary.CreateUnlit(new Color(0.8f, 0.8f, 0.8f, 0.8f));
            fmr.sharedMaterial = pv.FrameMat;
            fmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            pv.Rubble = new GameObject("Rubble");
            pv.Rubble.transform.SetParent(root.transform, false);
            pv.Rubble.AddComponent<MeshFilter>().sharedMesh = UnitModelFactory.GetMesh("pad_rubble", b =>
            {
                for (int i = 0; i < 14; i++)
                    b.Box(new Vector3(((i * 37) % 9 - 4) * 0.9f, 0.5f + (i % 3) * 0.35f, ((i * 53) % 9 - 4) * 0.9f), new Vector3(1.6f, 0.9f, 1.2f), i % 3 == 0 ? Palette.Burnt : (i % 3 == 1 ? Palette.ConcreteDark : Palette.Rust), new Vector3(i * 19, i * 47, i * 13));
                b.Tube(new Vector3(1f, 1.4f, 0.5f), 0.1f, 3f, Palette.Gunmetal, 6, new Vector3(-20, 40, 0));
            });
            pv.Rubble.AddComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.PaletteMaterial;
            pv.Rubble.SetActive(false);

            pv.Highlight = new GameObject("Highlight");
            pv.Highlight.transform.SetParent(root.transform, false);
            pv.Highlight.transform.localPosition = new Vector3(0, 0.55f, 0);
            pv.Highlight.AddComponent<MeshFilter>().sharedMesh = UnitModelFactory.GetMesh("pad_highlight", b => b.Ring(Vector3.zero, 7f, 8.2f, Palette.White, 4));
            var hmr = pv.Highlight.AddComponent<MeshRenderer>();
            pv.HighlightMat = MaterialLibrary.CreateUnlit(Color.green);
            hmr.sharedMaterial = pv.HighlightMat;
            hmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pv.Highlight.transform.localRotation = Quaternion.Euler(0, 45f, 0);
            pv.Highlight.SetActive(false);
            _pads.Add(pv);
        }

        /// <summary>Per-frame visual sync: zone colours (pulsing when contested), flags, pad states.</summary>
        public void UpdateVisuals(BattleSimulation sim, float time, bool placingStructure, Team viewer, int placingDefId)
        {
            for (int i = 0; i < _zones.Count && sim != null && i < sim.Zones.Length; i++)
            {
                var zs = sim.Zones[i];
                var zv = _zones[i];
                Color target = Conv.TeamColor(zs.Owner);
                if (zs.Owner == Team.Neutral && zs.Progress > 0.01f) target = Color.Lerp(target, Conv.TeamColor(zs.ControlSide), zs.Progress * 0.6f);
                if (zs.Contested) target = Color.Lerp(target, new Color(1f, 0.85f, 0.2f), 0.5f + 0.5f * Mathf.Sin(time * 8f));
                zv.Current = Color.Lerp(zv.Current, target, 0.15f);
                zv.BorderMat.color = zv.Current.WithAlpha(0.85f);
                MaterialLibrary.SetColor(zv.BorderMat, zv.Current.WithAlpha(0.85f));
                MaterialLibrary.SetColor(zv.FlagMat, Conv.TeamColor(zs.Owner));
                // Flag height shows control progress; flag waves.
                float h = 3f + 8f * (zs.Owner != Team.Neutral ? 1f : zs.Progress);
                zv.Flag.localPosition = new Vector3(0f, h, 0f);
                zv.Flag.localRotation = Quaternion.Euler(0f, Mathf.Sin(time * 2.3f + i) * 12f, 0f);
            }

            for (int i = 0; i < _pads.Count; i++)
            {
                var pv = _pads[i];
                if (sim == null || i >= sim.Pads.Length) { pv.Rubble.SetActive(false); pv.Highlight.SetActive(false); continue; }
                var pad = sim.Pads[i];
                pv.Rubble.SetActive(pad.State == PadState.Rubble);
                var owner = sim.Zones[pad.ZoneIndex].Owner;
                Color frame = owner == Team.Neutral ? new Color(0.75f, 0.75f, 0.75f, 0.7f) : Conv.TeamColor(owner).WithAlpha(0.85f);
                if (pad.State == PadState.Rubble) frame = new Color(0.9f, 0.55f, 0.1f, 0.9f);
                MaterialLibrary.SetColor(pv.FrameMat, frame);
                pv.FrameMat.color = frame;
                if (placingStructure)
                {
                    bool ok = sim.CanDeploy(viewer, placingDefId, pad.Pos, pad.Id, out _);
                    pv.Highlight.SetActive(true);
                    Color hc = ok ? new Color(0.3f, 1f, 0.4f, 0.75f + 0.25f * Mathf.Sin(time * 6f)) : new Color(1f, 0.25f, 0.2f, 0.45f);
                    pv.HighlightMat.color = hc;
                    MaterialLibrary.SetColor(pv.HighlightMat, hc);
                }
                else pv.Highlight.SetActive(false);
            }
        }
    }
}
