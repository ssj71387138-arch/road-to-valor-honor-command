using System;
using System.Collections;
using System.Reflection;
using RoadToValor.Core;
using UnityEngine;

namespace RoadToValor.Game
{
    /// <summary>
    /// Top-level game object: loads data, generates the battlefield, owns camera / lighting / profile,
    /// and switches between the lobby (with a live 3D demo battle) and real battles.
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        public static GameRoot Instance { get; private set; }

        public GameData Data;
        public WorldView World;
        public PlayerProfile Profile;
        public PortraitRenderer Portraits;
        public Camera MainCam;
        public RtsCamera CamRig;
        public Light Sun;
        public BattleController Battle;
        public BattleHud Hud;
        public LobbyUI Lobby;

        public bool Loading = true;
        public string LoadingText = "초기화 중...";
        public string Error;
        public bool InBattle;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private IEnumerator Start()
        {
            SetupCameraAndLighting();
            LoadingText = "데이터 로드 중...";
            yield return null;

            var roster = Resources.Load<TextAsset>("RoadToValor/Data/unit_roster");
            var balance = Resources.Load<TextAsset>("RoadToValor/Data/unit_balance");
            var config = Resources.Load<TextAsset>("RoadToValor/Data/game_config");
            var layout = Resources.Load<TextAsset>("RoadToValor/Data/map_layout");
            if (roster == null || balance == null || config == null || layout == null)
            {
                Error = "데이터 파일을 찾을 수 없습니다: Assets/RoadToValor/Resources/RoadToValor/Data/";
                yield break;
            }

            LoadingText = "1200m x 1200m 전장 생성 중 (지형·내비게이션·공격로)...";
            yield return null;
            try
            {
                Data = GameData.Load(roster.text, balance.text, config.text, layout.text);
                foreach (var w in Data.Units.Warnings) Debug.LogWarning("[RTV data] " + w);
            }
            catch (Exception e)
            {
                Error = "데이터/맵 생성 실패: " + e.Message;
                Debug.LogException(e);
                yield break;
            }

            World = new WorldView();
            yield return StartCoroutine(World.Build(Data.Map, transform));

            Profile = PlayerProfile.Load();
            if (Profile.deck == null || Profile.deck.Length != 8) Profile.deck = Data.Config.PlayerDefaultDeck;

            var pgo = new GameObject("Portraits");
            pgo.transform.SetParent(transform, false);
            Portraits = pgo.AddComponent<PortraitRenderer>();
            Portraits.Init(Data.Units);
            foreach (int id in Profile.deck) Portraits.Get(id);
            foreach (var d in Data.Units.Cards) Portraits.Get(d.Id);

            CamRig = MainCam.gameObject.GetComponent<RtsCamera>();
            if (CamRig == null) CamRig = MainCam.gameObject.AddComponent<RtsCamera>();
            CamRig.Init(MainCam, Data.Map);
            CamRig.EdgeScroll = Profile.edgeScroll;

            Battle = gameObject.AddComponent<BattleController>();
            Battle.Root = this;
            Hud = gameObject.AddComponent<BattleHud>();
            Hud.Root = this;
            Lobby = gameObject.AddComponent<LobbyUI>();
            Lobby.Root = this;

            Loading = false;
            EnterLobby();
        }

        private void SetupCameraAndLighting()
        {
            MainCam = Camera.main;
            if (MainCam == null)
            {
                var cgo = new GameObject("Main Camera") { tag = "MainCamera" };
                MainCam = cgo.AddComponent<Camera>();
                cgo.AddComponent<AudioListener>();
            }
            MainCam.cullingMask &= ~(1 << PortraitRenderer.Layer);
            Color fog = new Color(0.66f, 0.71f, 0.77f);
            MainCam.backgroundColor = fog;
            MainCam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            MainCam.transform.position = new Vector3(0, 200, -700);

            Sun = RenderSettings.sun;
            if (Sun == null)
            {
                var existing = GameObject.Find("Directional Light");
                if (existing != null) Sun = existing.GetComponent<Light>();
            }
            if (Sun == null)
            {
                var lgo = new GameObject("Sun");
                Sun = lgo.AddComponent<Light>();
                Sun.type = LightType.Directional;
            }
            Sun.transform.rotation = Quaternion.Euler(42f, -32f, 0f);
            Sun.color = new Color(1f, 0.95f, 0.88f);
            Sun.intensity = 1.15f;
            Sun.shadows = LightShadows.Soft;
            Sun.shadowStrength = 0.75f;
            RenderSettings.sun = Sun;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.00105f;
            RenderSettings.fogColor = fog;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.6f, 0.66f, 0.76f);
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.48f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.25f, 0.23f);
            var sh = new UnityEngine.Rendering.SphericalHarmonicsL2();
            sh.AddAmbientLight(new Color(0.48f, 0.51f, 0.56f));
            RenderSettings.ambientProbe = sh;

            // Long shadow distance for a high RTS camera (URP keeps it on the pipeline asset).
            QualitySettings.shadowDistance = 380f;
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (rp != null)
            {
                try
                {
                    var prop = rp.GetType().GetProperty("shadowDistance", BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null && prop.CanWrite) prop.SetValue(rp, 380f, null);
                }
                catch (Exception e) { Debug.Log("[RTV] could not set URP shadow distance: " + e.Message); }
            }
        }

        // ------------------------------------------------------------------ state changes

        public void EnterLobby()
        {
            InBattle = false;
            var setup = new BattleSetup
            {
                PlayerAi = true,
                EnemyAi = true,
                Waves = true,
                Difficulty = "normal",
                StartingSquads = new[] { 1, 2 },
                Seed = UnityEngine.Random.Range(1, 99999),
            };
            Battle.Begin(Data, setup, true);
            Hud.Visible = false;
            Hud.MenuOpen = false;
            Lobby.Visible = true;
            CamRig.Orbit = true;
            CamRig.InputEnabled = false;
            CamRig.SetFocus(new Vector3(0f, 14f, -10f), true);
            CamRig.SetZoom(0.55f, true);
        }

        public void StartBattle()
        {
            var p = Profile;
            var setup = new BattleSetup
            {
                PlayerDeck = p.deck,
                Difficulty = p.difficulty,
                EnemyAi = true,
                PlayerAi = false,
                Waves = true,
                StartingSquads = new[] { 1, 1 },
                Seed = UnityEngine.Random.Range(1, 99999),
            };
            // Research bonuses (proposed values in game_config.json "research").
            var tracks = Data.Config.Research;
            for (int i = 0; i < tracks.Count && i < p.research.Length; i++)
            {
                float bonus = 1f + tracks[i].PerLevel * p.research[i];
                switch (tracks[i].Key)
                {
                    case "infantryHp": setup.PlayerInfantryHp = bonus; break;
                    case "armorHp": setup.PlayerVehicleHp = bonus; break;
                    case "attack": setup.PlayerAttack = bonus; break;
                    case "cpRegen": setup.PlayerCpRegen = bonus; break;
                }
            }
            Portraits.StopLive();
            Battle.Begin(Data, setup, false);
            InBattle = true;
            Lobby.Visible = false;
            Hud.Visible = true;
            Hud.MenuOpen = false;
            CamRig.Orbit = false;
            CamRig.Yaw = 0f;
            CamRig.InputEnabled = true;
            var hq = Data.Layout.GetHq(Team.Player);
            Vec2 f = hq.Position + new Vec2(0f, 70f);
            CamRig.SetFocus(f.ToV3(Data.Map.SampleHeight(f)), true);
            CamRig.SetZoom(0.3f, true);
        }

        public void ReturnToLobby()
        {
            if (Battle.Result != null && Battle.Result.victory) { Profile.supplyCrates++; Profile.Save(); }
            EnterLobby();
        }

        private void Update()
        {
            // Restart the background demo battle when it finishes.
            if (!Loading && !InBattle && Battle != null && Battle.Sim != null && Battle.Sim.IsOver && Battle.Sim.Time > 5f) EnterLobby();
        }

        private void OnGUI()
        {
            if (!Loading && Error == null) return;
            UiTheme.Begin();
            float W = UiTheme.W, H = UiTheme.H;
            UiTheme.Fill(new Rect(0, 0, W, H), new Color(0.04f, 0.05f, 0.06f, 1f));
            UiTheme.DrawText(new Rect(0, H * 0.36f, W, 60), "<b>ROAD TO VALOR</b>  <color=#e6bb55>HONOR COMMAND</color>", new GUIStyle(UiTheme.Title) { alignment = TextAnchor.MiddleCenter, fontSize = 46 });
            string status = Error ?? (World != null && !string.IsNullOrEmpty(World.Status) ? World.Status : LoadingText);
            UiTheme.DrawText(new Rect(0, H * 0.36f + 80, W, 30), status, UiTheme.LabelCenter, Error != null ? UiTheme.WarnRed : UiTheme.TextDim);
            if (Error == null)
            {
                float t = Mathf.Repeat(Time.unscaledTime * 0.6f, 1f);
                UiTheme.Bar(new Rect(W * 0.5f - 200, H * 0.36f + 124, 400, 6), t, UiTheme.MilBlue, new Color(1, 1, 1, 0.08f));
            }
        }
    }
}
