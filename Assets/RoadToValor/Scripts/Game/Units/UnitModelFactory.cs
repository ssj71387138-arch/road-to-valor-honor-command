using System.Collections.Generic;
using RoadToValor.Core;
using UnityEngine;

namespace RoadToValor.Game
{
    /// <summary>
    /// Builds unit models. If a real prefab exists at Resources/RoadToValor/Models/U{id:00} it is used;
    /// otherwise a realistic-proportioned procedural placeholder (helmets, vests, pouches, rifles,
    /// officer coats, ghillie suits, exoskeletons, tanks with turrets, helicopters, bunkers...) is built.
    /// Team identification = blue / red helmet band + armband (soldiers), flag + stripe (vehicles, buildings).
    /// </summary>
    public static class UnitModelFactory
    {
        private static readonly Dictionary<string, Mesh> MeshCache = new Dictionary<string, Mesh>();
        private static readonly MeshBuilder B = new MeshBuilder();

        public static GameObject Build(UnitDefinition def, Team team)
        {
            var prefab = Resources.Load<GameObject>("RoadToValor/Models/U" + def.Id.ToString("00"));
            if (prefab != null) return FromPrefab(prefab, def, team);

            var go = new GameObject(def.Key + "_" + team);
            var rig = go.AddComponent<UnitRig>();
            string v = def.Visual ?? "";
            if (v.StartsWith("struct_") || v == "hq") BuildStructure(rig, v, team);
            else if (v.StartsWith("air_") || v.StartsWith("missile_")) BuildAircraft(rig, v, team);
            else if (v.StartsWith("tank_") || v.StartsWith("vehicle_") || v.StartsWith("robot_")) BuildVehicle(rig, v, team);
            else BuildSoldier(rig, v, team);
            rig.Renderers = go.GetComponentsInChildren<Renderer>(true);
            return go;
        }

        private static GameObject FromPrefab(GameObject prefab, UnitDefinition def, Team team)
        {
            var go = Object.Instantiate(prefab);
            go.name = def.Key + "_" + team;
            var rig = go.GetComponent<UnitRig>();
            if (rig == null) rig = go.AddComponent<UnitRig>();
            rig.IsPrefab = true;
            rig.Kind = def.IsStructure ? RigKind.Structure : (def.IsAir ? RigKind.Aircraft : (def.IsInfantry ? RigKind.Soldier : RigKind.Vehicle));
            rig.Body = rig.Body != null ? rig.Body : (FindDeep(go.transform, "Body") ?? go.transform);
            rig.Turret = rig.Turret != null ? rig.Turret : FindDeep(go.transform, "Turret");
            rig.Barrel = rig.Barrel != null ? rig.Barrel : FindDeep(go.transform, "Barrel");
            rig.Muzzle = rig.Muzzle != null ? rig.Muzzle : FindDeep(go.transform, "Muzzle");
            rig.Animator = go.GetComponentInChildren<Animator>();
            rig.Renderers = go.GetComponentsInChildren<Renderer>(true);
            // Team colour: any material whose name contains "TeamColor" is tinted.
            foreach (var r in rig.Renderers)
                foreach (var m in r.materials)
                    if (m.name.Contains("TeamColor")) MaterialLibrary.SetColor(m, Conv.TeamColor(team));
            return go;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }

        // ------------------------------------------------------------------ helpers

        private static Transform Node(Transform parent, string name, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        private delegate void BuildFn(MeshBuilder b);

        private static Transform Part(Transform parent, string name, Vector3 localPos, string cacheKey, BuildFn fn, bool shadows = true)
        {
            var t = Node(parent, name, localPos);
            if (!MeshCache.TryGetValue(cacheKey, out Mesh mesh))
            {
                B.Clear();
                fn(B);
                mesh = B.ToMesh(cacheKey);
                mesh.RecalculateBounds();
                MeshCache[cacheKey] = mesh;
            }
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = t.gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = MaterialLibrary.PaletteMaterial;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return t;
        }

        public static Mesh GetMesh(string cacheKey, System.Action<MeshBuilder> fn)
        {
            if (MeshCache.TryGetValue(cacheKey, out Mesh mesh)) return mesh;
            B.Clear();
            fn(B);
            mesh = B.ToMesh(cacheKey);
            MeshCache[cacheKey] = mesh;
            return mesh;
        }

        // ------------------------------------------------------------------ soldiers

        private enum Weapon { Rifle, Smg, Mg, Sniper, Bazooka, Flamer, Detector, Mortar, Gatling, Pistol, Megaphone, Controller, None, BayonetRifle }
        private enum Headgear { Helmet, SoftCap, OfficerCap, Ceremonial, Balaclava, ExoHelm, Ghillie, ParaHelmet }
        private enum Pack { None, Small, Radio, FlameTanks, MedicBag, Engineer, Parachute, Explosive, Antenna, MineBag, Mortar }

        private sealed class SoldierSpec
        {
            public Weapon Weapon = Weapon.Rifle;
            public Headgear Head = Headgear.Helmet;
            public Pack Pack = Pack.Small;
            public int Uniform = Palette.Uniform;
            public int VestColor = Palette.Vest;
            public bool Coat;
            public int CoatColor = Palette.OfficerCoat;
            public bool Ghillie;
            public bool Exo;
            public bool Shield;
            public bool MedicBand;
            public bool Medal;
            public bool WhiteBelts;
            public float Scale = 1f;
        }

        private static SoldierSpec Spec(string v)
        {
            var s = new SoldierSpec();
            switch (v)
            {
                case "soldier_assault": s.Weapon = Weapon.Smg; break;
                case "soldier_sniper": s.Weapon = Weapon.Sniper; s.Head = Headgear.SoftCap; break;
                case "soldier_mg": s.Weapon = Weapon.Mg; s.Pack = Pack.Small; break;
                case "soldier_bazooka": s.Weapon = Weapon.Bazooka; break;
                case "soldier_medic": s.Weapon = Weapon.Pistol; s.Pack = Pack.MedicBag; s.MedicBand = true; break;
                case "soldier_flamer": s.Weapon = Weapon.Flamer; s.Pack = Pack.FlameTanks; break;
                case "soldier_engineer": s.Weapon = Weapon.Rifle; s.Pack = Pack.Engineer; break;
                case "soldier_radio": s.Weapon = Weapon.Rifle; s.Pack = Pack.Radio; break;
                case "soldier_mortar": s.Weapon = Weapon.Mortar; s.Pack = Pack.Mortar; break;
                case "soldier_shield": s.Weapon = Weapon.Smg; s.Shield = true; s.VestColor = Palette.SteelDark; break;
                case "soldier_smg": s.Weapon = Weapon.Smg; break;
                case "soldier_detector": s.Weapon = Weapon.Detector; s.Pack = Pack.Small; break;
                case "soldier_grenade": s.Weapon = Weapon.Rifle; s.Pack = Pack.Small; break;
                case "soldier_scout": s.Weapon = Weapon.Rifle; s.Head = Headgear.SoftCap; s.Pack = Pack.Small; break;
                case "officer_medal": s.Weapon = Weapon.Pistol; s.Head = Headgear.OfficerCap; s.Pack = Pack.None; s.Coat = true; s.CoatColor = Palette.DressRed; s.Medal = true; s.Uniform = Palette.DressRed; break;
                case "guard_honor": s.Weapon = Weapon.BayonetRifle; s.Head = Headgear.Ceremonial; s.Pack = Pack.None; s.Uniform = Palette.DressNavy; s.VestColor = Palette.DressNavy; s.WhiteBelts = true; break;
                case "exo_heavy": s.Weapon = Weapon.Gatling; s.Head = Headgear.ExoHelm; s.Pack = Pack.None; s.Exo = true; s.Uniform = Palette.SteelDark; s.VestColor = Palette.ExoSteel; s.Scale = 1.25f; break;
                case "ghillie_sniper": s.Weapon = Weapon.Sniper; s.Head = Headgear.Ghillie; s.Pack = Pack.None; s.Ghillie = true; break;
                case "soldier_gatling": s.Weapon = Weapon.Gatling; s.VestColor = Palette.SteelDark; s.Pack = Pack.Small; break;
                case "commando_rifle": s.Weapon = Weapon.Smg; s.Head = Headgear.Balaclava; s.Uniform = Palette.CamoBrown; s.VestColor = Palette.Black; break;
                case "para_rifle": s.Weapon = Weapon.Rifle; s.Head = Headgear.ParaHelmet; s.Pack = Pack.Parachute; break;
                case "seal_rifle": s.Weapon = Weapon.Smg; s.Head = Headgear.Balaclava; s.Uniform = Palette.NavyDark; s.VestColor = Palette.Black; break;
                case "soldier_explosive": s.Weapon = Weapon.None; s.Pack = Pack.Explosive; break;
                case "officer_psy": s.Weapon = Weapon.Megaphone; s.Head = Headgear.OfficerCap; s.Coat = true; s.Pack = Pack.None; break;
                case "soldier_drone": s.Weapon = Weapon.Controller; s.Pack = Pack.Antenna; break;
                case "medic_doctor": s.Weapon = Weapon.Pistol; s.Head = Headgear.SoftCap; s.Pack = Pack.MedicBag; s.MedicBand = true; s.Coat = true; s.CoatColor = Palette.Bandage; break;
                case "soldier_mine": s.Weapon = Weapon.Rifle; s.Pack = Pack.MineBag; break;
                case "officer_field": s.Weapon = Weapon.Pistol; s.Head = Headgear.OfficerCap; s.Coat = true; s.Pack = Pack.None; s.Medal = true; break;
            }
            return s;
        }

        private static void BuildSoldier(UnitRig rig, string v, Team team)
        {
            var s = Spec(v);
            rig.Kind = RigKind.Soldier;
            rig.Height = 1.85f * s.Scale;
            string k = v + "_" + team;
            int teamCol = Palette.Team(team);
            var root = rig.transform;
            rig.Body = Node(root, "Body", Vector3.zero);
            rig.Body.localScale = Vector3.one * s.Scale;

            float legW = s.Exo ? 0.21f : 0.15f;
            BuildFn leg = b =>
            {
                b.Box(new Vector3(0f, -0.44f, 0f), new Vector3(legW, 0.86f, legW + 0.02f), s.Ghillie ? Palette.GhillieDark : s.Uniform);
                b.Box(new Vector3(0f, -0.25f, 0.03f), new Vector3(legW + 0.02f, 0.12f, 0.06f), s.Exo ? Palette.ExoSteel : s.Uniform); // knee pad / thigh pocket
                b.Box(new Vector3(0f, -0.86f, 0.05f), new Vector3(legW + 0.02f, 0.13f, 0.29f), s.Exo ? Palette.SteelDark : Palette.Boot);
                if (s.Ghillie) for (int i = 0; i < 5; i++) b.Box(new Vector3((i % 2 - 0.5f) * 0.08f, -0.15f - i * 0.15f, 0.02f), new Vector3(0.2f, 0.05f, 0.2f), i % 2 == 0 ? Palette.GhillieStraw : Palette.Reed, new Vector3(0, i * 37f, 12f));
                if (s.Exo) b.Box(new Vector3(0f, -0.5f, 0.0f), new Vector3(legW + 0.06f, 0.3f, legW + 0.08f), Palette.ExoSteel);
            };
            rig.LegL = Part(rig.Body, "LegL", new Vector3(-0.1f * (s.Exo ? 1.3f : 1f), 0.92f, 0f), k + "_leg", leg);
            rig.LegR = Part(rig.Body, "LegR", new Vector3(0.1f * (s.Exo ? 1.3f : 1f), 0.92f, 0f), k + "_leg", leg);

            rig.Torso = Part(rig.Body, "Torso", new Vector3(0f, 0.92f, 0f), k + "_torso", b => SoldierTorso(b, s, teamCol));

            rig.Arms = Part(rig.Torso, "Arms", new Vector3(0f, 0.47f, 0.06f), k + "_arms", b => SoldierArms(b, s, teamCol));
            rig.Muzzle = Node(rig.Arms, "Muzzle", MuzzleOffset(s.Weapon));

            if (s.Weapon == Weapon.Mortar)
            {
                // Deployed mortar on the ground (shown while firing) and a carried tube.
                rig.Extra = Part(rig.Body, "MortarDeployed", new Vector3(0.3f, 0f, 0.7f), "mortar_deployed", b =>
                {
                    b.Box(new Vector3(0, 0.03f, 0), new Vector3(0.36f, 0.05f, 0.36f), Palette.Gunmetal);
                    b.Tube(new Vector3(0, 0.45f, 0.05f), 0.055f, 1.0f, Palette.Gunmetal, 8, new Vector3(-55f, 0, 0));
                    b.Box(new Vector3(0.12f, 0.25f, 0.25f), new Vector3(0.03f, 0.5f, 0.03f), Palette.Gunmetal, new Vector3(20, 0, 15));
                    b.Box(new Vector3(-0.12f, 0.25f, 0.25f), new Vector3(0.03f, 0.5f, 0.03f), Palette.Gunmetal, new Vector3(20, 0, -15));
                });
            }
            if (s.Pack == Pack.Parachute)
            {
                rig.Extra = Part(rig.Body, "Parachute", new Vector3(0f, 6.5f, 0f), "parachute", b =>
                {
                    b.Sphere(Vector3.zero, new Vector3(5.5f, 2.6f, 5.5f), Palette.Canvas, 6, 10, true);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i / 6f * 360f;
                        var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                        b.Box(dir * 1.3f + Vector3.down * 2.4f, new Vector3(0.02f, 4.8f, 0.02f), Palette.Rope, new Vector3(0, a, 0) + new Vector3(-28f * Mathf.Cos(0), 0, 0));
                    }
                }, false);
                rig.Extra.gameObject.SetActive(false);
            }
        }

        private static Vector3 MuzzleOffset(Weapon w)
        {
            switch (w)
            {
                case Weapon.Sniper: return new Vector3(0.05f, -0.07f, 1.25f);
                case Weapon.Bazooka: return new Vector3(0.15f, 0.16f, 0.95f);
                case Weapon.Mg: case Weapon.Gatling: return new Vector3(0.05f, -0.1f, 1.05f);
                case Weapon.Pistol: return new Vector3(0.12f, -0.1f, 0.55f);
                case Weapon.Flamer: return new Vector3(0.05f, -0.12f, 0.95f);
                default: return new Vector3(0.05f, -0.1f, 0.95f);
            }
        }

        private static void SoldierTorso(MeshBuilder b, SoldierSpec s, int teamCol)
        {
            float w = s.Exo ? 1.35f : 1f;
            b.Box(new Vector3(0, 0.04f, 0), new Vector3(0.37f * w, 0.13f, 0.23f * w), s.WhiteBelts ? Palette.White : Palette.Webbing);
            b.Box(new Vector3(0, 0.32f, 0), new Vector3(0.4f * w, 0.46f, 0.24f * w), s.Uniform);
            b.Box(new Vector3(0, 0.34f, 0.01f), new Vector3(0.44f * w, 0.36f, 0.29f * w), s.VestColor);
            if (s.WhiteBelts)
            {
                b.Box(new Vector3(0, 0.34f, 0.15f), new Vector3(0.06f, 0.48f, 0.02f), Palette.White, new Vector3(0, 0, 32f));
                b.Box(new Vector3(0, 0.34f, 0.15f), new Vector3(0.06f, 0.48f, 0.02f), Palette.White, new Vector3(0, 0, -32f));
            }
            else if (!s.Exo)
            {
                // ammo pouches + grenades on the vest
                for (int i = -1; i <= 1; i++) b.Box(new Vector3(i * 0.12f, 0.2f, 0.17f), new Vector3(0.09f, 0.1f, 0.06f), Palette.Pouch);
                b.Box(new Vector3(-0.15f, 0.42f, 0.16f), new Vector3(0.06f, 0.06f, 0.04f), Palette.Pouch);
            }
            if (s.Exo)
            {
                b.Box(new Vector3(0, 0.36f, 0.06f), new Vector3(0.62f, 0.42f, 0.36f), Palette.ExoSteel);
                b.Box(new Vector3(0.36f, 0.5f, 0f), new Vector3(0.22f, 0.18f, 0.3f), Palette.ExoSteel);
                b.Box(new Vector3(-0.36f, 0.5f, 0f), new Vector3(0.22f, 0.18f, 0.3f), Palette.ExoSteel);
                b.Box(new Vector3(0, 0.4f, -0.25f), new Vector3(0.4f, 0.4f, 0.16f), Palette.SteelDark); // power pack
                b.Box(new Vector3(0.12f, 0.6f, -0.26f), new Vector3(0.05f, 0.12f, 0.05f), Palette.Visor);
            }
            if (s.Coat)
            {
                b.Box(new Vector3(0, 0.12f, 0f), new Vector3(0.46f, 1.0f, 0.3f), s.CoatColor, default(Vector3));
                b.Box(new Vector3(0, -0.38f, 0f), new Vector3(0.5f, 0.22f, 0.33f), s.CoatColor);
                b.Box(new Vector3(0, 0.05f, 0.155f), new Vector3(0.48f, 0.06f, 0.02f), Palette.Leather); // belt
                b.Box(new Vector3(0.17f, 0.5f, 0.0f), new Vector3(0.14f, 0.04f, 0.26f), Palette.Gold); // epaulettes
                b.Box(new Vector3(-0.17f, 0.5f, 0.0f), new Vector3(0.14f, 0.04f, 0.26f), Palette.Gold);
            }
            if (s.Medal) b.Box(new Vector3(-0.1f, 0.4f, 0.17f), new Vector3(0.07f, 0.09f, 0.02f), Palette.Gold);
            if (s.Ghillie)
            {
                for (int i = 0; i < 16; i++)
                {
                    float a = i * 47f;
                    var d = Quaternion.Euler(0, a, 0) * Vector3.forward;
                    b.Box(new Vector3(d.x * 0.2f, 0.1f + (i % 4) * 0.15f, d.z * 0.15f), new Vector3(0.24f, 0.06f, 0.24f), i % 3 == 0 ? Palette.GhillieStraw : (i % 3 == 1 ? Palette.GhillieDark : Palette.Reed), new Vector3(10 + i * 7, a, 25));
                }
            }

            // Team armband (left shoulder) — the primary team identifier along with the helmet band.
            b.Box(new Vector3(-0.235f * w, 0.42f, 0f), new Vector3(0.07f, 0.09f, 0.15f), teamCol);
            b.Box(new Vector3(0.235f * w, 0.42f, 0f), new Vector3(0.07f, 0.09f, 0.15f), s.MedicBand ? Palette.White : s.Uniform);
            if (s.MedicBand) b.Box(new Vector3(0.27f * w, 0.42f, 0f), new Vector3(0.01f, 0.05f, 0.05f), Palette.MedicRed);

            // Head & neck.
            b.Box(new Vector3(0, 0.58f, 0), new Vector3(0.1f, 0.08f, 0.1f), Palette.Skin);
            if (s.Head == Headgear.Balaclava) b.Sphere(new Vector3(0, 0.69f, 0), new Vector3(0.22f, 0.24f, 0.23f), Palette.Black);
            else if (s.Head == Headgear.ExoHelm) b.Box(new Vector3(0, 0.7f, 0), new Vector3(0.3f, 0.28f, 0.3f), Palette.ExoSteel);
            else b.Sphere(new Vector3(0, 0.69f, 0), new Vector3(0.21f, 0.24f, 0.22f), Palette.Skin);

            switch (s.Head)
            {
                case Headgear.Helmet:
                case Headgear.ParaHelmet:
                    b.Sphere(new Vector3(0, 0.71f, 0), new Vector3(0.3f, 0.3f, 0.32f), Palette.Helmet, 6, 8, true);
                    if (s.Head == Headgear.Helmet) b.Cylinder(new Vector3(0, 0.715f, 0), 0.175f, 0.025f, Palette.Helmet, 10);
                    b.Cylinder(new Vector3(0, 0.75f, 0), 0.153f, 0.04f, teamCol, 10, default(Vector3), 0.15f, false);
                    b.Box(new Vector3(0, 0.66f, 0.1f), new Vector3(0.16f, 0.015f, 0.02f), Palette.Webbing); // chin strap
                    break;
                case Headgear.SoftCap:
                    b.Cylinder(new Vector3(0, 0.79f, 0), 0.125f, 0.09f, s.Uniform, 10);
                    b.Box(new Vector3(0, 0.76f, 0.13f), new Vector3(0.2f, 0.02f, 0.1f), s.Uniform);
                    b.Cylinder(new Vector3(0, 0.765f, 0), 0.128f, 0.025f, teamCol, 10, default(Vector3), 0.128f, false);
                    break;
                case Headgear.OfficerCap:
                    b.Cylinder(new Vector3(0, 0.8f, 0.01f), 0.14f, 0.07f, s.Coat ? Palette.OfficerCoat : s.Uniform, 12, default(Vector3), 0.17f);
                    b.Cylinder(new Vector3(0, 0.775f, 0), 0.143f, 0.03f, teamCol, 12);
                    b.Box(new Vector3(0, 0.765f, 0.15f), new Vector3(0.2f, 0.02f, 0.1f), Palette.Black, new Vector3(-12, 0, 0));
                    b.Box(new Vector3(0, 0.81f, 0.15f), new Vector3(0.05f, 0.04f, 0.01f), Palette.Gold);
                    break;
                case Headgear.Ceremonial:
                    b.Cylinder(new Vector3(0, 0.84f, 0), 0.15f, 0.22f, Palette.Black, 10, default(Vector3), 0.13f);
                    b.Cylinder(new Vector3(0, 0.79f, 0), 0.152f, 0.04f, teamCol, 10);
                    b.Box(new Vector3(0, 1.0f, 0), new Vector3(0.04f, 0.14f, 0.04f), Palette.White);
                    b.Box(new Vector3(0, 0.78f, 0.15f), new Vector3(0.06f, 0.06f, 0.01f), Palette.Gold);
                    break;
                case Headgear.Balaclava:
                    b.Box(new Vector3(0, 0.71f, 0.11f), new Vector3(0.14f, 0.04f, 0.02f), Palette.Skin);
                    b.Cylinder(new Vector3(0, 0.79f, 0), 0.13f, 0.05f, teamCol, 10);
                    break;
                case Headgear.ExoHelm:
                    b.Box(new Vector3(0, 0.72f, 0.155f), new Vector3(0.24f, 0.06f, 0.02f), Palette.Visor);
                    b.Box(new Vector3(0, 0.86f, 0), new Vector3(0.31f, 0.04f, 0.31f), teamCol);
                    break;
                case Headgear.Ghillie:
                    for (int i = 0; i < 8; i++) b.Box(new Vector3(0, 0.8f, 0), new Vector3(0.36f, 0.06f, 0.1f), i % 2 == 0 ? Palette.GhillieStraw : Palette.GhillieDark, new Vector3(i * 11f, i * 45f, 20f));
                    b.Box(new Vector3(0, 0.86f, 0), new Vector3(0.12f, 0.03f, 0.12f), teamCol);
                    break;
            }

            switch (s.Pack)
            {
                case Pack.Small:
                    b.Box(new Vector3(0, 0.36f, -0.19f), new Vector3(0.3f, 0.32f, 0.14f), Palette.Webbing);
                    b.Box(new Vector3(0, 0.2f, -0.24f), new Vector3(0.28f, 0.08f, 0.1f), Palette.Canvas);
                    break;
                case Pack.Radio:
                case Pack.Antenna:
                    b.Box(new Vector3(0, 0.36f, -0.2f), new Vector3(0.3f, 0.4f, 0.16f), Palette.TankOlive);
                    b.Box(new Vector3(0.1f, 0.95f, -0.24f), new Vector3(0.015f, 0.9f, 0.015f), Palette.Black);
                    break;
                case Pack.FlameTanks:
                    b.Cylinder(new Vector3(-0.09f, 0.33f, -0.2f), 0.085f, 0.5f, Palette.TankOlive, 8);
                    b.Cylinder(new Vector3(0.09f, 0.33f, -0.2f), 0.085f, 0.5f, Palette.TankOlive, 8);
                    b.Cylinder(new Vector3(0f, 0.3f, -0.17f), 0.05f, 0.32f, Palette.Hazard, 6);
                    break;
                case Pack.MedicBag:
                    b.Box(new Vector3(0.0f, 0.34f, -0.19f), new Vector3(0.3f, 0.3f, 0.14f), Palette.White);
                    b.Box(new Vector3(0.0f, 0.34f, -0.265f), new Vector3(0.12f, 0.04f, 0.01f), Palette.MedicRed);
                    b.Box(new Vector3(0.0f, 0.34f, -0.265f), new Vector3(0.04f, 0.12f, 0.01f), Palette.MedicRed);
                    break;
                case Pack.Engineer:
                    b.Box(new Vector3(0, 0.36f, -0.2f), new Vector3(0.32f, 0.34f, 0.15f), Palette.Webbing);
                    b.Box(new Vector3(0.12f, 0.45f, -0.29f), new Vector3(0.04f, 0.6f, 0.03f), Palette.Wood, new Vector3(0, 0, 15f));
                    b.Box(new Vector3(0.2f, 0.75f, -0.29f), new Vector3(0.14f, 0.18f, 0.02f), Palette.Gunmetal, new Vector3(0, 0, 15f));
                    break;
                case Pack.Parachute:
                    b.Box(new Vector3(0, 0.38f, -0.2f), new Vector3(0.34f, 0.4f, 0.16f), Palette.Canvas);
                    break;
                case Pack.Explosive:
                    for (int i = 0; i < 6; i++) b.Cylinder(new Vector3(-0.15f + i * 0.06f, 0.32f, 0.17f), 0.025f, 0.22f, i % 2 == 0 ? Palette.MedicRed : Palette.Hazard, 6);
                    b.Box(new Vector3(0, 0.32f, 0.2f), new Vector3(0.4f, 0.03f, 0.02f), Palette.Black);
                    break;
                case Pack.MineBag:
                    b.Cylinder(new Vector3(0, 0.4f, -0.2f), 0.15f, 0.08f, Palette.TankOlive, 10, new Vector3(90, 0, 0));
                    b.Cylinder(new Vector3(0, 0.22f, -0.2f), 0.15f, 0.08f, Palette.TankOlive, 10, new Vector3(90, 0, 0));
                    break;
                case Pack.Mortar:
                    b.Box(new Vector3(0, 0.36f, -0.2f), new Vector3(0.3f, 0.32f, 0.14f), Palette.Webbing);
                    b.Tube(new Vector3(0.12f, 0.5f, -0.24f), 0.05f, 0.9f, Palette.Gunmetal, 6, new Vector3(-70f, 0, 0));
                    break;
            }
        }

        private static void SoldierArms(MeshBuilder b, SoldierSpec s, int teamCol)
        {
            int sleeve = s.Coat ? s.CoatColor : (s.Exo ? Palette.ExoSteel : (s.Ghillie ? Palette.GhillieDark : s.Uniform));
            float w = s.Exo ? 1.3f : 1f;
            // Upper arms + forearms in a two-hand weapon hold.
            b.Box(new Vector3(0.21f * w, -0.08f, 0.04f), new Vector3(0.1f * w, 0.28f, 0.11f * w), sleeve, new Vector3(25f, 0, 8f));
            b.Box(new Vector3(0.16f * w, -0.17f, 0.22f), new Vector3(0.09f * w, 0.09f * w, 0.3f), sleeve, new Vector3(-8f, -12f, 0));
            b.Box(new Vector3(-0.21f * w, -0.08f, 0.06f), new Vector3(0.1f * w, 0.28f, 0.11f * w), sleeve, new Vector3(35f, 0, -8f));
            b.Box(new Vector3(-0.1f * w, -0.15f, 0.3f), new Vector3(0.09f * w, 0.09f * w, 0.34f), sleeve, new Vector3(-6f, 22f, 0));
            int glove = s.Coat ? Palette.Leather : (s.Exo ? Palette.SteelDark : Palette.Skin);
            b.Box(new Vector3(0.1f, -0.18f, 0.38f), new Vector3(0.08f, 0.08f, 0.09f), glove);
            b.Box(new Vector3(0.0f, -0.12f, 0.5f), new Vector3(0.08f, 0.08f, 0.09f), glove);

            switch (s.Weapon)
            {
                case Weapon.Rifle:
                case Weapon.BayonetRifle:
                    b.Box(new Vector3(0.05f, -0.1f, 0.42f), new Vector3(0.055f, 0.09f, 0.62f), Palette.Gunmetal);
                    b.Box(new Vector3(0.05f, -0.13f, 0.08f), new Vector3(0.05f, 0.11f, 0.24f), Palette.Wood);
                    b.Box(new Vector3(0.05f, -0.2f, 0.36f), new Vector3(0.04f, 0.13f, 0.06f), Palette.Gunmetal, new Vector3(12, 0, 0));
                    b.Tube(new Vector3(0.05f, -0.08f, 0.82f), 0.014f, 0.3f, Palette.Gunmetal);
                    if (s.Weapon == Weapon.BayonetRifle) b.Box(new Vector3(0.05f, -0.1f, 1.02f), new Vector3(0.015f, 0.03f, 0.22f), Palette.MetalLight);
                    break;
                case Weapon.Smg:
                    b.Box(new Vector3(0.05f, -0.1f, 0.4f), new Vector3(0.06f, 0.1f, 0.48f), Palette.Gunmetal);
                    b.Box(new Vector3(0.05f, -0.22f, 0.4f), new Vector3(0.04f, 0.16f, 0.05f), Palette.Gunmetal);
                    b.Tube(new Vector3(0.05f, -0.08f, 0.72f), 0.018f, 0.2f, Palette.Gunmetal);
                    break;
                case Weapon.Mg:
                    b.Box(new Vector3(0.05f, -0.1f, 0.45f), new Vector3(0.09f, 0.13f, 0.7f), Palette.Gunmetal);
                    b.Tube(new Vector3(0.05f, -0.08f, 0.92f), 0.025f, 0.35f, Palette.Gunmetal);
                    b.Box(new Vector3(-0.03f, -0.13f, 0.4f), new Vector3(0.1f, 0.12f, 0.1f), Palette.TankOlive); // ammo box
                    b.Box(new Vector3(0.05f, -0.2f, 0.9f), new Vector3(0.02f, 0.18f, 0.02f), Palette.Gunmetal, new Vector3(0, 0, 25));
                    b.Box(new Vector3(0.05f, -0.2f, 0.9f), new Vector3(0.02f, 0.18f, 0.02f), Palette.Gunmetal, new Vector3(0, 0, -25));
                    break;
                case Weapon.Sniper:
                    b.Box(new Vector3(0.05f, -0.1f, 0.45f), new Vector3(0.05f, 0.08f, 0.7f), Palette.Gunmetal);
                    b.Box(new Vector3(0.05f, -0.13f, 0.06f), new Vector3(0.05f, 0.12f, 0.26f), s.Ghillie ? Palette.GhillieDark : Palette.Wood);
                    b.Tube(new Vector3(0.05f, -0.08f, 1.0f), 0.013f, 0.5f, Palette.Gunmetal);
                    b.Tube(new Vector3(0.05f, -0.01f, 0.42f), 0.025f, 0.28f, Palette.Black);
                    break;
                case Weapon.Bazooka:
                    b.Tube(new Vector3(0.15f, 0.12f, 0.35f), 0.065f, 1.2f, Palette.TankOlive);
                    b.Tube(new Vector3(0.15f, 0.12f, 0.95f), 0.07f, 0.08f, Palette.Gunmetal);
                    b.Box(new Vector3(0.15f, 0.0f, 0.3f), new Vector3(0.04f, 0.14f, 0.05f), Palette.Wood);
                    break;
                case Weapon.Flamer:
                    b.Box(new Vector3(0.05f, -0.12f, 0.45f), new Vector3(0.06f, 0.09f, 0.6f), Palette.Gunmetal);
                    b.Tube(new Vector3(0.05f, -0.12f, 0.82f), 0.03f, 0.2f, Palette.Black);
                    b.Box(new Vector3(0.05f, -0.2f, 0.25f), new Vector3(0.03f, 0.03f, 0.4f), Palette.Black, new Vector3(30, 0, 0)); // hose
                    break;
                case Weapon.Detector:
                    b.Box(new Vector3(0.05f, -0.4f, 0.75f), new Vector3(0.03f, 0.03f, 1.1f), Palette.Gunmetal, new Vector3(35, 0, 0));
                    b.Cylinder(new Vector3(0.05f, -0.75f, 1.2f), 0.16f, 0.03f, Palette.TankOlive, 10);
                    b.Box(new Vector3(-0.15f, 0.0f, 0.1f), new Vector3(0.08f, 0.08f, 0.08f), Palette.Black); // headset box
                    break;
                case Weapon.Mortar:
                    b.Box(new Vector3(0.05f, -0.1f, 0.4f), new Vector3(0.055f, 0.09f, 0.55f), Palette.Gunmetal);
                    break;
                case Weapon.Gatling:
                    b.Box(new Vector3(0.05f, -0.12f, 0.35f), new Vector3(0.14f, 0.16f, 0.4f), Palette.Gunmetal);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i / 6f * Mathf.PI * 2f;
                        b.Tube(new Vector3(0.05f + Mathf.Cos(a) * 0.04f, -0.12f + Mathf.Sin(a) * 0.04f, 0.75f), 0.012f, 0.55f, Palette.Gunmetal, 5);
                    }
                    b.Box(new Vector3(-0.1f, -0.2f, 0.3f), new Vector3(0.14f, 0.16f, 0.18f), Palette.TankOlive);
                    break;
                case Weapon.Pistol:
                    b.Box(new Vector3(0.1f, -0.13f, 0.45f), new Vector3(0.035f, 0.08f, 0.16f), Palette.Black);
                    break;
                case Weapon.Megaphone:
                    b.Cylinder(new Vector3(0.05f, -0.06f, 0.5f), 0.04f, 0.3f, Palette.White, 8, new Vector3(90, 0, 0), 0.1f);
                    break;
                case Weapon.Controller:
                    b.Box(new Vector3(0.05f, -0.14f, 0.45f), new Vector3(0.24f, 0.05f, 0.14f), Palette.Black);
                    b.Box(new Vector3(0.12f, -0.06f, 0.48f), new Vector3(0.01f, 0.14f, 0.01f), Palette.Gunmetal);
                    break;
            }
            if (s.Shield)
            {
                b.Box(new Vector3(-0.2f, -0.12f, 0.52f), new Vector3(0.55f, 0.95f, 0.05f), Palette.SteelDark);
                b.Box(new Vector3(-0.2f, 0.12f, 0.55f), new Vector3(0.3f, 0.08f, 0.02f), Palette.Glass);
                b.Box(new Vector3(-0.2f, -0.45f, 0.55f), new Vector3(0.45f, 0.06f, 0.02f), teamCol);
            }
        }

        // ------------------------------------------------------------------ vehicles

        private sealed class VSpec
        {
            public float L = 6.2f, W = 3.2f, H = 1.1f;
            public bool Tracked = true;
            public int Wheels = 0;            // per side, wheeled vehicles
            public float TurretW = 2.2f, TurretL = 2.6f, TurretH = 0.75f;
            public float TurretZ = -0.2f;
            public float BarrelL = 3.2f, BarrelR = 0.09f;
            public bool NoTurret;
            public int Color = Palette.TankOlive;
            public string Weapon = "cannon";   // cannon, mg, twinaa, rockets, flame, nebel, none, casemate, howitzer, blade
            public bool Skirts;
            public bool OpenTop;
            public float ClearanceY = 0.55f;
        }

        private static VSpec VehicleSpec(string v)
        {
            var s = new VSpec();
            switch (v)
            {
                case "tank_light": s.L = 5f; s.W = 2.7f; s.H = 0.9f; s.TurretW = 1.7f; s.TurretL = 2f; s.TurretH = 0.62f; s.BarrelL = 2.6f; s.BarrelR = 0.06f; break;
                case "tank_medium": break;
                case "tank_heavy": s.L = 7.3f; s.W = 3.7f; s.H = 1.35f; s.TurretW = 2.8f; s.TurretL = 3.1f; s.TurretH = 0.95f; s.BarrelL = 4.4f; s.BarrelR = 0.12f; s.Skirts = true; s.Color = Palette.TankGrey; break;
                case "tank_flame": s.Weapon = "flame"; s.BarrelL = 1.4f; s.BarrelR = 0.14f; s.Skirts = true; break;
                case "tank_spg": s.Weapon = "howitzer"; s.L = 6.6f; s.TurretW = 2.8f; s.TurretL = 3.0f; s.TurretH = 1.2f; s.TurretZ = -1.1f; s.BarrelL = 4.6f; s.BarrelR = 0.14f; s.OpenTop = true; break;
                case "tank_assaultgun": s.Weapon = "casemate"; s.TurretW = 2.8f; s.TurretL = 3.4f; s.TurretH = 0.9f; s.TurretZ = 0.4f; s.BarrelL = 2.2f; s.BarrelR = 0.16f; s.Color = Palette.TankGrey; break;
                case "tank_destroyer": s.Weapon = "casemate"; s.L = 6.6f; s.H = 1.0f; s.TurretW = 2.6f; s.TurretL = 3.2f; s.TurretH = 0.8f; s.TurretZ = 0.2f; s.BarrelL = 4.4f; s.BarrelR = 0.1f; s.Skirts = true; break;
                case "tank_jagd": s.Weapon = "casemate"; s.L = 7.8f; s.W = 3.7f; s.H = 1.25f; s.TurretW = 3.2f; s.TurretL = 4.0f; s.TurretH = 1.1f; s.TurretZ = -0.5f; s.BarrelL = 5.2f; s.BarrelR = 0.13f; s.Skirts = true; s.Color = Palette.TankGrey; break;
                case "vehicle_aa": s.Weapon = "twinaa"; s.TurretW = 2.2f; s.TurretL = 2.2f; s.TurretH = 0.9f; s.BarrelL = 2.4f; s.BarrelR = 0.05f; break;
                case "vehicle_armoredcar": s.Tracked = false; s.Wheels = 3; s.L = 5.4f; s.W = 2.4f; s.H = 1.2f; s.TurretW = 1.3f; s.TurretL = 1.5f; s.TurretH = 0.6f; s.Weapon = "mg"; s.BarrelL = 1.2f; s.BarrelR = 0.04f; s.ClearanceY = 0.7f; break;
                case "vehicle_apc": s.Tracked = false; s.Wheels = 4; s.L = 6.4f; s.W = 2.6f; s.H = 1.7f; s.TurretW = 1.0f; s.TurretL = 1.0f; s.TurretH = 0.4f; s.Weapon = "mg"; s.BarrelL = 1.1f; s.BarrelR = 0.035f; s.ClearanceY = 0.75f; break;
                case "vehicle_flameapc": s.Tracked = false; s.Wheels = 4; s.L = 6.4f; s.W = 2.6f; s.H = 1.6f; s.TurretW = 1.3f; s.TurretL = 1.4f; s.TurretH = 0.55f; s.Weapon = "flame"; s.BarrelL = 1.3f; s.BarrelR = 0.1f; s.ClearanceY = 0.75f; break;
                case "vehicle_jeep": s.Tracked = false; s.Wheels = 2; s.L = 3.6f; s.W = 1.7f; s.H = 0.55f; s.Weapon = "mg"; s.TurretW = 0.3f; s.TurretL = 0.3f; s.TurretH = 0.6f; s.TurretZ = -0.6f; s.BarrelL = 1.1f; s.BarrelR = 0.035f; s.OpenTop = true; s.ClearanceY = 0.55f; break;
                case "robot_recon": s.L = 1.8f; s.W = 1.2f; s.H = 0.5f; s.Weapon = "mg"; s.TurretW = 0.5f; s.TurretL = 0.5f; s.TurretH = 0.35f; s.TurretZ = 0f; s.BarrelL = 0.6f; s.BarrelR = 0.025f; s.Color = Palette.TankGrey; s.ClearanceY = 0.25f; break;
                case "vehicle_minelayer": s.Weapon = "mg"; s.TurretW = 1.0f; s.TurretL = 1.0f; s.TurretH = 0.45f; s.TurretZ = 1.2f; s.BarrelL = 1.0f; s.BarrelR = 0.04f; break;
                case "vehicle_truck": s.Tracked = false; s.Wheels = 3; s.L = 7f; s.W = 2.4f; s.H = 1.0f; s.Weapon = "none"; s.NoTurret = true; s.ClearanceY = 0.8f; break;
                case "vehicle_mlrs": s.Tracked = false; s.Wheels = 3; s.L = 7f; s.W = 2.5f; s.H = 1.0f; s.Weapon = "rockets"; s.TurretZ = -1.6f; s.TurretW = 2.2f; s.TurretL = 2.8f; s.TurretH = 1.0f; s.ClearanceY = 0.8f; break;
                case "vehicle_nebel": s.Tracked = false; s.Wheels = 2; s.L = 4.2f; s.W = 2.2f; s.H = 0.6f; s.Weapon = "nebel"; s.TurretZ = 0f; s.TurretW = 1.4f; s.TurretL = 1.6f; s.TurretH = 0.9f; s.ClearanceY = 0.6f; s.Color = Palette.TankGrey; break;
                case "vehicle_engineer": s.Weapon = "blade"; s.TurretW = 1.4f; s.TurretL = 1.6f; s.TurretH = 0.6f; s.BarrelL = 1.0f; s.BarrelR = 0.04f; break;
            }
            return s;
        }

        private static void BuildVehicle(UnitRig rig, string v, Team team)
        {
            var s = VehicleSpec(v);
            rig.Kind = RigKind.Vehicle;
            rig.Tracked = s.Tracked;
            rig.Height = s.ClearanceY + s.H + s.TurretH + 0.4f;
            string k = v + "_" + team;
            int teamCol = Palette.Team(team);
            int flag = Palette.Flag(team);
            rig.Body = Node(rig.transform, "Body", Vector3.zero);
            float hullY = s.ClearanceY + s.H * 0.5f;

            Part(rig.Body, "Hull", Vector3.zero, k + "_hull", b =>
            {
                if (v == "vehicle_truck" || v == "vehicle_mlrs")
                {
                    // cab + chassis + bed
                    b.Box(new Vector3(0, s.ClearanceY + 0.25f, 0), new Vector3(s.W * 0.9f, 0.5f, s.L), Palette.SteelDark);
                    b.Wedge(new Vector3(0, s.ClearanceY + 1.0f, s.L * 0.36f), new Vector3(s.W, 1.4f, 1.8f), 0.95f, 0.75f, -0.1f, s.Color);
                    b.Box(new Vector3(0, s.ClearanceY + 1.3f, s.L * 0.36f + 0.7f), new Vector3(s.W * 0.8f, 0.45f, 0.05f), Palette.Glass);
                    b.Box(new Vector3(0, s.ClearanceY + 0.45f, s.L * 0.5f), new Vector3(s.W * 0.95f, 0.5f, 0.3f), Palette.Gunmetal);
                    if (v == "vehicle_truck")
                    {
                        b.Box(new Vector3(0, s.ClearanceY + 0.75f, -s.L * 0.15f), new Vector3(s.W, 0.6f, s.L * 0.62f), s.Color);
                        b.Wedge(new Vector3(0, s.ClearanceY + 1.65f, -s.L * 0.15f), new Vector3(s.W * 1.02f, 1.2f, s.L * 0.62f), 0.8f, 1f, 0f, Palette.Canvas);
                        b.Box(new Vector3(s.W * 0.51f, s.ClearanceY + 1.0f, -s.L * 0.15f), new Vector3(0.02f, 0.5f, 1.2f), teamCol);
                        b.Box(new Vector3(-s.W * 0.51f, s.ClearanceY + 1.0f, -s.L * 0.15f), new Vector3(0.02f, 0.5f, 1.2f), teamCol);
                    }
                    else b.Box(new Vector3(s.W * 0.51f, s.ClearanceY + 1.0f, s.L * 0.36f), new Vector3(0.02f, 0.4f, 0.8f), teamCol);
                    return;
                }
                if (v == "vehicle_jeep")
                {
                    b.Box(new Vector3(0, s.ClearanceY + 0.3f, 0), new Vector3(s.W, 0.55f, s.L), s.Color);
                    b.Box(new Vector3(0, s.ClearanceY + 0.62f, s.L * 0.38f), new Vector3(s.W * 0.95f, 0.1f, s.L * 0.24f), s.Color);
                    b.Box(new Vector3(0, s.ClearanceY + 0.95f, s.L * 0.18f), new Vector3(s.W * 0.92f, 0.5f, 0.04f), Palette.Glass, new Vector3(-15, 0, 0));
                    b.Box(new Vector3(0.4f, s.ClearanceY + 0.75f, -0.3f), new Vector3(0.5f, 0.35f, 0.5f), Palette.Leather);
                    b.Box(new Vector3(-0.4f, s.ClearanceY + 0.75f, -0.3f), new Vector3(0.5f, 0.35f, 0.5f), Palette.Leather);
                    b.Box(new Vector3(0, s.ClearanceY + 0.66f, s.L * 0.38f), new Vector3(0.4f, 0.02f, 0.3f), teamCol);
                    b.Cylinder(new Vector3(0, s.ClearanceY + 0.45f, -s.L * 0.5f - 0.15f), 0.32f, 0.2f, Palette.Rubber, 10, new Vector3(90, 0, 0));
                    return;
                }
                // Generic armoured hull with sloped glacis.
                b.Wedge(new Vector3(0, hullY, 0), new Vector3(s.W * (s.Tracked ? 0.78f : 0.95f), s.H, s.L), 1f, 0.82f, -0.04f, s.Color);
                b.Wedge(new Vector3(0, hullY + s.H * 0.15f, s.L * 0.47f), new Vector3(s.W * 0.76f, s.H * 0.7f, 0.5f), 1f, 0.2f, -0.3f, s.Color, new Vector3(0, 0, 0));
                if (s.Tracked)
                {
                    b.Box(new Vector3(0, hullY + s.H * 0.42f, 0), new Vector3(s.W * 1.02f, 0.08f, s.L * 0.96f), s.Color); // fenders
                    if (s.Skirts)
                    {
                        b.Box(new Vector3(s.W * 0.52f, hullY + 0.05f, 0), new Vector3(0.05f, s.H * 0.65f, s.L * 0.9f), s.Color);
                        b.Box(new Vector3(-s.W * 0.52f, hullY + 0.05f, 0), new Vector3(0.05f, s.H * 0.65f, s.L * 0.9f), s.Color);
                    }
                    // engine deck, tools, spare track links, jerry cans
                    b.Box(new Vector3(0, hullY + s.H * 0.52f, -s.L * 0.36f), new Vector3(s.W * 0.6f, 0.06f, s.L * 0.22f), Palette.SteelDark);
                    b.Box(new Vector3(s.W * 0.35f, hullY + s.H * 0.5f, s.L * 0.1f), new Vector3(0.12f, 0.08f, 1.0f), Palette.Wood);
                    b.Box(new Vector3(-s.W * 0.3f, hullY + s.H * 0.58f, -s.L * 0.42f), new Vector3(0.3f, 0.36f, 0.2f), Palette.FuelDrum);
                }
                else
                {
                    b.Wedge(new Vector3(0, hullY + s.H * 0.5f + 0.15f, -0.2f), new Vector3(s.W * 0.92f, 0.35f, s.L * 0.8f), 0.85f, 0.9f, 0f, s.Color);
                }
                // headlights + team stripe on the rear
                b.Box(new Vector3(s.W * 0.3f, hullY + s.H * 0.3f, s.L * 0.5f), new Vector3(0.12f, 0.12f, 0.08f), Palette.LampLight);
                b.Box(new Vector3(-s.W * 0.3f, hullY + s.H * 0.3f, s.L * 0.5f), new Vector3(0.12f, 0.12f, 0.08f), Palette.LampLight);
                b.Box(new Vector3(0, hullY + s.H * 0.1f, -s.L * 0.5f), new Vector3(s.W * 0.6f, 0.18f, 0.03f), teamCol);
                if (v == "vehicle_minelayer") b.Wedge(new Vector3(0, hullY + s.H * 0.5f + 0.4f, -s.L * 0.3f), new Vector3(s.W * 0.7f, 0.8f, s.L * 0.3f), 1.2f, 1.2f, 0f, Palette.Rust);
                if (v == "vehicle_engineer")
                {
                    b.Box(new Vector3(0, s.ClearanceY + 0.3f, s.L * 0.5f + 0.6f), new Vector3(s.W * 1.15f, 0.9f, 0.15f), Palette.Hazard, new Vector3(-15, 0, 0));
                    b.Box(new Vector3(s.W * 0.3f, hullY + s.H, -0.6f), new Vector3(0.15f, 0.15f, 2.6f), Palette.Hazard, new Vector3(-25, 0, 0));
                }
            });

            if (s.Tracked)
            {
                BuildFn track = b =>
                {
                    b.Box(new Vector3(0, s.ClearanceY * 0.9f, 0), new Vector3(s.W * 0.2f, s.ClearanceY * 1.5f + 0.1f, s.L * 1.0f), Palette.Track);
                    int wheels = Mathf.Max(3, Mathf.RoundToInt(s.L / 1.05f));
                    for (int i = 0; i < wheels; i++)
                    {
                        float z = -s.L * 0.42f + i * (s.L * 0.84f / (wheels - 1));
                        b.Cylinder(new Vector3(0.0f, s.ClearanceY * 0.6f, z), s.ClearanceY * 0.48f, s.W * 0.23f, Palette.SteelDark, 10, new Vector3(0, 0, 90));
                    }
                    b.Cylinder(new Vector3(0, s.ClearanceY * 1.1f, s.L * 0.48f), s.ClearanceY * 0.4f, s.W * 0.22f, Palette.SteelDark, 8, new Vector3(0, 0, 90));
                };
                Part(rig.Body, "TrackL", new Vector3(-s.W * 0.4f, 0, 0), k + "_track", track);
                Part(rig.Body, "TrackR", new Vector3(s.W * 0.4f, 0, 0), k + "_track", track);
                BuildFn cleats = b =>
                {
                    int n = Mathf.RoundToInt(s.L / 0.36f);
                    for (int i = 0; i < n; i++)
                    {
                        float z = -s.L * 0.5f + i * 0.36f;
                        b.Box(new Vector3(0, s.ClearanceY * 1.65f + 0.06f, z), new Vector3(s.W * 0.21f, 0.04f, 0.12f), Palette.Rubber);
                    }
                };
                rig.TreadL = Part(rig.Body, "TreadL", new Vector3(-s.W * 0.4f, 0, 0), k + "_cleats", cleats, false);
                rig.TreadR = Part(rig.Body, "TreadR", new Vector3(s.W * 0.4f, 0, 0), k + "_cleats", cleats, false);
                rig.TreadSpacing = 0.36f;
            }
            else
            {
                var wheels = new List<Transform>();
                float r = s.ClearanceY * 0.75f;
                rig.WheelRadius = r;
                for (int i = 0; i < s.Wheels; i++)
                {
                    float z = s.Wheels == 1 ? 0f : -s.L * 0.36f + i * (s.L * 0.72f / (s.Wheels - 1));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var w = Part(rig.Body, "Wheel", new Vector3(side * s.W * 0.48f, r, z), "wheel_" + r.ToString("0.00"), b =>
                        {
                            b.Cylinder(Vector3.zero, r, 0.35f, Palette.Rubber, 12, new Vector3(0, 0, 90));
                            b.Cylinder(Vector3.zero, r * 0.5f, 0.37f, Palette.SteelDark, 8, new Vector3(0, 0, 90));
                        });
                        wheels.Add(w);
                    }
                }
                rig.Wheels = wheels.ToArray();
            }

            if (s.NoTurret) { rig.Muzzle = Node(rig.Body, "Muzzle", new Vector3(0, hullY + 1f, s.L * 0.5f)); return; }

            float turretY = s.ClearanceY + s.H + (s.Weapon == "casemate" ? -s.H * 0.4f : 0f);
            rig.Turret = Part(rig.Body, "Turret", new Vector3(0, turretY, s.TurretZ), k + "_turret", b =>
            {
                switch (s.Weapon)
                {
                    case "twinaa":
                        b.Cylinder(new Vector3(0, s.TurretH * 0.5f, 0), s.TurretW * 0.5f, s.TurretH, s.Color, 10, default(Vector3), s.TurretW * 0.42f);
                        b.Box(new Vector3(0, s.TurretH + 0.6f, -0.6f), new Vector3(0.9f, 0.05f, 0.6f), Palette.Gunmetal, new Vector3(-30, 0, 0)); // radar
                        b.Box(new Vector3(0, s.TurretH * 0.6f, 0), new Vector3(0.2f, 0.25f, 0.6f), teamCol);
                        break;
                    case "rockets":
                        b.Box(new Vector3(0, 0.2f, 0), new Vector3(1.2f, 0.4f, 1.2f), Palette.SteelDark);
                        break;
                    case "nebel":
                        b.Box(new Vector3(0, 0.1f, 0), new Vector3(1.2f, 0.2f, 1.2f), Palette.SteelDark);
                        break;
                    case "mg":
                        b.Cylinder(new Vector3(0, s.TurretH * 0.5f, 0), s.TurretW * 0.5f, s.TurretH, s.Color, 10, default(Vector3), s.TurretW * 0.45f);
                        b.Box(new Vector3(0, s.TurretH * 0.5f, -s.TurretW * 0.45f), new Vector3(0.25f, 0.2f, 0.04f), teamCol);
                        break;
                    case "casemate":
                        b.Wedge(new Vector3(0, s.TurretH * 0.5f, 0), new Vector3(s.TurretW, s.TurretH, s.TurretL), 0.82f, 0.7f, -0.05f, s.Color);
                        b.Box(new Vector3(s.TurretW * 0.5f + 0.01f, s.TurretH * 0.5f, -0.3f), new Vector3(0.02f, 0.3f, 0.6f), teamCol);
                        break;
                    default:
                        b.Wedge(new Vector3(0, s.TurretH * 0.5f, 0), new Vector3(s.TurretW, s.TurretH, s.TurretL), 0.8f, 0.78f, -0.05f, s.Color);
                        b.Box(new Vector3(0, s.TurretH * 0.45f, s.TurretL * 0.5f), new Vector3(s.TurretW * 0.42f, s.TurretH * 0.75f, 0.35f), s.Color); // mantlet
                        b.Cylinder(new Vector3(s.TurretW * 0.22f, s.TurretH + 0.15f, -s.TurretL * 0.15f), 0.3f, 0.3f, s.Color, 10); // cupola
                        b.Box(new Vector3(-s.TurretW * 0.3f, s.TurretH + 0.6f, -s.TurretL * 0.4f), new Vector3(0.02f, 1.4f, 0.02f), Palette.Black); // antenna
                        b.Plane(new Vector3(-s.TurretW * 0.3f - 0.2f, s.TurretH + 1.1f, -s.TurretL * 0.4f), new Vector2(0.4f, 0.28f), flag, new Vector3(0, 0, 90));
                        b.Box(new Vector3(s.TurretW * 0.5f + 0.01f, s.TurretH * 0.5f, 0f), new Vector3(0.02f, 0.25f, 0.5f), teamCol);
                        b.Box(new Vector3(-s.TurretW * 0.5f - 0.01f, s.TurretH * 0.5f, 0f), new Vector3(0.02f, 0.25f, 0.5f), teamCol);
                        if (s.OpenTop) b.Box(new Vector3(0, s.TurretH * 0.95f, -0.2f), new Vector3(s.TurretW * 0.7f, 0.05f, s.TurretL * 0.6f), Palette.Black);
                        if (s.Weapon == "flame") { b.Cylinder(new Vector3(0, s.TurretH * 0.6f, -s.TurretL * 0.55f), 0.35f, s.TurretW * 0.8f, Palette.Rust, 10, new Vector3(0, 0, 90)); }
                        break;
                }
            });

            // Barrel (separate node for recoil).
            Vector3 barrelPos = new Vector3(0, s.TurretH * 0.5f, s.TurretL * 0.5f);
            if (s.Weapon == "mg") barrelPos = new Vector3(0, s.TurretH * 0.75f, s.TurretW * 0.4f);
            if (s.Weapon == "rockets" || s.Weapon == "nebel") barrelPos = new Vector3(0, 0.5f, 0f);
            rig.Barrel = Part(rig.Turret, "Barrel", barrelPos, k + "_barrel", b =>
            {
                switch (s.Weapon)
                {
                    case "twinaa":
                        b.Tube(new Vector3(0.25f, 0, s.BarrelL * 0.5f), s.BarrelR, s.BarrelL, Palette.Gunmetal);
                        b.Tube(new Vector3(-0.25f, 0, s.BarrelL * 0.5f), s.BarrelR, s.BarrelL, Palette.Gunmetal);
                        break;
                    case "rockets":
                        b.Box(new Vector3(0, 0.5f, 0.2f), new Vector3(s.TurretW, 1.0f, s.TurretL), s.Color);
                        for (int x = 0; x < 4; x++) for (int y = 0; y < 3; y++) b.Cylinder(new Vector3(-0.75f + x * 0.5f, 0.2f + y * 0.3f, s.TurretL * 0.5f + 0.21f), 0.12f, 0.02f, Palette.Black, 6, new Vector3(90, 0, 0));
                        b.Box(new Vector3(s.TurretW * 0.5f + 0.01f, 0.5f, 0.2f), new Vector3(0.02f, 0.3f, 0.8f), teamCol);
                        break;
                    case "nebel":
                        for (int i = 0; i < 6; i++)
                        {
                            float a = i / 6f * Mathf.PI * 2f;
                            b.Tube(new Vector3(Mathf.Cos(a) * 0.3f, 0.35f + Mathf.Sin(a) * 0.3f, 0.3f), 0.13f, 1.4f, Palette.SteelDark, 8);
                        }
                        break;
                    case "flame":
                        b.Tube(new Vector3(0, 0, s.BarrelL * 0.5f), s.BarrelR, s.BarrelL, Palette.Gunmetal, 8, default(Vector3), s.BarrelR * 0.6f);
                        break;
                    case "none":
                        break;
                    default:
                        b.Tube(new Vector3(0, 0, s.BarrelL * 0.5f), s.BarrelR, s.BarrelL, Palette.Gunmetal, 8);
                        if (s.BarrelR > 0.07f) b.Tube(new Vector3(0, 0, s.BarrelL), s.BarrelR * 1.5f, 0.35f, Palette.Gunmetal, 8); // muzzle brake
                        break;
                }
            });
            if (s.Weapon == "rockets" || s.Weapon == "nebel" || s.Weapon == "howitzer") rig.Barrel.localRotation = Quaternion.Euler(-25f, 0, 0);
            rig.BarrelRestZ = rig.Barrel.localPosition.z;
            float muzzleZ = s.Weapon == "rockets" ? s.TurretL * 0.6f : (s.Weapon == "nebel" ? 1.0f : s.BarrelL + 0.2f);
            rig.Muzzle = Node(rig.Barrel, "Muzzle", new Vector3(0, s.Weapon == "rockets" ? 0.5f : 0f, muzzleZ));
        }

        // ------------------------------------------------------------------ aircraft

        private static void BuildAircraft(UnitRig rig, string v, Team team)
        {
            rig.Kind = RigKind.Aircraft;
            string k = v + "_" + team;
            int teamCol = Palette.Team(team);
            rig.Body = Node(rig.transform, "Body", Vector3.zero);
            var spinners = new List<Transform>();
            var axes = new List<Vector3>();
            var speeds = new List<float>();
            rig.Height = 2f;

            switch (v)
            {
                case "air_drone":
                case "air_flaredrone":
                {
                    Part(rig.Body, "Frame", Vector3.zero, k + "_frame", b =>
                    {
                        b.Box(Vector3.zero, new Vector3(0.7f, 0.25f, 0.9f), Palette.AirDark);
                        b.Box(Vector3.zero, new Vector3(2.2f, 0.06f, 0.1f), Palette.AirGrey, new Vector3(0, 45, 0));
                        b.Box(Vector3.zero, new Vector3(2.2f, 0.06f, 0.1f), Palette.AirGrey, new Vector3(0, -45, 0));
                        b.Sphere(new Vector3(0, -0.2f, 0.3f), new Vector3(0.25f, 0.25f, 0.25f), Palette.Black);
                        b.Box(new Vector3(0, 0.14f, 0), new Vector3(0.3f, 0.03f, 0.3f), teamCol);
                        if (v == "air_flaredrone") b.Sphere(new Vector3(0, -0.35f, 0), new Vector3(0.35f, 0.35f, 0.35f), Palette.LampLight);
                    });
                    for (int i = 0; i < 4; i++)
                    {
                        float a = (45f + i * 90f) * Mathf.Deg2Rad;
                        var p = new Vector3(Mathf.Sin(a) * 0.78f, 0.1f, Mathf.Cos(a) * 0.78f);
                        var rot = Part(rig.Body, "Rotor", p, "drone_rotor", b => { b.Box(Vector3.zero, new Vector3(0.9f, 0.015f, 0.08f), Palette.Black); }, false);
                        spinners.Add(rot); axes.Add(Vector3.up); speeds.Add(2400f);
                    }
                    rig.Muzzle = Node(rig.Body, "Muzzle", new Vector3(0, -0.3f, 0.4f));
                    break;
                }
                case "air_kamikaze":
                {
                    Part(rig.Body, "Frame", Vector3.zero, k + "_frame", b =>
                    {
                        b.Tube(Vector3.zero, 0.15f, 1.6f, Palette.AirGrey, 8, default(Vector3), 0.08f);
                        b.Wedge(new Vector3(0, 0, -0.2f), new Vector3(2.2f, 0.05f, 0.9f), 0.2f, 0.4f, -0.2f, Palette.AirDark);
                        b.Box(new Vector3(0, 0.25f, -0.6f), new Vector3(0.04f, 0.4f, 0.35f), teamCol);
                        b.Sphere(new Vector3(0, 0, 0.8f), new Vector3(0.22f, 0.22f, 0.3f), Palette.MedicRed);
                    });
                    var prop = Part(rig.Body, "Prop", new Vector3(0, 0, -0.85f), "kamikaze_prop", b => b.Box(Vector3.zero, new Vector3(0.6f, 0.06f, 0.02f), Palette.Black), false);
                    spinners.Add(prop); axes.Add(Vector3.forward); speeds.Add(3000f);
                    rig.Muzzle = Node(rig.Body, "Muzzle", new Vector3(0, 0, 0.8f));
                    break;
                }
                case "air_attackheli":
                case "air_cargoheli":
                {
                    bool cargo = v == "air_cargoheli";
                    float L = cargo ? 12f : 10f;
                    Part(rig.Body, "Fuselage", Vector3.zero, k + "_fus", b =>
                    {
                        if (cargo)
                        {
                            b.Box(new Vector3(0, 0, 0), new Vector3(2.4f, 2.4f, 9f), Palette.TankOlive);
                            b.Sphere(new Vector3(0, 0.1f, 4.4f), new Vector3(2.4f, 2.2f, 2.2f), Palette.TankOlive);
                            b.Box(new Vector3(0, 1.6f, -4.2f), new Vector3(1.2f, 1.6f, 1.6f), Palette.TankOlive);
                            b.Box(new Vector3(0, 1.4f, 3.6f), new Vector3(1.0f, 0.8f, 1.4f), Palette.TankOlive);
                            b.Box(new Vector3(0, 0.4f, 5.1f), new Vector3(1.8f, 0.7f, 0.2f), Palette.Canopy, new Vector3(-20, 0, 0));
                            for (int i = -1; i <= 1; i += 2) b.Box(new Vector3(i * 1.25f, -0.6f, 0), new Vector3(0.6f, 0.6f, 4f), Palette.TankOlive);
                            b.Box(new Vector3(1.21f, 0.3f, 0), new Vector3(0.02f, 0.6f, 1.2f), teamCol);
                            b.Box(new Vector3(-1.21f, 0.3f, 0), new Vector3(0.02f, 0.6f, 1.2f), teamCol);
                        }
                        else
                        {
                            b.Wedge(new Vector3(0, 0, 1f), new Vector3(1.3f, 1.6f, 5f), 0.7f, 0.8f, -0.05f, Palette.TankOlive);
                            b.Box(new Vector3(0, 0.6f, 2.4f), new Vector3(0.9f, 0.7f, 1.6f), Palette.Canopy, new Vector3(-12, 0, 0));
                            b.Tube(new Vector3(0, 0.2f, -2.6f), 0.35f, 5f, Palette.TankOlive, 8, default(Vector3), 0.18f);
                            b.Box(new Vector3(0, 0.9f, -5f), new Vector3(0.12f, 1.4f, 0.9f), Palette.TankOlive);
                            b.Box(new Vector3(0, 0.2f, 0.6f), new Vector3(3.4f, 0.12f, 0.8f), Palette.TankOlive);
                            for (int i = -1; i <= 1; i += 2) b.Tube(new Vector3(i * 1.5f, -0.1f, 0.7f), 0.22f, 1.4f, Palette.Gunmetal);
                            b.Tube(new Vector3(0, -0.75f, 3.1f), 0.06f, 1.2f, Palette.Gunmetal);
                            b.Box(new Vector3(0.66f, 0.1f, 0f), new Vector3(0.02f, 0.5f, 1f), teamCol);
                            b.Box(new Vector3(-0.66f, 0.1f, 0f), new Vector3(0.02f, 0.5f, 1f), teamCol);
                            for (int i = -1; i <= 1; i += 2) b.Box(new Vector3(i * 0.7f, -1.0f, 1f), new Vector3(0.08f, 0.08f, 3.2f), Palette.Gunmetal);
                        }
                    });
                    BuildFn rotor = b =>
                    {
                        b.Box(Vector3.zero, new Vector3(cargo ? 11f : 11.5f, 0.05f, 0.35f), Palette.Black);
                        b.Box(Vector3.zero, new Vector3(0.35f, 0.05f, cargo ? 11f : 11.5f), Palette.Black);
                        b.Cylinder(new Vector3(0, -0.2f, 0), 0.2f, 0.5f, Palette.Gunmetal, 8);
                    };
                    if (cargo)
                    {
                        spinners.Add(Part(rig.Body, "RotorF", new Vector3(0, 2.0f, 3.6f), "rotor_cargo", rotor, false)); axes.Add(Vector3.up); speeds.Add(900f);
                        spinners.Add(Part(rig.Body, "RotorR", new Vector3(0, 2.6f, -4.2f), "rotor_cargo", rotor, false)); axes.Add(Vector3.up); speeds.Add(-900f);
                    }
                    else
                    {
                        spinners.Add(Part(rig.Body, "Rotor", new Vector3(0, 1.25f, 0.8f), "rotor_heli", rotor, false)); axes.Add(Vector3.up); speeds.Add(1000f);
                        spinners.Add(Part(rig.Body, "Tail", new Vector3(0.12f, 1.0f, -5.2f), "rotor_tail", bb => bb.Box(Vector3.zero, new Vector3(0.04f, 2f, 0.2f), Palette.Black), false)); axes.Add(Vector3.right); speeds.Add(2200f);
                    }
                    rig.Muzzle = Node(rig.Body, "Muzzle", new Vector3(0, -0.6f, cargo ? 5f : 3.8f));
                    rig.Height = 3f;
                    break;
                }
                case "air_osprey":
                {
                    Part(rig.Body, "Fuselage", Vector3.zero, k + "_fus", b =>
                    {
                        b.Box(Vector3.zero, new Vector3(2.4f, 2.2f, 10f), Palette.AirGrey);
                        b.Sphere(new Vector3(0, 0, 5f), new Vector3(2.4f, 2.1f, 2.6f), Palette.AirGrey);
                        b.Box(new Vector3(0, 0.5f, 5.6f), new Vector3(1.6f, 0.6f, 0.2f), Palette.Canopy, new Vector3(-25, 0, 0));
                        b.Box(new Vector3(0, 1.25f, 0.5f), new Vector3(13f, 0.25f, 1.8f), Palette.AirGrey);
                        b.Box(new Vector3(0, 1.6f, -5.2f), new Vector3(4.5f, 0.15f, 1f), Palette.AirGrey);
                        b.Box(new Vector3(2f, 2.2f, -5.2f), new Vector3(0.12f, 1.6f, 1f), Palette.AirGrey);
                        b.Box(new Vector3(-2f, 2.2f, -5.2f), new Vector3(0.12f, 1.6f, 1f), Palette.AirGrey);
                        for (int i = -1; i <= 1; i += 2) b.Tube(new Vector3(i * 6.4f, 1.3f, 0.6f), 0.55f, 3.2f, Palette.AirDark);
                        b.Box(new Vector3(1.22f, 0f, 1f), new Vector3(0.02f, 0.6f, 1.4f), teamCol);
                        b.Box(new Vector3(-1.22f, 0f, 1f), new Vector3(0.02f, 0.6f, 1.4f), teamCol);
                        b.Tube(new Vector3(-1.3f, -0.3f, 1.5f), 0.08f, 1.4f, Palette.Gunmetal, 6, new Vector3(0, -80, 0));
                    });
                    BuildFn prop = b => { for (int i = 0; i < 3; i++) b.Box(Vector3.zero, new Vector3(5.6f, 0.05f, 0.3f), Palette.Black, new Vector3(0, i * 60f, 0)); };
                    spinners.Add(Part(rig.Body, "PropL", new Vector3(-6.4f, 3.0f, 0.6f), "osprey_prop", prop, false)); axes.Add(Vector3.up); speeds.Add(900f);
                    spinners.Add(Part(rig.Body, "PropR", new Vector3(6.4f, 3.0f, 0.6f), "osprey_prop", prop, false)); axes.Add(Vector3.up); speeds.Add(-900f);
                    rig.Muzzle = Node(rig.Body, "Muzzle", new Vector3(-1.5f, -0.3f, 1.5f));
                    rig.Height = 3f;
                    break;
                }
                case "missile_v2":
                {
                    Part(rig.Body, "Missile", Vector3.zero, "v2_" + team, b =>
                    {
                        b.Tube(Vector3.zero, 0.8f, 10f, Palette.AirGrey, 12, default(Vector3), 0.8f);
                        b.Tube(new Vector3(0, 0, 6.2f), 0.8f, 2.6f, Palette.Black, 12, default(Vector3), 0.05f);
                        for (int i = 0; i < 4; i++) b.Box(new Vector3(0, 0, -4.4f), new Vector3(0.1f, 3.2f, 1.6f), Palette.Black, new Vector3(0, 0, i * 90f));
                    });
                    rig.Muzzle = Node(rig.Body, "Muzzle", new Vector3(0, 0, 7f));
                    break;
                }
                default: // fixed-wing: fighter, aa jet, bomber, supply/cargo planes
                {
                    bool big = v == "air_bomber" || v == "air_cargoplane" || v == "air_supplydrop";
                    float L = big ? 20f : 13f, span = big ? 28f : 10f;
                    int body = v == "air_aajet" || v == "air_fighter" ? Palette.AirGrey : Palette.TankOlive;
                    Part(rig.Body, "Airframe", Vector3.zero, k + "_air", b =>
                    {
                        b.Tube(Vector3.zero, big ? 1.4f : 0.7f, L, body, 10, default(Vector3), big ? 1.2f : 0.45f);
                        b.Sphere(new Vector3(0, 0, L * 0.5f), new Vector3(big ? 2.6f : 1.3f, big ? 2.6f : 1.3f, big ? 3f : 2.6f), body);
                        b.Sphere(new Vector3(0, big ? 1f : 0.55f, L * 0.32f), new Vector3(big ? 1.4f : 0.7f, big ? 0.8f : 0.6f, big ? 2f : 2.2f), Palette.Canopy);
                        b.Wedge(new Vector3(0, 0, big ? 0f : -1f), new Vector3(span, 0.2f, big ? 4f : 4.5f), 0.4f, big ? 0.6f : 0.25f, big ? 0f : -0.3f, body);
                        b.Wedge(new Vector3(0, 0.3f, -L * 0.45f), new Vector3(span * 0.35f, 0.15f, 2.4f), 0.5f, 0.5f, -0.3f, body);
                        b.Box(new Vector3(0, big ? 2.5f : 1.4f, -L * 0.42f), new Vector3(0.2f, big ? 4f : 2.2f, 2.6f), body, new Vector3(-15f, 0, 0));
                        b.Box(new Vector3(span * 0.38f, 0.12f, big ? 0f : -1.2f), new Vector3(1.2f, 0.02f, 1.2f), teamCol);
                        b.Box(new Vector3(-span * 0.38f, 0.12f, big ? 0f : -1.2f), new Vector3(1.2f, 0.02f, 1.2f), teamCol);
                        if (big)
                            for (int i = -2; i <= 2; i++) { if (i == 0) continue; b.Tube(new Vector3(i * span * 0.18f, -0.5f, 1.2f), 0.6f, 3f, Palette.AirDark); }
                        else for (int i = -1; i <= 1; i += 2) b.Tube(new Vector3(i * 2.6f, -0.35f, -0.5f), 0.18f, 2.6f, Palette.Gunmetal);
                    });
                    if (big)
                    {
                        for (int i = -2; i <= 2; i++)
                        {
                            if (i == 0) continue;
                            spinners.Add(Part(rig.Body, "Prop", new Vector3(i * span * 0.18f, -0.5f, 2.8f), "plane_prop", b => { b.Box(Vector3.zero, new Vector3(3.2f, 0.25f, 0.05f), Palette.Black); b.Box(Vector3.zero, new Vector3(0.25f, 3.2f, 0.05f), Palette.Black); }, false));
                            axes.Add(Vector3.forward); speeds.Add(1800f);
                        }
                    }
                    rig.Muzzle = Node(rig.Body, "Muzzle", new Vector3(0, -0.4f, L * 0.5f));
                    rig.Height = 3f;
                    break;
                }
            }
            rig.Spinners = spinners.ToArray();
            rig.SpinAxes = axes.ToArray();
            rig.SpinSpeeds = speeds.ToArray();
        }

        // ------------------------------------------------------------------ structures

        private static void BuildStructure(UnitRig rig, string v, Team team)
        {
            rig.Kind = RigKind.Structure;
            string k = v + "_" + team;
            int teamCol = Palette.Team(team);
            int flag = Palette.Flag(team);
            rig.Body = Node(rig.transform, "Body", Vector3.zero);
            var spinners = new List<Transform>();
            var axes = new List<Vector3>();
            var speeds = new List<float>();
            rig.Height = 3f;

            BuildFn flagPole = b =>
            {
                b.Box(new Vector3(0, 2f, 0), new Vector3(0.08f, 4f, 0.08f), Palette.Gunmetal);
                b.Plane(new Vector3(0.6f, 3.6f, 0), new Vector2(0.8f, 1.2f), flag, new Vector3(0, 0, 90));
            };

            switch (v)
            {
                case "struct_sandbag":
                    Part(rig.Body, "Base", Vector3.zero, k, b =>
                    {
                        for (int i = 0; i < 14; i++)
                        {
                            float a = (i / 14f * 260f - 130f) * Mathf.Deg2Rad;
                            for (int row = 0; row < 3; row++)
                                b.Box(new Vector3(Mathf.Sin(a) * 3.2f, 0.18f + row * 0.32f, Mathf.Cos(a) * 3.2f), new Vector3(0.9f, 0.32f, 0.5f), Palette.Sandbag, new Vector3(0, a * Mathf.Rad2Deg + (row % 2) * 6f, 0));
                        }
                        b.Box(new Vector3(0, 0.03f, 0), new Vector3(5f, 0.06f, 5f), Palette.Mud);
                        b.Box(new Vector3(-2.6f, 0.5f, -2.2f), new Vector3(0.6f, 0.4f, 0.4f), teamCol);
                    });
                    rig.Height = 1.5f;
                    break;
                case "struct_mgbunker":
                case "struct_pillbox":
                {
                    bool pill = v == "struct_pillbox";
                    Part(rig.Body, "Base", Vector3.zero, k, b =>
                    {
                        if (pill)
                        {
                            b.Cylinder(new Vector3(0, 1.3f, 0), 4.4f, 2.6f, Palette.Concrete, 6, default(Vector3), 3.9f);
                            b.Cylinder(new Vector3(0, 2.75f, 0), 3.9f, 0.3f, Palette.ConcreteDark, 6, default(Vector3), 3.4f);
                            for (int i = 0; i < 6; i++)
                            {
                                float a = (i * 60f + 30f) * Mathf.Deg2Rad;
                                b.Box(new Vector3(Mathf.Sin(a) * 4.0f, 1.7f, Mathf.Cos(a) * 4.0f), new Vector3(1.2f, 0.25f, 0.3f), Palette.Black, new Vector3(0, a * Mathf.Rad2Deg, 0));
                            }
                        }
                        else
                        {
                            b.Wedge(new Vector3(0, 1.0f, 0), new Vector3(6f, 2f, 5f), 0.85f, 0.85f, 0f, Palette.Concrete);
                            b.Box(new Vector3(0, 1.3f, 2.2f), new Vector3(2.6f, 0.3f, 0.7f), Palette.Black);
                            for (int i = -3; i <= 3; i++) b.Box(new Vector3(i * 0.95f, 0.25f, 3.1f), new Vector3(0.9f, 0.5f, 0.5f), Palette.Sandbag);
                        }
                        b.Box(new Vector3(0, pill ? 3.0f : 2.05f, 0), new Vector3(1.0f, 0.04f, 1.0f), teamCol);
                    });
                    rig.Turret = Part(rig.Body, "Turret", new Vector3(0, pill ? 1.7f : 1.3f, pill ? 0f : 1.6f), "bunker_gun", b =>
                    {
                        b.Box(new Vector3(0, 0, 0.2f), new Vector3(0.3f, 0.3f, 0.6f), Palette.Gunmetal);
                    });
                    rig.Barrel = Part(rig.Turret, "Barrel", new Vector3(0, 0, 0.5f), "bunker_barrel", b => b.Tube(new Vector3(0, 0, 0.7f), 0.06f, 1.4f, Palette.Gunmetal));
                    rig.Muzzle = Node(rig.Barrel, "Muzzle", new Vector3(0, 0, pill ? 3.6f : 1.5f));
                    rig.Height = pill ? 3.2f : 2.4f;
                    break;
                }
                case "struct_atturret":
                    Part(rig.Body, "Base", Vector3.zero, k, b =>
                    {
                        b.Cylinder(new Vector3(0, 0.4f, 0), 2.8f, 0.8f, Palette.ConcreteDark, 10);
                        for (int i = 0; i < 10; i++) { float a = i / 10f * Mathf.PI * 2f; b.Box(new Vector3(Mathf.Sin(a) * 3.1f, 0.4f, Mathf.Cos(a) * 3.1f), new Vector3(1.6f, 0.8f, 0.5f), Palette.Sandbag, new Vector3(0, a * Mathf.Rad2Deg, 0)); }
                        b.Box(new Vector3(-2.2f, 0.9f, -2.2f), new Vector3(0.5f, 0.3f, 0.5f), teamCol);
                    });
                    rig.Turret = Part(rig.Body, "Turret", new Vector3(0, 0.9f, 0), "at_turret_" + team, b =>
                    {
                        b.Box(new Vector3(0, 0.7f, 0.3f), new Vector3(2.4f, 1.4f, 0.12f), Palette.TankGrey, new Vector3(-10, 0, 0));
                        b.Box(new Vector3(0, 0.3f, -0.4f), new Vector3(0.8f, 0.6f, 1.6f), Palette.Gunmetal);
                        b.Box(new Vector3(0.8f, 0.2f, -1.2f), new Vector3(0.12f, 0.12f, 2.2f), Palette.Gunmetal, new Vector3(0, 20, 0));
                        b.Box(new Vector3(-0.8f, 0.2f, -1.2f), new Vector3(0.12f, 0.12f, 2.2f), Palette.Gunmetal, new Vector3(0, -20, 0));
                        b.Box(new Vector3(0, 1.2f, 0.36f), new Vector3(0.6f, 0.3f, 0.02f), teamCol);
                    });
                    rig.Barrel = Part(rig.Turret, "Barrel", new Vector3(0, 0.6f, 0.3f), "at_barrel", b => { b.Tube(new Vector3(0, 0, 1.6f), 0.09f, 3.2f, Palette.Gunmetal); b.Tube(new Vector3(0, 0, 3.2f), 0.13f, 0.3f, Palette.Gunmetal); });
                    rig.Muzzle = Node(rig.Barrel, "Muzzle", new Vector3(0, 0, 3.4f));
                    rig.Height = 2.4f;
                    break;
                case "struct_aabattery":
                    Part(rig.Body, "Base", Vector3.zero, k, b =>
                    {
                        b.Box(new Vector3(0, 0.3f, 0), new Vector3(5f, 0.6f, 5f), Palette.ConcreteDark);
                        b.Box(new Vector3(2f, 0.9f, -2f), new Vector3(1.2f, 0.8f, 1.6f), Palette.TankOlive);
                        b.Box(new Vector3(-2.2f, 0.7f, 2.2f), new Vector3(0.4f, 0.4f, 0.4f), teamCol);
                    });
                    rig.Turret = Part(rig.Body, "Turret", new Vector3(0, 0.6f, 0), "sam_turret", b =>
                    {
                        b.Cylinder(new Vector3(0, 0.3f, 0), 0.9f, 0.6f, Palette.TankOlive, 10);
                    });
                    rig.Barrel = Part(rig.Turret, "Barrel", new Vector3(0, 1.0f, 0), "sam_rack_" + team, b =>
                    {
                        b.Box(new Vector3(0, 0, 0), new Vector3(2.2f, 0.2f, 0.4f), Palette.Gunmetal);
                        for (int i = 0; i < 4; i++) b.Tube(new Vector3(-0.8f + i * 0.53f, 0.35f, 0.4f), 0.2f, 3.2f, Palette.AirGrey, 8);
                        b.Box(new Vector3(0, 0.35f, 2.05f), new Vector3(2.2f, 0.45f, 0.05f), teamCol);
                    });
                    rig.Barrel.localRotation = Quaternion.Euler(-35f, 0, 0);
                    rig.Muzzle = Node(rig.Barrel, "Muzzle", new Vector3(0, 0.4f, 2.2f));
                    rig.Height = 3f;
                    break;
                case "struct_radar":
                case "struct_satellite":
                {
                    bool sat = v == "struct_satellite";
                    Part(rig.Body, "Base", Vector3.zero, k, b =>
                    {
                        b.Box(new Vector3(0, 1.2f, -1.6f), new Vector3(3.6f, 2.4f, 2.4f), sat ? Palette.Concrete : Palette.TankOlive);
                        b.Box(new Vector3(0, 2.45f, -1.6f), new Vector3(3.8f, 0.1f, 2.6f), Palette.Roof);
                        b.Box(new Vector3(0, 1.4f, -0.39f), new Vector3(0.8f, 0.6f, 0.02f), Palette.Glass);
                        float h = sat ? 5f : 6.5f;
                        for (int i = 0; i < 4; i++)
                        {
                            float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                            b.Box(new Vector3(Mathf.Sin(a) * 0.9f, h * 0.5f, 1.4f + Mathf.Cos(a) * 0.9f), new Vector3(0.12f, h, 0.12f), Palette.Gunmetal, new Vector3(Mathf.Cos(a) * -6f, 0, Mathf.Sin(a) * 6f));
                        }
                        for (int j = 1; j < 4; j++) b.Box(new Vector3(0, j * h / 4f, 1.4f), new Vector3(1.6f, 0.08f, 1.6f), Palette.Gunmetal);
                        b.Box(new Vector3(-1.9f, 2.7f, -1.6f), new Vector3(0.6f, 0.4f, 0.04f), teamCol);
                    });
                    var dish = Part(rig.Body, "Dish", new Vector3(0, sat ? 5.6f : 7f, 1.4f), "dish_" + (sat ? "sat" : "radar"), b =>
                    {
                        if (sat)
                        {
                            b.Sphere(new Vector3(0, 0.8f, 0.2f), new Vector3(5f, 1.6f, 5f), Palette.MetalLight, 6, 10, true);
                            b.Box(new Vector3(0, 1.6f, 0), new Vector3(0.1f, 1.8f, 0.1f), Palette.Gunmetal);
                        }
                        else
                        {
                            b.Box(new Vector3(0, 0.6f, 0), new Vector3(4.2f, 1.4f, 0.12f), Palette.MetalLight, new Vector3(-10, 0, 0));
                            b.Box(new Vector3(0, 0.1f, -0.3f), new Vector3(0.3f, 0.6f, 0.6f), Palette.Gunmetal);
                        }
                    });
                    if (sat) dish.localRotation = Quaternion.Euler(-35f, 0, 0);
                    spinners.Add(dish); axes.Add(Vector3.up); speeds.Add(sat ? 6f : 60f);
                    rig.Height = sat ? 7f : 8.5f;
                    break;
                }
                case "struct_commandpost":
                    Part(rig.Body, "Base", Vector3.zero, k, b =>
                    {
                        b.Wedge(new Vector3(0, 1.2f, 0), new Vector3(6f, 2.4f, 5f), 0.15f, 1f, 0f, Palette.Canvas);
                        b.Box(new Vector3(0, 0.8f, 2.51f), new Vector3(1.4f, 1.6f, 0.02f), Palette.Black);
                        for (int i = -3; i <= 3; i++) b.Box(new Vector3(i * 1.0f, 0.3f, -3.2f), new Vector3(0.95f, 0.6f, 0.5f), Palette.Sandbag);
                        b.Box(new Vector3(3.6f, 0.5f, 1f), new Vector3(0.8f, 1f, 1.2f), Palette.Crate);
                        b.Box(new Vector3(-3.4f, 3.2f, -1.2f), new Vector3(0.06f, 6.4f, 0.06f), Palette.Gunmetal);
                        b.Box(new Vector3(-3.4f, 6.2f, -1.2f), new Vector3(1.6f, 0.06f, 0.06f), Palette.Gunmetal);
                    });
                    Part(rig.Body, "Flag", new Vector3(2.8f, 0, -2f), "flagpole_" + team, flagPole);
                    rig.Turret = Node(rig.Body, "Turret", new Vector3(0, 1.4f, 2.4f));
                    rig.Muzzle = Node(rig.Turret, "Muzzle", new Vector3(0, 0, 0.5f));
                    rig.Height = 3f;
                    break;
                case "struct_barbedwire":
                    Part(rig.Body, "Base", Vector3.zero, k, b =>
                    {
                        for (int row = 0; row < 3; row++)
                            for (int i = 0; i < 6; i++)
                            {
                                Vector3 p = new Vector3(-5f + i * 2f, 0.6f, -2.5f + row * 2.5f);
                                b.Box(p, new Vector3(0.08f, 1.4f, 0.08f), Palette.Wood, new Vector3(0, 0, 35));
                                b.Box(p, new Vector3(0.08f, 1.4f, 0.08f), Palette.Wood, new Vector3(0, 0, -35));
                            }
                        for (int row = 0; row < 3; row++)
                            for (int w = 0; w < 3; w++)
                                b.Box(new Vector3(0, 0.3f + w * 0.3f, -2.5f + row * 2.5f), new Vector3(11f, 0.025f, 0.025f), Palette.SteelDark);
                        b.Box(new Vector3(5.6f, 0.5f, 0f), new Vector3(0.3f, 0.3f, 0.3f), teamCol);
                    });
                    rig.Height = 1.2f;
                    break;
                case "struct_tesla":
                    Part(rig.Body, "Base", Vector3.zero, k, b =>
                    {
                        b.Cylinder(new Vector3(0, 0.5f, 0), 2.4f, 1f, Palette.ConcreteDark, 8, default(Vector3), 2.0f);
                        b.Cylinder(new Vector3(0, 3.5f, 0), 0.7f, 5f, Palette.Gunmetal, 8, default(Vector3), 0.45f);
                        for (int i = 0; i < 8; i++) b.Cylinder(new Vector3(0, 1.6f + i * 0.55f, 0), 0.95f - i * 0.05f, 0.12f, Palette.Rust, 10);
                        b.Box(new Vector3(1.8f, 1.1f, 0f), new Vector3(0.4f, 0.4f, 0.4f), teamCol);
                    });
                    rig.Extra = Part(rig.Body, "Coil", new Vector3(0, 6.6f, 0), "tesla_orb", b => b.Sphere(Vector3.zero, new Vector3(1.6f, 1.6f, 1.6f), Palette.TeslaGlow), false);
                    var glow = rig.Extra.GetComponent<MeshRenderer>();
                    glow.sharedMaterial = TeslaMaterial;
                    rig.Turret = Node(rig.Body, "Turret", new Vector3(0, 6.6f, 0));
                    rig.Muzzle = rig.Turret;
                    rig.Height = 7.5f;
                    break;
                case "struct_extractor":
                    Part(rig.Body, "Base", Vector3.zero, k, b =>
                    {
                        b.Box(new Vector3(0, 0.2f, 0), new Vector3(6f, 0.4f, 5f), Palette.ConcreteDark);
                        b.Cylinder(new Vector3(-1.8f, 1.6f, -1.2f), 1.2f, 2.8f, Palette.FuelDrum, 12);
                        b.Box(new Vector3(1.2f, 1.6f, 0f), new Vector3(0.4f, 3.2f, 0.4f), Palette.Hazard);
                        b.Box(new Vector3(1.2f, 0.8f, 1.6f), new Vector3(1.4f, 1.2f, 1.4f), Palette.TankOlive);
                        b.Box(new Vector3(-1.8f, 3.1f, -1.2f), new Vector3(0.6f, 0.06f, 0.6f), teamCol);
                    });
                    rig.Extra = Part(rig.Body, "PumpBeam", new Vector3(1.2f, 3.3f, 0f), "pump_beam", b =>
                    {
                        b.Box(new Vector3(0, 0, 0), new Vector3(0.3f, 0.35f, 4.4f), Palette.Hazard);
                        b.Box(new Vector3(0, -0.2f, 2.2f), new Vector3(0.5f, 0.9f, 0.6f), Palette.SteelDark);
                    });
                    rig.Height = 4.5f;
                    break;
                case "struct_mortarpit":
                    Part(rig.Body, "Base", Vector3.zero, k, b =>
                    {
                        for (int i = 0; i < 12; i++) { float a = i / 12f * Mathf.PI * 2f; b.Box(new Vector3(Mathf.Sin(a) * 3.3f, 0.35f, Mathf.Cos(a) * 3.3f), new Vector3(1.8f, 0.7f, 0.55f), Palette.Sandbag, new Vector3(0, a * Mathf.Rad2Deg, 0)); }
                        b.Box(new Vector3(0, 0.03f, 0), new Vector3(6f, 0.06f, 6f), Palette.Mud);
                        for (int i = 0; i < 6; i++) b.Cylinder(new Vector3(-2f + i * 0.3f, 0.3f, -2f), 0.1f, 0.6f, Palette.TankOlive, 6);
                        b.Box(new Vector3(2.4f, 0.9f, -2.4f), new Vector3(0.4f, 0.3f, 0.4f), teamCol);
                    });
                    rig.Turret = Part(rig.Body, "Turret", new Vector3(0, 0.2f, 0), "mortarpit_turret", b => b.Box(Vector3.zero, new Vector3(0.9f, 0.2f, 0.9f), Palette.Gunmetal));
                    rig.Barrel = Part(rig.Turret, "Barrel", new Vector3(0, 0.3f, 0), "mortarpit_tube", b => b.Tube(new Vector3(0, 0, 1.0f), 0.17f, 2.2f, Palette.Gunmetal, 10));
                    rig.Barrel.localRotation = Quaternion.Euler(-55f, 0, 0);
                    rig.Muzzle = Node(rig.Barrel, "Muzzle", new Vector3(0, 0, 2.1f));
                    rig.Height = 2.2f;
                    break;
                case "hq":
                    Part(rig.Body, "Keep", Vector3.zero, k, b =>
                    {
                        b.Box(new Vector3(0, 0.6f, 0), new Vector3(30f, 1.2f, 30f), Palette.ConcreteDark);
                        b.Wedge(new Vector3(0, 5.2f, 0), new Vector3(20f, 8f, 18f), 0.9f, 0.9f, 0f, Palette.Concrete);
                        b.Box(new Vector3(0, 9.5f, 0), new Vector3(14f, 1f, 12f), Palette.ConcreteDark);
                        b.Box(new Vector3(0, 11.5f, -1f), new Vector3(8f, 3.5f, 7f), Palette.Concrete);
                        for (int i = -2; i <= 2; i++) b.Box(new Vector3(i * 3.4f, 5.5f, 8.15f), new Vector3(1.2f, 0.6f, 0.2f), Palette.Black);
                        for (int i = -2; i <= 2; i++) b.Box(new Vector3(i * 3.4f, 5.5f, -8.15f), new Vector3(1.2f, 0.6f, 0.2f), Palette.Black);
                        b.Box(new Vector3(0, 3f, 9.05f), new Vector3(4.2f, 4.6f, 0.4f), Palette.SteelDark);
                        for (int c = 0; c < 4; c++)
                        {
                            float x = (c % 2 == 0 ? -1 : 1) * 11f, z = (c < 2 ? -1 : 1) * 10f;
                            b.Cylinder(new Vector3(x, 6f, z), 2.4f, 12f, Palette.ConcreteDark, 8, default(Vector3), 2.1f);
                            b.Cylinder(new Vector3(x, 12.3f, z), 2.8f, 0.8f, Palette.Concrete, 8);
                            b.Cylinder(new Vector3(x, 10f, z), 2.42f, 0.7f, teamCol, 8);
                        }
                        // banners in team colour
                        b.Plane(new Vector3(0, 7f, 8.3f), new Vector2(3f, 5f), flag, new Vector3(-90, 0, 0));
                        b.Plane(new Vector3(0, 7f, -8.3f), new Vector2(3f, 5f), flag, new Vector3(90, 0, 0));
                        b.Box(new Vector3(0, 16f, -1f), new Vector3(0.25f, 6f, 0.25f), Palette.Gunmetal);
                        b.Plane(new Vector3(1.6f, 18.2f, -1f), new Vector2(3f, 1.9f), flag, new Vector3(0, 0, 90));
                        // sandbags & crates at the gate
                        for (int i = -4; i <= 4; i++) b.Box(new Vector3(i * 1.1f, 0.5f, 12.5f), new Vector3(1f, 0.6f, 0.6f), Palette.Sandbag);
                    });
                    rig.Turret = Part(rig.Body, "Turret", new Vector3(0, 13.4f, -1f), "hq_gun", b => { b.Box(new Vector3(0, 0.6f, 0), new Vector3(3f, 1.2f, 3.4f), Palette.TankGrey); });
                    rig.Barrel = Part(rig.Turret, "Barrel", new Vector3(0, 0.6f, 1.7f), "hq_barrel", b => { b.Tube(new Vector3(0.4f, 0, 2f), 0.15f, 4f, Palette.Gunmetal); b.Tube(new Vector3(-0.4f, 0, 2f), 0.15f, 4f, Palette.Gunmetal); });
                    rig.Muzzle = Node(rig.Barrel, "Muzzle", new Vector3(0, 0, 4.2f));
                    rig.Height = 20f;
                    break;
                default:
                    Part(rig.Body, "Base", Vector3.zero, k, b => b.Box(new Vector3(0, 1f, 0), new Vector3(4f, 2f, 4f), Palette.Concrete));
                    break;
            }
            rig.Spinners = spinners.ToArray();
            rig.SpinAxes = axes.ToArray();
            rig.SpinSpeeds = speeds.ToArray();
            if (rig.Muzzle == null) rig.Muzzle = Node(rig.Body, "Muzzle", new Vector3(0, rig.Height * 0.6f, 0));
        }

        private static Material _tesla;
        private static Material TeslaMaterial
        {
            get
            {
                if (_tesla == null) _tesla = MaterialLibrary.CreateEmissive(new Color(0.4f, 0.75f, 1f), new Color(0.6f, 1.2f, 2.4f));
                return _tesla;
            }
        }
    }
}
