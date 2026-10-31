using System.Collections.Generic;
using RoadToValor.Core;
using UnityEngine;

namespace RoadToValor.Game
{
    /// <summary>
    /// In-battle HUD. Centre of the screen stays clear for the battlefield.
    /// Top: both HQ health bars, match time, zone counts, kill stats. Bottom: selected unit panel
    /// (portrait, HP, stats, ability button), 8 unit cards, CP gauge. Bottom-right: large minimap with zones,
    /// units, lanes and the camera frustum. In-world: capture gauges, health bars, placement preview, drag box.
    /// </summary>
    public sealed class BattleHud : MonoBehaviour
    {
        public GameRoot Root;
        public bool Visible = true;
        public bool MenuOpen;

        private readonly List<Rect> _uiRects = new List<Rect>(16);
        private readonly List<Rect> _frameRects = new List<Rect>(16);
        private Rect _minimapRect;
        private bool _minimapDrag;
        private readonly Vector3[] _corners = new Vector3[4];
        private float _fps;

        private BattleController B => Root.Battle;

        public bool IsOverUi(Vector2 screen)
        {
            if (!Visible) return false;
            if (MenuOpen || (B != null && B.Sim != null && B.Sim.IsOver)) return true;
            Vector2 g = UiTheme.ScreenToGui(screen);
            foreach (var r in _uiRects) if (r.Contains(g)) return true;
            return false;
        }

        private Rect Ui(Rect r) { _frameRects.Add(r); return r; }

        private void Update()
        {
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.05f);
            if (!Visible || B == null || B.Sim == null || B.IsDemo) return;
            if (GameInput.KeyDown(GameKey.Escape) && (B.Cmd == null || (B.Cmd.PlacingDefId < 0 && !B.Cmd.AttackMoveArmed && B.Cmd.LastEscapeFrame != Time.frameCount)))
            {
                MenuOpen = !MenuOpen;
                B.Paused = MenuOpen;
            }
        }

        private void OnGUI()
        {
            if (!Visible || B == null || B.Sim == null || B.IsDemo) return;
            UiTheme.Begin();
            var sim = B.Sim;
            var cam = Root.MainCam;
            if (Event.current.type == EventType.Repaint) _frameRects.Clear();

            if (Event.current.type == EventType.Repaint) DrawWorldOverlays(sim, cam);
            DrawTopBar(sim);
            DrawNotices();
            DrawBottomPanel(sim);
            DrawMinimap(sim);
            DrawFeedback();
            if (MenuOpen) DrawMenu();
            if (sim.IsOver) DrawResult(sim);

            if (Event.current.type == EventType.Repaint)
            {
                _uiRects.Clear();
                _uiRects.AddRange(_frameRects);
            }
        }

        // ------------------------------------------------------------------ world overlays

        private void DrawWorldOverlays(BattleSimulation sim, Camera cam)
        {
            float W = UiTheme.W, H = UiTheme.H;
            // Zones: id badge, name, capture gauge.
            foreach (var z in sim.Zones)
            {
                Vector3 wp = z.Def.Center.ToV3(sim.Map.SampleHeight(z.Def.Center) + 16f);
                Vector2 g = UiTheme.WorldToGui(cam, wp, out bool front);
                if (!front || g.x < -100 || g.y < -50 || g.x > W + 100 || g.y > H + 50) continue;
                Color oc = Conv.TeamColor(z.Owner);
                var badge = new Rect(g.x - 15, g.y - 15, 30, 26);
                UiTheme.Fill(badge, new Color(oc.r * 0.6f, oc.g * 0.6f, oc.b * 0.6f, 0.9f), 4f);
                UiTheme.Frame(badge, oc, 1.5f, 4f);
                UiTheme.DrawText(badge, z.Def.Id.ToString(), UiTheme.LabelCenterBold);
                UiTheme.DrawText(new Rect(g.x - 90, g.y + 11, 180, 20), z.Def.Name, UiTheme.LabelCenter, new Color(1f, 1f, 1f, 0.9f));
                bool capturing = z.Capturer != Team.Neutral || z.Contested || (z.Owner == Team.Neutral && z.Progress > 0.01f) || (z.Owner != Team.Neutral && z.Progress < 0.999f);
                if (capturing)
                {
                    var bar = new Rect(g.x - 55, g.y + 32, 110, 9);
                    Color fc = Conv.TeamColor(z.ControlSide == Team.Neutral ? z.Capturer : z.ControlSide);
                    UiTheme.Bar(bar, z.Progress, fc, new Color(0, 0, 0, 0.6f));
                    string st = z.Contested ? "<b>경합 중</b>" : (z.Capturer != Team.Neutral ? "점령 중 " + Mathf.RoundToInt(z.Progress * 100f) + "%" : Mathf.RoundToInt(z.Progress * 100f) + "%");
                    UiTheme.DrawText(new Rect(g.x - 70, g.y + 42, 140, 18), st, UiTheme.LabelCenter, z.Contested ? new Color(1f, 0.85f, 0.2f) : Color.white);
                }
            }

            // Health bars.
            int drawn = 0;
            float zoom = Root.CamRig.Zoom;
            foreach (var v in B.Views.Active)
            {
                if (!v.OnScreen || v.Unit == null || !v.Unit.Alive || drawn > 260) continue;
                var u = v.Unit;
                if (u.Team != B.PlayerTeam && u.Def.Ability == AbilityType.Stealth && !u.Revealed) continue;
                bool show = v.Selected || u.Hp < u.MaxHp - 0.5f || v == B.Cmd.Hovered || zoom < 0.25f || u.IsStructure || u.Constructing;
                if (!show) continue;
                Vector2 g = UiTheme.WorldToGui(cam, v.WorldPos + Vector3.up * (v.Height + 1.2f), out bool front);
                if (!front) continue;
                float w = u.Def.IsHQ ? 160f : (u.IsStructure ? 64f : (u.IsInfantry ? 26f : 46f));
                float h = u.Def.IsHQ ? 9f : (u.IsInfantry ? 4f : 6f);
                Color c = u.Team == B.PlayerTeam ? new Color(0.25f, 0.7f, 1f) : new Color(1f, 0.28f, 0.22f);
                UiTheme.Bar(new Rect(g.x - w * 0.5f, g.y, w, h), u.HpRatio, c, new Color(0, 0, 0, 0.65f));
                if (u.Constructing)
                {
                    UiTheme.Bar(new Rect(g.x - w * 0.5f, g.y + h + 2, w, 4), u.BuildProgress, UiTheme.Gold, new Color(0, 0, 0, 0.65f));
                    UiTheme.DrawText(new Rect(g.x - 50, g.y - 18, 100, 16), "건설 중", UiTheme.LabelCenter, UiTheme.Gold);
                }
                if (u.PassengerCount > 0) UiTheme.DrawText(new Rect(g.x + w * 0.5f + 2, g.y - 6, 40, 16), "+" + u.PassengerCount, UiTheme.LabelSmall, Color.white);
                drawn++;
            }

            // Hover tooltip.
            var hv = B.Cmd.Hovered;
            if (hv != null && hv.Unit != null && hv.Unit.Alive)
            {
                Vector2 m = UiTheme.ScreenToGui(GameInput.MousePosition);
                var u = hv.Unit;
                string t = "<b>" + u.Def.NameKo + "</b>  " + Mathf.CeilToInt(u.Hp) + "/" + Mathf.CeilToInt(u.MaxHp) + (u.Team != B.PlayerTeam ? "  <color=#ff6655>적군</color>" : "");
                var r = new Rect(m.x + 18, m.y + 8, 260, 24);
                UiTheme.Fill(r, new Color(0, 0, 0, 0.7f), 3f);
                UiTheme.DrawText(new Rect(r.x + 6, r.y, r.width - 8, r.height), t, UiTheme.LabelSmall);
            }

            // Drag selection box.
            if (B.Cmd.Dragging)
            {
                Vector2 a = UiTheme.ScreenToGui(B.Cmd.DragStart), b = UiTheme.ScreenToGui(B.Cmd.DragEnd);
                var r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                UiTheme.Fill(r, new Color(0.3f, 0.6f, 1f, 0.12f));
                UiTheme.Frame(r, new Color(0.4f, 0.75f, 1f, 0.9f), 1.5f);
            }

            // Placement preview.
            if (B.Cmd.PlacingDefId >= 0)
            {
                var def = sim.Units.Get(B.Cmd.PlacingDefId);
                Color c = B.Cmd.PlacementValid ? new Color(0.35f, 1f, 0.45f, 0.95f) : new Color(1f, 0.3f, 0.25f, 0.95f);
                float radius = def.IsStructure ? 7f : (def.IsSkill ? Mathf.Max(8f, def.AbRadius) : Mathf.Max(4f, 2f + def.Count));
                DrawWorldCircle(cam, B.Cmd.PlacementPoint, radius, c, sim.Map);
                Vector2 m = UiTheme.ScreenToGui(GameInput.MousePosition);
                string txt = "<b>" + def.NameKo + "</b>  CP " + def.CpCost + (B.Cmd.PlacementValid ? "  - 좌클릭 배치" : "  - " + B.Cmd.PlacementReason) + "   (우클릭/ESC 취소, Shift 연속)";
                var r = new Rect(m.x + 20, m.y + 10, 520, 26);
                UiTheme.Fill(r, new Color(0, 0, 0, 0.75f), 3f);
                UiTheme.DrawText(new Rect(r.x + 8, r.y, r.width - 10, r.height), txt, UiTheme.LabelSmall, c);
            }
            else if (B.Cmd.AttackMoveArmed)
            {
                Vector2 m = UiTheme.ScreenToGui(GameInput.MousePosition);
                UiTheme.DrawText(new Rect(m.x + 18, m.y + 6, 200, 22), "<b>공격 이동</b> - 좌클릭", UiTheme.LabelSmall, UiTheme.WarnRed);
            }
        }

        private static void DrawWorldCircle(Camera cam, Vector3 c, float radius, Color col, MapData map)
        {
            Vector2 prev = Vector2.zero;
            bool havePrev = false;
            for (int i = 0; i <= 32; i++)
            {
                float a = i / 32f * Mathf.PI * 2f;
                Vector3 p = c + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * radius;
                p.y = Mathf.Max(map.SampleHeight(p.x, p.z), map.WaterLevel) + 0.3f;
                Vector2 g = UiTheme.WorldToGui(cam, p, out bool front);
                if (front && havePrev) UiTheme.DrawLine(prev, g, col, 2.5f);
                prev = g;
                havePrev = front;
            }
        }

        // ------------------------------------------------------------------ top bar

        private void DrawTopBar(BattleSimulation sim)
        {
            float W = UiTheme.W;
            var r = Ui(new Rect(W * 0.5f - 430, 6, 860, 84));
            UiTheme.PanelBox(r);
            var ph = sim.Hq(B.PlayerTeam);
            var eh = sim.Hq(TeamUtil.Opponent(B.PlayerTeam));
            float php = ph != null ? ph.HpRatio : 0f, ehp = eh != null ? eh.HpRatio : 0f;
            UiTheme.DrawText(new Rect(r.x + 14, r.y + 6, 200, 20), "아군 본진", UiTheme.LabelSmall, UiTheme.MilBlue);
            UiTheme.Bar(new Rect(r.x + 14, r.y + 26, 300, 16), php, UiTheme.MilBlue, new Color(0, 0, 0, 0.6f));
            UiTheme.DrawText(new Rect(r.x + 14, r.y + 25, 300, 18), ph != null ? Mathf.CeilToInt(ph.Hp) + " / " + Mathf.CeilToInt(ph.MaxHp) : "파괴됨", UiTheme.LabelCenter, Color.white);
            UiTheme.DrawText(new Rect(r.xMax - 314, r.y + 6, 300, 20), "적군 본진", new GUIStyle(UiTheme.LabelSmall) { alignment = TextAnchor.MiddleRight }, UiTheme.WarnRed);
            UiTheme.Bar(new Rect(r.xMax - 314, r.y + 26, 300, 16), ehp, UiTheme.WarnRed, new Color(0, 0, 0, 0.6f));
            UiTheme.DrawText(new Rect(r.xMax - 314, r.y + 25, 300, 18), eh != null ? Mathf.CeilToInt(eh.Hp) + " / " + Mathf.CeilToInt(eh.MaxHp) : "파괴됨", UiTheme.LabelCenter, Color.white);

            int t = Mathf.FloorToInt(sim.Time);
            UiTheme.DrawText(new Rect(r.x + 330, r.y + 4, 200, 32), (t / 60).ToString("00") + ":" + (t % 60).ToString("00"), new GUIStyle(UiTheme.LabelCenterBold) { fontSize = 26 }, Color.white);
            UiTheme.DrawText(new Rect(r.x + 330, r.y + 32, 200, 16), "증원 부대 " + Mathf.CeilToInt(sim.NextWaveIn) + "초", UiTheme.LabelCenter, UiTheme.TextDim);

            int pz = sim.ZonesOwned(B.PlayerTeam), ez = sim.ZonesOwned(TeamUtil.Opponent(B.PlayerTeam)), nz = sim.Zones.Length - pz - ez;
            var ts = sim.TeamOf(B.PlayerTeam);
            string line = "<color=#5aa0ff>■ 아군 구역 " + pz + "</color>   <color=#bbbbbb>중립 " + nz + "</color>   <color=#ff5a4a>적군 구역 " + ez + " ■</color>      격파 " + ts.Stats.Kills + "  ·  손실 " + ts.Stats.Losses + "  ·  건물 파괴 " + ts.Stats.StructuresDestroyed;
            UiTheme.DrawText(new Rect(r.x + 10, r.y + 54, r.width - 20, 22), line, UiTheme.LabelCenter);

            // Menu button.
            var mb = Ui(new Rect(10, 10, 110, 36));
            if (UiTheme.Btn(mb, "≡ 메뉴")) { MenuOpen = !MenuOpen; B.Paused = MenuOpen; }
            var info = new Rect(W - 260, 10, 250, 22);
            UiTheme.DrawText(info, "FPS " + Mathf.RoundToInt(_fps) + "  유닛 " + sim.UnitList.Count, new GUIStyle(UiTheme.LabelSmall) { alignment = TextAnchor.MiddleRight }, UiTheme.TextDim);
        }

        private void DrawNotices()
        {
            float y = 56;
            for (int i = B.Notices.Count - 1; i >= 0 && y < 260; i--)
            {
                var n = B.Notices[i];
                float age = Time.unscaledTime - n.Time;
                if (age > 7f) continue;
                float a = Mathf.Clamp01(7f - age);
                var r = new Rect(10, y, 440, 24);
                UiTheme.Fill(r, new Color(0, 0, 0, 0.55f * a), 3f);
                UiTheme.Fill(new Rect(r.x, r.y, 4, r.height), n.Color.WithAlpha(a));
                UiTheme.DrawText(new Rect(r.x + 10, r.y, r.width - 12, r.height), n.Text, UiTheme.LabelSmall, Color.white.WithAlpha(a));
                y += 27;
            }
        }

        private void DrawFeedback()
        {
            if (B.Cmd == null || string.IsNullOrEmpty(B.Cmd.Feedback)) return;
            float age = Time.unscaledTime - B.Cmd.FeedbackTime;
            if (age > 2.5f) return;
            float a = Mathf.Clamp01(2.5f - age);
            var r = new Rect(UiTheme.W * 0.5f - 300, UiTheme.H - 262, 600, 28);
            UiTheme.Fill(r, new Color(0, 0, 0, 0.6f * a), 4f);
            UiTheme.DrawText(r, B.Cmd.Feedback, UiTheme.LabelCenterBold, Color.white.WithAlpha(a));
        }

        // ------------------------------------------------------------------ bottom panel

        private void DrawBottomPanel(BattleSimulation sim)
        {
            float W = UiTheme.W, H = UiTheme.H;
            float mapSize = 300f;
            var panel = Ui(new Rect(0, H - 226, W - mapSize - 16, 226));
            UiTheme.Fill(panel, UiTheme.Black);
            UiTheme.Fill(new Rect(panel.x, panel.y, panel.width, 2), UiTheme.MilBlueDark);

            var sel = new Rect(10, H - 216, 400, 206);
            DrawSelection(sim, sel);

            // CP gauge + cards.
            var ts = sim.TeamOf(B.PlayerTeam);
            float cardsX = sel.xMax + 14;
            float cardW = 112, cardH = 150, gap = 8;
            float cardsW = Mathf.Min(8, ts.Deck.Length) * (cardW + gap) - gap;
            var cp = new Rect(cardsX, H - 214, cardsW, 34);
            DrawCp(sim, ts, cp);
            for (int i = 0; i < ts.Deck.Length && i < 8; i++)
            {
                var r = new Rect(cardsX + i * (cardW + gap), H - 172, cardW, cardH + 6);
                DrawCard(sim, ts, ts.Deck[i], i, r);
            }
        }

        private void DrawCp(BattleSimulation sim, TeamState ts, Rect r)
        {
            UiTheme.Fill(r, new Color(0, 0, 0, 0.6f), 3f);
            float max = sim.Config.CpMax;
            int segs = Mathf.RoundToInt(max);
            float segW = (r.width - 150) / segs;
            for (int i = 0; i < segs; i++)
            {
                var s = new Rect(r.x + 6 + i * segW, r.y + 7, segW - 4, r.height - 14);
                float fill = Mathf.Clamp01(ts.Cp - i);
                UiTheme.Fill(s, new Color(0.12f, 0.14f, 0.18f, 1f), 2f);
                if (fill > 0f) UiTheme.Fill(new Rect(s.x, s.y, s.width * fill, s.height), fill >= 1f ? new Color(0.95f, 0.75f, 0.25f) : new Color(0.6f, 0.45f, 0.15f), 2f);
            }
            float rate = sim.CpRegenRate(B.PlayerTeam);
            string extra = ts.ExtractorCount > 0 ? " <color=#ffcc55>채굴기 x" + sim.Config.ExtractorMultiplier.ToString("0.0") + "</color>" : "";
            UiTheme.DrawText(new Rect(r.xMax - 146, r.y, 144, r.height), "<b>CP " + ts.Cp.ToString("0.0") + "/" + max.ToString("0") + "</b>  +" + rate.ToString("0.00") + "/s" + extra, UiTheme.LabelSmall, UiTheme.Gold);
        }

        private void DrawCard(BattleSimulation sim, TeamState ts, int defId, int index, Rect r)
        {
            var def = sim.Units.Get(defId);
            if (def == null) return;
            bool placing = B.Cmd.PlacingDefId == defId;
            float cd = ts.CardCooldown(defId);
            bool affordable = ts.Cp + 1e-4f >= def.CpCost && cd <= 0f;
            var rc = UiTheme.RarityColor(def.Rarity);
            UiTheme.Fill(r, new Color(0.1f, 0.11f, 0.13f, 0.98f), 5f);
            var portrait = Root.Portraits.Get(def.Id);
            var pr = new Rect(r.x + 4, r.y + 4, r.width - 8, 96);
            if (portrait != null) GUI.DrawTexture(pr, portrait, ScaleMode.ScaleAndCrop);
            else { UiTheme.Fill(pr, new Color(0.15f, 0.17f, 0.2f), 3f); UiTheme.DrawText(pr, UiTheme.SectionGlyph(def), new GUIStyle(UiTheme.LabelCenterBold) { fontSize = 40 }, UiTheme.TextDim); }
            UiTheme.Fill(new Rect(r.x + 4, r.y + 100, r.width - 8, 3), rc);
            UiTheme.DrawText(new Rect(r.x + 4, r.y + 104, r.width - 8, 22), def.NameKo, new GUIStyle(UiTheme.LabelCenter) { fontSize = 13, fontStyle = FontStyle.Bold });
            UiTheme.DrawText(new Rect(r.x + 4, r.y + 124, r.width - 8, 18), def.DeployKindKo + (def.Count > 1 ? " x" + def.Count : ""), new GUIStyle(UiTheme.LabelCenter) { fontSize = 11 }, UiTheme.TextDim);
            var badge = new Rect(r.x + 2, r.y + 2, 30, 30);
            UiTheme.Fill(badge, affordable ? new Color(0.85f, 0.65f, 0.15f) : new Color(0.45f, 0.1f, 0.1f), 15f);
            UiTheme.DrawText(badge, def.CpCost.ToString(), UiTheme.LabelCenterBold, Color.white);
            UiTheme.DrawText(new Rect(r.xMax - 34, r.y + 4, 30, 16), "F" + (index + 1), new GUIStyle(UiTheme.LabelSmall) { alignment = TextAnchor.MiddleRight, fontSize = 11 }, UiTheme.TextDim);
            if (!affordable)
            {
                UiTheme.Fill(r, new Color(0, 0, 0, 0.55f), 5f);
                if (cd > 0f) UiTheme.DrawText(r, Mathf.CeilToInt(cd) + "초", new GUIStyle(UiTheme.LabelCenterBold) { fontSize = 22 }, Color.white);
                else
                {
                    float need = def.CpCost - ts.Cp;
                    UiTheme.Fill(new Rect(r.x, r.yMax - 6, r.width * Mathf.Clamp01(ts.Cp / Mathf.Max(1f, def.CpCost)), 4), UiTheme.Gold);
                    _ = need;
                }
            }
            UiTheme.Frame(r, placing ? UiTheme.Gold : (affordable ? rc.WithAlpha(0.8f) : UiTheme.Line), placing ? 3f : 1.5f, 5f);

            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 0) { if (placing) B.Cmd.CancelPlacement(); else B.Cmd.BeginPlacement(defId); }
                Event.current.Use();
            }
            if (r.Contains(Event.current.mousePosition) && Event.current.type == EventType.Repaint)
            {
                var tip = new Rect(r.x - 40, r.y - 120, 300, 112);
                UiTheme.Fill(tip, new Color(0.02f, 0.03f, 0.04f, 0.95f), 4f);
                UiTheme.Frame(tip, rc, 1.5f, 4f);
                string t = "<b>#" + def.Id + " " + def.NameKo + "</b>  <color=#" + ColorUtility.ToHtmlStringRGB(rc) + ">" + def.RarityKo + "</color>\n" +
                           "체력 " + def.Hp + "  공격 " + def.Attack + "  소환 " + def.Count + "\n" +
                           "능력: " + UnitDatabase.AbilityNameKo(def.Ability) + "\n" + def.Description;
                UiTheme.DrawText(new Rect(tip.x + 8, tip.y + 6, tip.width - 14, tip.height - 10), t, UiTheme.Tooltip);
            }
        }

        private void DrawSelection(BattleSimulation sim, Rect r)
        {
            UiTheme.PanelBox(r);
            var units = B.Cmd.SelectedUnits();
            if (units.Count == 0)
            {
                UiTheme.DrawText(new Rect(r.x + 12, r.y + 8, r.width - 20, 24), "선택된 부대 없음", UiTheme.LabelBold, UiTheme.TextDim);
                string help = "좌클릭/드래그: 선택   우클릭: 이동·공격\nA+클릭: 공격 이동   S: 정지   H: 위치 사수\nCtrl+숫자: 부대 지정   숫자: 부대 선택\nSpace: 선택 부대로 카메라   F: 특수 능력\nF1~F8 / 카드 클릭: 배치   ESC: 메뉴";
                UiTheme.DrawText(new Rect(r.x + 12, r.y + 36, r.width - 20, 160), help, new GUIStyle(UiTheme.LabelSmall) { alignment = TextAnchor.UpperLeft }, UiTheme.TextDim);
                return;
            }

            // Group by definition.
            var groups = new Dictionary<int, List<SimUnit>>();
            foreach (var u in units)
            {
                if (!groups.TryGetValue(u.Def.Id, out var l)) { l = new List<SimUnit>(); groups[u.Def.Id] = l; }
                l.Add(u);
            }
            var first = units[0];
            var def = first.Def;
            if (groups.Count == 1)
            {
                var pr = new Rect(r.x + 10, r.y + 10, 110, 110);
                var tex = Root.Portraits.Get(def.Id);
                if (tex != null) GUI.DrawTexture(pr, tex, ScaleMode.ScaleAndCrop); else UiTheme.Fill(pr, UiTheme.PanelLight, 3f);
                UiTheme.Frame(pr, UiTheme.RarityColor(def.Rarity), 2f, 3f);
                float hp = 0f, maxHp = 0f;
                foreach (var u in units) { hp += u.Hp; maxHp += u.MaxHp; }
                float x = pr.xMax + 12;
                UiTheme.DrawText(new Rect(x, r.y + 8, 260, 24), def.NameKo + (units.Count > 1 ? "  x" + units.Count : ""), UiTheme.LabelBold);
                UiTheme.DrawText(new Rect(x, r.y + 30, 260, 18), "<color=#" + ColorUtility.ToHtmlStringRGB(UiTheme.RarityColor(def.Rarity)) + ">" + def.RarityKo + "</color>  ·  " + def.SectionKo, UiTheme.LabelSmall);
                UiTheme.Bar(new Rect(x, r.y + 52, 250, 14), maxHp > 0 ? hp / maxHp : 0f, new Color(0.3f, 0.75f, 0.35f), new Color(0, 0, 0, 0.6f));
                UiTheme.DrawText(new Rect(x, r.y + 51, 250, 16), Mathf.CeilToInt(hp) + " / " + Mathf.CeilToInt(maxHp), new GUIStyle(UiTheme.LabelCenter) { fontSize = 12 });
                string stats = "공격 " + def.Attack + " (" + DamageKo(def.DamageType) + ")   사거리 " + def.Range + "m\n" +
                               "속도 " + def.MoveSpeed + "m/s   시야 " + def.Sight + "m";
                if (def.Magazine > 0) stats += "   탄약 " + first.Ammo + "/" + def.Magazine;
                UiTheme.DrawText(new Rect(x, r.y + 70, 270, 40), stats, new GUIStyle(UiTheme.LabelSmall) { alignment = TextAnchor.UpperLeft, fontSize = 12 }, UiTheme.TextDim);

                // Status line.
                string status = OrderKo(first);
                if (first.ChargeTimer > 0f) status += "  <color=#ffcc55>돌격!</color>";
                if (first.SuppressTimer > 0f) status += "  <color=#ff8866>제압됨</color>";
                if (first.BurnTimer > 0f) status += "  <color=#ff7733>화염</color>";
                if (def.Ability == AbilityType.Stealth) status += first.Revealed ? "  <color=#ff8866>노출</color>" : "  <color=#88ccff>은신 중</color>";
                if (first.AuraAttackBonus > 0f) status += "  <color=#88ff99>지휘 버프</color>";
                if (first.Constructing) status += "  건설 " + Mathf.RoundToInt(first.BuildProgress * 100) + "%";
                UiTheme.DrawText(new Rect(x, r.y + 108, 270, 18), status, UiTheme.LabelSmall);

                // Ability.
                var ab = new Rect(r.x + 10, r.y + 130, r.width - 20, 66);
                UiTheme.Fill(ab, UiTheme.PanelLight, 3f);
                string abName = UnitDatabase.AbilityNameKo(def.Ability);
                bool active = BattleSimulation.HasActiveAbility(def);
                UiTheme.DrawText(new Rect(ab.x + 8, ab.y + 4, ab.width - 120, 20), "특수 능력: <b>" + abName + "</b>", UiTheme.LabelSmall);
                UiTheme.DrawText(new Rect(ab.x + 8, ab.y + 24, ab.width - 120, 40), AbilityDesc(def), new GUIStyle(UiTheme.LabelSmall) { fontSize = 11, wordWrap = true, alignment = TextAnchor.UpperLeft }, UiTheme.TextDim);
                if (active)
                {
                    float cd = first.AbilityCooldown;
                    var btn = new Rect(ab.xMax - 104, ab.y + 10, 96, 46);
                    bool ready = cd <= 0f && (def.Ability != AbilityType.Transport || first.PassengerCount > 0);
                    GUI.enabled = ready;
                    if (UiTheme.Btn(btn, ready ? "사용 (F)" : (cd > 0 ? Mathf.CeilToInt(cd) + "초" : "-"), ready ? UiTheme.ButtonTabOn : UiTheme.ButtonTab)) B.Cmd.UseAbility();
                    GUI.enabled = true;
                }
                else UiTheme.DrawText(new Rect(ab.xMax - 104, ab.y + 10, 96, 46), def.AbilityMode == AbilityMode.Auto ? "자동" : "상시", UiTheme.LabelCenter, UiTheme.TextDim);
            }
            else
            {
                UiTheme.DrawText(new Rect(r.x + 12, r.y + 6, r.width - 20, 22), "선택: " + units.Count + "기 (" + groups.Count + "종)", UiTheme.LabelBold);
                int i = 0;
                foreach (var kv in groups)
                {
                    if (i >= 12) break;
                    int col = i % 6, row = i / 6;
                    var cell = new Rect(r.x + 10 + col * 64, r.y + 34 + row * 78, 58, 72);
                    var d = sim.Units.Get(kv.Key);
                    var tex = Root.Portraits.Get(kv.Key);
                    var pr = new Rect(cell.x, cell.y, 58, 52);
                    if (tex != null) GUI.DrawTexture(pr, tex, ScaleMode.ScaleAndCrop); else UiTheme.Fill(pr, UiTheme.PanelLight, 3f);
                    UiTheme.Frame(pr, UiTheme.RarityColor(d.Rarity), 1.5f, 3f);
                    float hp = 0, mx = 0;
                    foreach (var u in kv.Value) { hp += u.Hp; mx += u.MaxHp; }
                    UiTheme.Bar(new Rect(cell.x, cell.y + 54, 58, 5), mx > 0 ? hp / mx : 0, new Color(0.3f, 0.75f, 0.35f), new Color(0, 0, 0, 0.6f));
                    UiTheme.DrawText(new Rect(cell.x, cell.y + 58, 58, 14), "x" + kv.Value.Count, new GUIStyle(UiTheme.LabelCenter) { fontSize = 12 });
                    if (Event.current.type == EventType.MouseDown && pr.Contains(Event.current.mousePosition))
                    {
                        var ids = new List<int>();
                        foreach (var u in kv.Value) ids.Add(u.Id);
                        B.Cmd.Select(ids, false);
                        Event.current.Use();
                    }
                    i++;
                }
                bool anyActive = false;
                foreach (var kv in groups) if (BattleSimulation.HasActiveAbility(sim.Units.Get(kv.Key))) anyActive = true;
                if (anyActive && UiTheme.Btn(new Rect(r.xMax - 110, r.y + 6, 100, 26), "특수 능력 (F)", UiTheme.ButtonTabOn)) B.Cmd.UseAbility();
            }
        }

        private static string DamageKo(DamageType t)
        {
            switch (t)
            {
                case DamageType.Bullet: return "소화기";
                case DamageType.Sniper: return "저격";
                case DamageType.AP: return "대전차";
                case DamageType.HE: return "고폭";
                case DamageType.Flame: return "화염";
                case DamageType.AA: return "대공";
                case DamageType.Tesla: return "전격";
                case DamageType.Melee: return "근접";
                default: return "-";
            }
        }

        private static string OrderKo(SimUnit u)
        {
            switch (u.Order)
            {
                case OrderType.Move: return "이동 중";
                case OrderType.AttackMove: return "공격 이동";
                case OrderType.AttackUnit: return "목표 공격";
                case OrderType.Hold: return "위치 사수";
                case OrderType.Lane: return "공격로 진격";
                case OrderType.ClearRubble: return "잔해 철거";
                default: return u.TargetId != 0 ? "교전 중" : "대기";
            }
        }

        private static string AbilityDesc(UnitDefinition d)
        {
            switch (d.Ability)
            {
                case AbilityType.Charge: return "이동속도 x" + d.AbP1 + ", 연사 +33% (" + d.AbP2 + "초, 대기 " + d.AbCooldown + "초)";
                case AbilityType.Heal: return "반경 " + d.AbRadius + "m 아군 보병 초당 " + d.AbP1 + " 치료";
                case AbilityType.Repair: return "차량/건물 초당 " + d.AbP1 + " 수리, 잔해 철거 (우클릭 잔해)";
                case AbilityType.Transport: return "보병 " + d.AbP2 + "명 탑승. 버튼/도착 시 하차";
                case AbilityType.Stealth: return "적 " + d.AbP1 + "m 이내 또는 사격 후 " + d.AbP2 + "초간 노출";
                case AbilityType.Suppress: return "명중한 보병 이동 x" + d.AbP1 + ", 연사 감소 (" + d.AbP2 + "초)";
                case AbilityType.Aura: return "반경 " + d.AbRadius + "m 아군 공격 +" + Mathf.RoundToInt(d.AbP1 * 100) + "%, 피해 -" + Mathf.RoundToInt(d.AbP2 * 100) + "%";
                case AbilityType.Recon: return "반경 " + d.AbRadius + "m 관측·은신 탐지 (포병 사격 지원)";
                case AbilityType.Suicide: return "적에게 돌진해 반경 " + d.AbRadius + "m 피해 " + d.AbP1;
                case AbilityType.Fear: return "반경 " + d.AbRadius + "m 적 공격 -" + Mathf.RoundToInt(d.AbP1 * 100) + "%, 속도 -" + Mathf.RoundToInt(d.AbP2 * 100) + "%";
                case AbilityType.CallArtillery: return "밀집한 적에게 포격 " + d.AbP2 + "발 (대기 " + d.AbCooldown + "초)";
                case AbilityType.LayMines: return "지뢰 매설 (최대 " + d.AbP2 + "개, 피해 " + d.AbP1 + ")";
                case AbilityType.MineSweep: return "반경 " + d.AbRadius + "m 적 지뢰 탐지·해체";
                case AbilityType.Shield: return "총탄 피해 -" + Mathf.RoundToInt(d.AbP1 * 100) + "%";
                case AbilityType.Burn: return "명중 대상 초당 " + d.AbP1 + " 화염 피해 (" + d.AbP2 + "초)";
                case AbilityType.DroneSwarm: return "자폭 드론 자동 발진 (최대 " + d.AbP2 + ")";
                case AbilityType.Fortify: return "주변 아군 보병 피해 -" + Mathf.RoundToInt(d.AbP1 * 100) + "%";
                case AbilityType.SlowField: return "주변 적 보병·경차량 감속 x" + d.AbP1;
                case AbilityType.Tesla: return "전격이 " + d.AbP1 + "회 연쇄";
                case AbilityType.ForwardPost: return "반경 " + d.AbRadius + "m 부대 배치 가능, CP +" + Mathf.RoundToInt(d.AbP1 * 100) + "%";
                case AbilityType.CpBoost: return "CP 충전 속도 x1.5";
                case AbilityType.Resupply: return "주변 회복, CP 충전 +" + Mathf.RoundToInt(d.AbP1 * 100) + "%";
                case AbilityType.Flare: return "반경 " + d.AbRadius + "m 아군 공격 +" + Mathf.RoundToInt(d.AbP1 * 100) + "%, 관측";
                default: return d.Description;
            }
        }

        // ------------------------------------------------------------------ minimap

        public Vector2 WorldToMinimap(Vec2 p)
        {
            float half = B.Sim.Map.Half, size = B.Sim.Map.Size;
            return new Vector2(_minimapRect.x + (p.X + half) / size * _minimapRect.width, _minimapRect.y + (1f - (p.Z + half) / size) * _minimapRect.height);
        }

        private Vec2 MinimapToWorld(Vector2 g)
        {
            float half = B.Sim.Map.Half, size = B.Sim.Map.Size;
            float x = (g.x - _minimapRect.x) / _minimapRect.width * size - half;
            float z = (1f - (g.y - _minimapRect.y) / _minimapRect.height) * size - half;
            return new Vec2(x, z);
        }

        private void DrawMinimap(BattleSimulation sim)
        {
            float W = UiTheme.W, H = UiTheme.H, s = 300f;
            var frame = Ui(new Rect(W - s - 12, H - s - 12, s + 8, s + 8));
            UiTheme.Fill(frame, UiTheme.Black, 4f);
            _minimapRect = new Rect(frame.x + 4, frame.y + 4, s, s);
            if (Root.World.MinimapTexture != null) GUI.DrawTexture(_minimapRect, Root.World.MinimapTexture);

            var ev = Event.current;
            if (ev.type == EventType.Repaint)
            {
                // Lanes.
                foreach (var lane in sim.Map.Lanes)
                    for (int i = 0; i + 4 < lane.Count; i += 4)
                        UiTheme.DrawLine(WorldToMinimap(lane[i]), WorldToMinimap(lane[i + 4]), new Color(0.95f, 0.85f, 0.55f, 0.35f), 2f);
                // Zones.
                foreach (var z in sim.Zones)
                {
                    Vector2 c = WorldToMinimap(z.Def.Center);
                    float rr = z.Def.Radius / sim.Map.Size * s;
                    Color oc = Conv.TeamColor(z.Owner);
                    if (z.Contested && Mathf.Repeat(Time.unscaledTime, 0.6f) < 0.3f) oc = new Color(1f, 0.85f, 0.2f);
                    UiTheme.Fill(new Rect(c.x - rr, c.y - rr, rr * 2, rr * 2), oc.WithAlpha(0.22f), rr);
                    UiTheme.Frame(new Rect(c.x - rr, c.y - rr, rr * 2, rr * 2), oc.WithAlpha(0.9f), 1.5f, rr);
                    UiTheme.DrawText(new Rect(c.x - 12, c.y - 8, 24, 16), z.Def.Id.ToString(), new GUIStyle(UiTheme.LabelCenter) { fontSize = 10, fontStyle = FontStyle.Bold });
                }
                // Units.
                foreach (var u in sim.UnitList)
                {
                    if (!u.Alive) continue;
                    if (u.Team != B.PlayerTeam && u.Def.Ability == AbilityType.Stealth && !u.Revealed) continue;
                    Vector2 p = WorldToMinimap(u.Pos);
                    float sz = u.Def.IsHQ ? 12f : (u.IsStructure ? 6f : (u.IsInfantry ? 3f : 4.5f));
                    Color c = u.Team == B.PlayerTeam ? new Color(0.35f, 0.75f, 1f) : new Color(1f, 0.3f, 0.25f);
                    if (u.IsAir) c = u.Team == B.PlayerTeam ? new Color(0.6f, 0.9f, 1f) : new Color(1f, 0.6f, 0.5f);
                    UiTheme.Fill(new Rect(p.x - sz * 0.5f, p.y - sz * 0.5f, sz, sz), c, u.IsStructure ? 0f : sz * 0.5f);
                    if (u.Def.IsHQ) UiTheme.Frame(new Rect(p.x - sz * 0.5f, p.y - sz * 0.5f, sz, sz), Color.white, 1.5f);
                }
                // Pings.
                foreach (var pg in B.Pings)
                {
                    float age = Time.unscaledTime - pg.Time;
                    float rr = 6f + age * 14f;
                    Vector2 c = WorldToMinimap(pg.Pos);
                    UiTheme.Frame(new Rect(c.x - rr, c.y - rr, rr * 2, rr * 2), pg.Color.WithAlpha(Mathf.Clamp01(1f - age / 3f)), 2f, rr);
                }
                // Camera view.
                if (Root.CamRig.GroundCorners(_corners))
                {
                    for (int i = 0; i < 4; i++)
                    {
                        Vector2 a = WorldToMinimap(new Vec2(_corners[i].x, _corners[i].z));
                        Vector2 b = WorldToMinimap(new Vec2(_corners[(i + 1) % 4].x, _corners[(i + 1) % 4].z));
                        a.x = Mathf.Clamp(a.x, _minimapRect.xMin, _minimapRect.xMax); a.y = Mathf.Clamp(a.y, _minimapRect.yMin, _minimapRect.yMax);
                        b.x = Mathf.Clamp(b.x, _minimapRect.xMin, _minimapRect.xMax); b.y = Mathf.Clamp(b.y, _minimapRect.yMin, _minimapRect.yMax);
                        UiTheme.DrawLine(a, b, Color.white, 1.5f);
                    }
                }
                UiTheme.Frame(_minimapRect, UiTheme.Line, 1.5f);
                UiTheme.DrawText(new Rect(_minimapRect.x + 4, _minimapRect.y + 2, 120, 16), "N ▲", new GUIStyle(UiTheme.LabelSmall) { fontSize = 11 }, UiTheme.TextDim);
            }

            // Minimap input: left = move camera (drag), right = move selected units.
            if (ev.type == EventType.MouseDown && _minimapRect.Contains(ev.mousePosition))
            {
                var w = MinimapToWorld(ev.mousePosition);
                if (ev.button == 0)
                {
                    _minimapDrag = true;
                    Root.CamRig.SetFocus(w.ToV3(sim.Map.SampleHeight(w)));
                }
                else if (ev.button == 1 && B.Cmd.Selection.Count > 0)
                {
                    var ids = new List<int>();
                    foreach (var u in B.Cmd.SelectedUnits()) if (!u.IsStructure) ids.Add(u.Id);
                    if (ids.Count > 0) sim.Enqueue(GameCommand.Units(CommandType.Move, B.PlayerTeam, ids.ToArray(), w));
                    B.Cmd.Say("미니맵 이동 명령");
                }
                ev.Use();
            }
            if (ev.type == EventType.MouseDrag && _minimapDrag)
            {
                var w = MinimapToWorld(new Vector2(Mathf.Clamp(ev.mousePosition.x, _minimapRect.xMin, _minimapRect.xMax), Mathf.Clamp(ev.mousePosition.y, _minimapRect.yMin, _minimapRect.yMax)));
                Root.CamRig.SetFocus(w.ToV3(sim.Map.SampleHeight(w)));
                ev.Use();
            }
            if (ev.type == EventType.MouseUp) _minimapDrag = false;
        }

        // ------------------------------------------------------------------ menu / result

        private void DrawMenu()
        {
            float W = UiTheme.W, H = UiTheme.H;
            UiTheme.Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.55f));
            var r = new Rect(W * 0.5f - 200, H * 0.5f - 190, 400, 380);
            UiTheme.PanelBox(r, true);
            UiTheme.DrawText(new Rect(r.x, r.y + 14, r.width, 40), "일시 정지", new GUIStyle(UiTheme.LabelCenterBold) { fontSize = 28 });
            var p = Root.Profile;
            if (UiTheme.Btn(new Rect(r.x + 40, r.y + 80, 320, 46), "전투 계속")) { MenuOpen = false; B.Paused = false; }
            if (UiTheme.Btn(new Rect(r.x + 40, r.y + 136, 320, 40), "눈 내리는 효과: " + (p.snow ? "켜짐" : "꺼짐"))) { p.snow = !p.snow; B.Fx.SnowEnabled = p.snow; p.Save(); }
            if (UiTheme.Btn(new Rect(r.x + 40, r.y + 184, 320, 40), "화면 가장자리 스크롤: " + (p.edgeScroll ? "켜짐" : "꺼짐"))) { p.edgeScroll = !p.edgeScroll; Root.CamRig.EdgeScroll = p.edgeScroll; p.Save(); }
            if (UiTheme.Btn(new Rect(r.x + 40, r.y + 232, 320, 40), "게임 속도: x" + B.SimSpeed.ToString("0.0"))) B.SimSpeed = B.SimSpeed >= 2f ? 0.5f : B.SimSpeed + 0.5f;
            if (UiTheme.Btn(new Rect(r.x + 40, r.y + 300, 320, 46), "항복하고 로비로")) { MenuOpen = false; B.Surrender(); }
        }

        private void DrawResult(BattleSimulation sim)
        {
            float W = UiTheme.W, H = UiTheme.H;
            UiTheme.Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.5f));
            bool win = sim.Winner == B.PlayerTeam;
            UiTheme.DrawText(new Rect(0, H * 0.18f, W, 100), win ? "승  리" : "패  배", UiTheme.Big, win ? UiTheme.Gold : UiTheme.WarnRed);
            UiTheme.DrawText(new Rect(0, H * 0.18f + 90, W, 30), win ? "VICTORY - 적 본진 파괴" : "DEFEAT - 아군 본진 함락", UiTheme.LabelCenterBold, Color.white);
            var r = new Rect(W * 0.5f - 260, H * 0.42f, 520, 250);
            UiTheme.PanelBox(r, true);
            var res = B.Result;
            if (res != null)
            {
                int t = Mathf.FloorToInt(res.duration);
                string s = "전투 시간  " + (t / 60) + "분 " + (t % 60) + "초\n" +
                           "격파  " + res.kills + "     손실  " + res.losses + "\n" +
                           "최종 점령 구역  " + res.zones + " / 24     건설  " + res.buildings + "\n\n" +
                           "<color=#e6bb55>보상: 골드 +" + res.goldReward + "   경험치 +" + res.xpReward + "</color>";
                UiTheme.DrawText(new Rect(r.x + 30, r.y + 20, r.width - 60, 150), s, new GUIStyle(UiTheme.Label) { alignment = TextAnchor.UpperLeft, fontSize = 18 });
            }
            if (UiTheme.Btn(new Rect(r.x + 110, r.yMax - 62, 300, 46), "로비로 돌아가기", UiTheme.ButtonBig)) Root.ReturnToLobby();
        }
    }
}
