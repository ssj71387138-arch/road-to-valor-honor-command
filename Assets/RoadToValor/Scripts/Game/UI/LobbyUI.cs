using System.Collections.Generic;
using RoadToValor.Core;
using UnityEngine;

namespace RoadToValor.Game
{
    /// <summary>
    /// Lobby: live 3D battlefield preview in the centre (AI-vs-AI demo battle with an orbiting camera),
    /// battle start, squad builder (pick 8 of 74 cards), unit codex with rotating 3D preview, research,
    /// logistics (daily supply), battle records, shop, player level / gold / gems.
    /// </summary>
    public sealed class LobbyUI : MonoBehaviour
    {
        public GameRoot Root;
        public bool Visible;

        private enum Screen { Main, Deck, Codex, Research, Logistics, Records, Shop }
        private Screen _screen = Screen.Main;
        private Vector2 _scroll, _scroll2;
        private int _filter;
        private int _codexId = 1;
        private readonly List<int> _deckEdit = new List<int>();
        private string _toast = "";
        private float _toastTime;
        private Texture2D _concept;

        private static readonly string[] FilterNames = { "전체", "보병", "기갑", "특수·정예", "항공", "건물", "하드코어" };

        private void Toast(string s) { _toast = s; _toastTime = Time.unscaledTime; }

        private void OnGUI()
        {
            if (!Visible) return;
            UiTheme.Begin();
            float W = UiTheme.W, H = UiTheme.H;
            var p = Root.Profile;

            // Vignette so the 3D preview reads as background.
            UiTheme.Fill(new Rect(0, 0, W, 70), new Color(0, 0, 0, 0.75f));
            UiTheme.Fill(new Rect(0, H - 40, W, 40), new Color(0, 0, 0, 0.6f));
            DrawTopBar(p, W);

            switch (_screen)
            {
                case Screen.Main: DrawMain(p, W, H); break;
                case Screen.Deck: DrawDeck(p, W, H); break;
                case Screen.Codex: DrawCodex(W, H); break;
                case Screen.Research: DrawResearch(p, W, H); break;
                case Screen.Logistics: DrawLogistics(p, W, H); break;
                case Screen.Records: DrawRecords(p, W, H); break;
                case Screen.Shop: DrawShop(p, W, H); break;
            }
            if (_screen != Screen.Codex) Root.Portraits.StopLive();

            UiTheme.DrawText(new Rect(16, H - 36, W - 32, 30), "ROAD TO VALOR: HONOR COMMAND  ·  프로토타입 빌드  ·  유닛 수치는 unit_balance.csv 제안값", UiTheme.LabelSmall, UiTheme.TextDim);
            if (!string.IsNullOrEmpty(_toast) && Time.unscaledTime - _toastTime < 2.5f)
            {
                var r = new Rect(W * 0.5f - 280, H - 110, 560, 34);
                UiTheme.Fill(r, new Color(0, 0, 0, 0.8f), 4f);
                UiTheme.DrawText(r, _toast, UiTheme.LabelCenterBold, UiTheme.Gold);
            }
        }

        private void DrawTopBar(PlayerProfile p, float W)
        {
            UiTheme.DrawText(new Rect(24, 8, 600, 36), "<b>ROAD TO VALOR</b> <color=#e6bb55>HONOR COMMAND</color>", UiTheme.Title);
            UiTheme.DrawText(new Rect(26, 42, 400, 22), "로두터발러  ·  " + Root.Data.Layout.Name, UiTheme.LabelSmall, UiTheme.TextDim);
            float x = W - 640;
            var lv = new Rect(x, 12, 250, 46);
            UiTheme.PanelBox(lv);
            UiTheme.DrawText(new Rect(lv.x + 10, lv.y + 2, 230, 22), "<b>Lv." + p.level + "</b>  " + p.commanderName, UiTheme.Label);
            UiTheme.Bar(new Rect(lv.x + 10, lv.y + 28, 230, 9), p.xp / (float)p.XpForNext, UiTheme.MilBlue, new Color(0, 0, 0, 0.6f));
            var gold = new Rect(x + 262, 12, 180, 46);
            UiTheme.PanelBox(gold);
            UiTheme.Fill(new Rect(gold.x + 10, gold.y + 15, 16, 16), UiTheme.Gold, 8f);
            UiTheme.DrawText(new Rect(gold.x + 34, gold.y, 140, 46), "골드 <b>" + p.gold.ToString("N0") + "</b>", UiTheme.Label);
            var gems = new Rect(x + 454, 12, 170, 46);
            UiTheme.PanelBox(gems);
            UiTheme.Fill(new Rect(gems.x + 10, gems.y + 15, 16, 16), new Color(0.4f, 0.85f, 1f), 3f);
            UiTheme.DrawText(new Rect(gems.x + 34, gems.y, 130, 46), "보석 <b>" + p.gems.ToString("N0") + "</b>", UiTheme.Label);
        }

        private bool MenuButton(Rect r, string label, Screen target)
        {
            bool on = _screen == target;
            if (UiTheme.Btn(r, label, on ? UiTheme.ButtonTabOn : UiTheme.Button))
            {
                _screen = target;
                _scroll = Vector2.zero;
                if (target == Screen.Deck) { _deckEdit.Clear(); _deckEdit.AddRange(CurrentDeck()); }
                return true;
            }
            return false;
        }

        private int[] CurrentDeck()
        {
            var d = Root.Profile.deck;
            if (d == null || d.Length != 8) d = Root.Data.Config.PlayerDefaultDeck;
            return d;
        }

        private void DrawSideMenu(float H)
        {
            float x = 24, y = 96, w = 250, h = 52, g = 10;
            MenuButton(new Rect(x, y, w, h), "작전 본부", Screen.Main); y += h + g;
            MenuButton(new Rect(x, y, w, h), "부대 편성", Screen.Deck); y += h + g;
            MenuButton(new Rect(x, y, w, h), "유닛 도감", Screen.Codex); y += h + g;
            MenuButton(new Rect(x, y, w, h), "연구소", Screen.Research); y += h + g;
            MenuButton(new Rect(x, y, w, h), "군수 보급", Screen.Logistics); y += h + g;
            MenuButton(new Rect(x, y, w, h), "전적 기록", Screen.Records); y += h + g;
            MenuButton(new Rect(x, y, w, h), "상점", Screen.Shop); y += h + g;
            _ = H;
        }

        // ------------------------------------------------------------------ main

        private void DrawMain(PlayerProfile p, float W, float H)
        {
            DrawSideMenu(H);

            // Briefing (right).
            var br = new Rect(W - 440, 90, 416, 470);
            UiTheme.PanelBox(br);
            UiTheme.DrawText(new Rect(br.x + 14, br.y + 8, 380, 28), "작전 브리핑", UiTheme.LabelBold, UiTheme.Gold);
            if (_concept == null) _concept = Resources.Load<Texture2D>("RoadToValor/UI/battlefield_map_concept");
            var img = new Rect(br.x + 14, br.y + 42, 388, 258);
            if (_concept != null) GUI.DrawTexture(img, _concept, ScaleMode.ScaleToFit);
            else if (Root.World.MinimapTexture != null) GUI.DrawTexture(img, Root.World.MinimapTexture, ScaleMode.ScaleToFit);
            UiTheme.Frame(img, UiTheme.Line, 1f);
            string brief = "• 전장 1200m x 1200m, 점령 구역 24곳 (건설 패드 48개)\n" +
                           "• 탑 · 미드 · 바텀 3개 공격로와 정글 우회로\n" +
                           "• 구역을 점령하면 그 구역의 패드 2곳에 건설 가능\n" +
                           "• CP는 최대 10, 시간에 따라 충전 (채굴기 1.5배)\n" +
                           "• 승리 조건: 적군 본진 파괴";
            UiTheme.DrawText(new Rect(br.x + 14, br.y + 306, 390, 150), brief, new GUIStyle(UiTheme.LabelSmall) { alignment = TextAnchor.UpperLeft, wordWrap = true, fontSize = 14 });

            // Deck preview + difficulty + start (bottom centre).
            var deck = CurrentDeck();
            float cw = 92, ch = 118, gap = 8;
            float total = 8 * (cw + gap) - gap;
            float x0 = W * 0.5f - total * 0.5f, y0 = H - 290;
            UiTheme.DrawText(new Rect(x0, y0 - 28, 400, 24), "출전 부대 (8장)  ·  평균 CP " + AvgCost(deck).ToString("0.0"), UiTheme.LabelBold);
            for (int i = 0; i < deck.Length && i < 8; i++) MiniCard(new Rect(x0 + i * (cw + gap), y0, cw, ch), deck[i], false);

            float by = H - 156;
            var diffs = new[] { "easy", "normal", "hard" };
            var diffKo = new[] { "쉬움", "보통", "어려움" };
            UiTheme.DrawText(new Rect(W * 0.5f - 330, by + 10, 120, 30), "적 AI 난이도", UiTheme.Label);
            for (int i = 0; i < 3; i++)
            {
                var r = new Rect(W * 0.5f - 200 + i * 104, by + 8, 96, 34);
                if (UiTheme.Btn(r, diffKo[i], p.difficulty == diffs[i] ? UiTheme.ButtonTabOn : UiTheme.ButtonTab)) { p.difficulty = diffs[i]; p.Save(); }
            }
            var start = new Rect(W * 0.5f + 130, by - 4, 280, 64);
            if (UiTheme.Btn(start, "전투 시작", UiTheme.ButtonBig)) Root.StartBattle();
            UiTheme.DrawText(new Rect(W * 0.5f - 330, by + 50, 760, 22), "전적 " + p.totalBattles + "전 " + p.totalWins + "승  ·  보급 상자 " + p.supplyCrates + "개", UiTheme.LabelSmall, UiTheme.TextDim);
        }

        private float AvgCost(int[] deck)
        {
            float s = 0; int n = 0;
            foreach (int id in deck) { var d = Root.Data.Units.Get(id); if (d != null) { s += d.CpCost; n++; } }
            return n > 0 ? s / n : 0f;
        }

        private void MiniCard(Rect r, int id, bool highlight)
        {
            var def = Root.Data.Units.Get(id);
            if (def == null) return;
            var rc = UiTheme.RarityColor(def.Rarity);
            UiTheme.Fill(r, new Color(0.08f, 0.09f, 0.11f, 0.95f), 4f);
            var pr = new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 32);
            var tex = Root.Portraits.Get(id);
            if (tex != null) GUI.DrawTexture(pr, tex, ScaleMode.ScaleAndCrop);
            else UiTheme.DrawText(pr, UiTheme.SectionGlyph(def), new GUIStyle(UiTheme.LabelCenterBold) { fontSize = 30 }, UiTheme.TextDim);
            UiTheme.Fill(new Rect(r.x + 3, r.yMax - 29, r.width - 6, 2), rc);
            UiTheme.DrawText(new Rect(r.x + 2, r.yMax - 27, r.width - 4, 24), def.NameKo, new GUIStyle(UiTheme.LabelCenter) { fontSize = 11, fontStyle = FontStyle.Bold });
            var badge = new Rect(r.x + 2, r.y + 2, 24, 24);
            UiTheme.Fill(badge, new Color(0.85f, 0.65f, 0.15f), 12f);
            UiTheme.DrawText(badge, def.CpCost.ToString(), new GUIStyle(UiTheme.LabelCenterBold) { fontSize = 14 });
            UiTheme.Frame(r, highlight ? UiTheme.Gold : rc.WithAlpha(0.7f), highlight ? 3f : 1.5f, 4f);
        }

        // ------------------------------------------------------------------ deck builder

        private bool PassFilter(UnitDefinition d)
        {
            switch (_filter)
            {
                case 1: return d.Section == UnitSection.Infantry;
                case 2: return d.Section == UnitSection.Armor;
                case 3: return d.Section == UnitSection.Elite;
                case 4: return d.Section == UnitSection.Air;
                case 5: return d.Section == UnitSection.Structure;
                case 6: return d.Section == UnitSection.Hardcore;
                default: return true;
            }
        }

        private void DrawFilters(float x, float y)
        {
            for (int i = 0; i < FilterNames.Length; i++)
                if (UiTheme.Btn(new Rect(x + i * 118, y, 112, 34), FilterNames[i], _filter == i ? UiTheme.ButtonTabOn : UiTheme.ButtonTab)) { _filter = i; _scroll = Vector2.zero; }
        }

        private void DrawDeck(PlayerProfile p, float W, float H)
        {
            DrawSideMenu(H);
            var panel = new Rect(296, 90, W - 320, H - 150);
            UiTheme.PanelBox(panel);
            UiTheme.DrawText(new Rect(panel.x + 16, panel.y + 8, 600, 30), "부대 편성 - 74종 중 8장을 선택하세요", UiTheme.LabelBold, UiTheme.Gold);
            DrawFilters(panel.x + 16, panel.y + 44);

            float cw = 104, ch = 132, gap = 10;
            int cols = Mathf.Max(1, (int)((panel.width - 40) / (cw + gap)));
            var list = new List<UnitDefinition>();
            foreach (var d in Root.Data.Units.Cards) if (PassFilter(d)) list.Add(d);
            int rows = Mathf.CeilToInt(list.Count / (float)cols);
            var view = new Rect(panel.x + 16, panel.y + 90, panel.width - 32, panel.height - 270);
            var content = new Rect(0, 0, view.width - 20, rows * (ch + gap));
            _scroll = GUI.BeginScrollView(view, _scroll, content);
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i];
                var r = new Rect((i % cols) * (cw + gap), (i / cols) * (ch + gap), cw, ch);
                bool inDeck = _deckEdit.Contains(d.Id);
                MiniCard(r, d.Id, inDeck);
                if (inDeck) UiTheme.DrawText(new Rect(r.xMax - 26, r.y + 2, 24, 22), "✔", UiTheme.LabelCenterBold, UiTheme.Gold);
                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                {
                    if (inDeck) _deckEdit.Remove(d.Id);
                    else if (_deckEdit.Count < 8) _deckEdit.Add(d.Id);
                    else Toast("덱은 8장까지입니다. 먼저 카드를 빼세요.");
                    Event.current.Use();
                }
            }
            GUI.EndScrollView();

            // Selected deck row.
            float y = panel.yMax - 170;
            UiTheme.DrawText(new Rect(panel.x + 16, y, 500, 24), "선택한 카드 " + _deckEdit.Count + " / 8  ·  평균 CP " + AvgCost(_deckEdit.ToArray()).ToString("0.0") + "  (클릭하여 제외)", UiTheme.Label);
            for (int i = 0; i < 8; i++)
            {
                var r = new Rect(panel.x + 16 + i * 98, y + 28, 90, 112);
                if (i < _deckEdit.Count)
                {
                    MiniCard(r, _deckEdit[i], false);
                    if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition)) { _deckEdit.RemoveAt(i); Event.current.Use(); break; }
                }
                else { UiTheme.Fill(r, new Color(0, 0, 0, 0.4f), 4f); UiTheme.Frame(r, UiTheme.Line, 1f, 4f); }
            }
            GUI.enabled = _deckEdit.Count == 8;
            if (UiTheme.Btn(new Rect(panel.xMax - 220, y + 30, 200, 50), "편성 저장", UiTheme.ButtonBig)) { p.deck = _deckEdit.ToArray(); p.Save(); Toast("부대 편성이 저장되었습니다."); }
            GUI.enabled = true;
            if (UiTheme.Btn(new Rect(panel.xMax - 220, y + 90, 200, 40), "기본 덱으로")) { _deckEdit.Clear(); _deckEdit.AddRange(Root.Data.Config.PlayerDefaultDeck); }
        }

        // ------------------------------------------------------------------ codex

        private void DrawCodex(float W, float H)
        {
            DrawSideMenu(H);
            var panel = new Rect(296, 90, W - 320, H - 150);
            UiTheme.PanelBox(panel);
            UiTheme.DrawText(new Rect(panel.x + 16, panel.y + 8, 600, 30), "유닛 도감 (74종)", UiTheme.LabelBold, UiTheme.Gold);
            DrawFilters(panel.x + 16, panel.y + 44);

            var listRect = new Rect(panel.x + 16, panel.y + 90, 330, panel.height - 106);
            var list = new List<UnitDefinition>();
            foreach (var d in Root.Data.Units.Cards) if (PassFilter(d)) list.Add(d);
            var content = new Rect(0, 0, listRect.width - 20, list.Count * 34);
            _scroll2 = GUI.BeginScrollView(listRect, _scroll2, content);
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i];
                var r = new Rect(0, i * 34, content.width, 30);
                bool sel = d.Id == _codexId;
                UiTheme.Fill(r, sel ? new Color(0.15f, 0.3f, 0.55f, 0.9f) : new Color(0.1f, 0.11f, 0.13f, 0.9f), 3f);
                UiTheme.Fill(new Rect(r.x, r.y, 4, r.height), UiTheme.RarityColor(d.Rarity));
                UiTheme.DrawText(new Rect(r.x + 10, r.y, r.width - 60, r.height), "#" + d.Id.ToString("00") + "  " + d.NameKo, UiTheme.LabelSmall);
                UiTheme.DrawText(new Rect(r.xMax - 50, r.y, 46, r.height), "CP " + d.CpCost, new GUIStyle(UiTheme.LabelSmall) { alignment = TextAnchor.MiddleRight }, UiTheme.Gold);
                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition)) { _codexId = d.Id; Event.current.Use(); }
            }
            GUI.EndScrollView();

            var def = Root.Data.Units.Get(_codexId);
            if (def == null) return;
            var detail = new Rect(listRect.xMax + 20, panel.y + 90, panel.xMax - listRect.xMax - 36, panel.height - 106);
            var preview = new Rect(detail.x, detail.y, Mathf.Min(420, detail.width * 0.5f), Mathf.Min(420, detail.width * 0.5f));
            var live = Root.Portraits.LivePreview(def.Id);
            if (live != null) GUI.DrawTexture(preview, live, ScaleMode.ScaleToFit, false);
            UiTheme.Frame(preview, UiTheme.RarityColor(def.Rarity), 2f, 4f);
            UiTheme.DrawText(new Rect(preview.x + 8, preview.yMax - 26, preview.width, 22), "3D 미리보기 (플레이스홀더 모델)", UiTheme.LabelSmall, UiTheme.TextDim);

            float sx = preview.xMax + 20, sw = detail.xMax - sx;
            UiTheme.DrawText(new Rect(sx, detail.y, sw, 36), "#" + def.Id + " " + def.NameKo, new GUIStyle(UiTheme.LabelBold) { fontSize = 26 });
            UiTheme.DrawText(new Rect(sx, detail.y + 38, sw, 22), "<color=#" + ColorUtility.ToHtmlStringRGB(UiTheme.RarityColor(def.Rarity)) + ">" + def.RarityKo + "</color>  ·  " + def.SectionKo + "  ·  " + def.Role, UiTheme.Label);
            string stats =
                "체력          " + def.Hp + (def.Count > 1 ? "  (x" + def.Count + ")" : "") + "\n" +
                "공격력        " + def.Attack + "\n" +
                "CP 비용       " + def.CpCost + (def.CardCooldown > 0 ? "   (재사용 " + def.CardCooldown + "초)" : "") + "\n" +
                "소환 수       " + def.Count + "\n" +
                "사거리        " + def.Range + " m\n" +
                "공격 간격     " + def.AttackInterval + " 초" + (def.Magazine > 0 ? "   탄창 " + def.Magazine + " / 재장전 " + def.ReloadTime + "초" : "") + "\n" +
                "이동 속도     " + def.MoveSpeed + " m/s\n" +
                "시야          " + def.Sight + " m\n" +
                "배치 방식     " + def.DeployKindKo + "\n" +
                "특수 능력     " + UnitDatabase.AbilityNameKo(def.Ability);
            UiTheme.DrawText(new Rect(sx, detail.y + 70, sw, 260), stats, new GUIStyle(UiTheme.Label) { alignment = TextAnchor.UpperLeft, fontSize = 15 });
            UiTheme.DrawText(new Rect(detail.x, preview.yMax + 16, detail.width, 120), def.Description + "\n\n<color=#8a96a3>※ 수치는 원본 기획서 수치가 없어 Resources/RoadToValor/Data/unit_balance.csv 의 제안값을 표시합니다.</color>",
                new GUIStyle(UiTheme.Label) { wordWrap = true, alignment = TextAnchor.UpperLeft });
        }

        // ------------------------------------------------------------------ research / logistics / records / shop

        private void DrawResearch(PlayerProfile p, float W, float H)
        {
            DrawSideMenu(H);
            var panel = new Rect(296, 90, Mathf.Min(900, W - 320), 520);
            UiTheme.PanelBox(panel);
            UiTheme.DrawText(new Rect(panel.x + 16, panel.y + 8, 600, 30), "연구소 - 영구 강화 (제안값)", UiTheme.LabelBold, UiTheme.Gold);
            var tracks = Root.Data.Config.Research;
            for (int i = 0; i < tracks.Count && i < p.research.Length; i++)
            {
                var t = tracks[i];
                var r = new Rect(panel.x + 16, panel.y + 56 + i * 110, panel.width - 32, 100);
                UiTheme.Fill(r, UiTheme.PanelLight, 4f);
                int lv = p.research[i];
                UiTheme.DrawText(new Rect(r.x + 14, r.y + 8, 400, 26), t.Name + "  <color=#8a96a3>Lv " + lv + " / " + t.MaxLevel + "</color>", UiTheme.LabelBold);
                UiTheme.DrawText(new Rect(r.x + 14, r.y + 36, 500, 22), "현재 보너스 +" + Mathf.RoundToInt(lv * t.PerLevel * 100f) + "%   (레벨당 +" + Mathf.RoundToInt(t.PerLevel * 100f) + "%)", UiTheme.Label, UiTheme.TextDim);
                UiTheme.Bar(new Rect(r.x + 14, r.y + 66, 480, 12), lv / (float)Mathf.Max(1, t.MaxLevel), UiTheme.MilBlue, new Color(0, 0, 0, 0.5f));
                int cost = t.BaseCost * (lv + 1);
                GUI.enabled = lv < t.MaxLevel && p.gold >= cost;
                if (UiTheme.Btn(new Rect(r.xMax - 220, r.y + 24, 200, 52), lv >= t.MaxLevel ? "최대 레벨" : "연구 (" + cost + " 골드)", UiTheme.ButtonTabOn))
                {
                    p.gold -= cost;
                    p.research[i]++;
                    p.Save();
                    Toast(t.Name + " 연구 완료!");
                }
                GUI.enabled = true;
            }
        }

        private void DrawLogistics(PlayerProfile p, float W, float H)
        {
            DrawSideMenu(H);
            var panel = new Rect(296, 90, Mathf.Min(820, W - 320), 400);
            UiTheme.PanelBox(panel);
            UiTheme.DrawText(new Rect(panel.x + 16, panel.y + 8, 600, 30), "군수 보급", UiTheme.LabelBold, UiTheme.Gold);
            string today = System.DateTime.Now.ToString("yyyy-MM-dd");
            bool claimed = p.lastSupplyDate == today;
            var r1 = new Rect(panel.x + 16, panel.y + 56, panel.width - 32, 120);
            UiTheme.Fill(r1, UiTheme.PanelLight, 4f);
            UiTheme.DrawText(new Rect(r1.x + 14, r1.y + 10, 500, 26), "일일 보급품", UiTheme.LabelBold);
            UiTheme.DrawText(new Rect(r1.x + 14, r1.y + 40, 500, 60), "매일 골드 300 + 보석 10 지급", UiTheme.Label, UiTheme.TextDim);
            GUI.enabled = !claimed;
            if (UiTheme.Btn(new Rect(r1.xMax - 220, r1.y + 34, 200, 52), claimed ? "오늘 수령 완료" : "수령", UiTheme.ButtonTabOn))
            {
                p.lastSupplyDate = today; p.gold += 300; p.gems += 10; p.Save(); Toast("보급품 수령: 골드 +300, 보석 +10");
            }
            GUI.enabled = true;
            var r2 = new Rect(panel.x + 16, panel.y + 190, panel.width - 32, 120);
            UiTheme.Fill(r2, UiTheme.PanelLight, 4f);
            UiTheme.DrawText(new Rect(r2.x + 14, r2.y + 10, 500, 26), "보급 상자  x" + p.supplyCrates, UiTheme.LabelBold);
            UiTheme.DrawText(new Rect(r2.x + 14, r2.y + 40, 520, 60), "전투 승리 시 1개 획득. 열면 골드 150~450", UiTheme.Label, UiTheme.TextDim);
            GUI.enabled = p.supplyCrates > 0;
            if (UiTheme.Btn(new Rect(r2.xMax - 220, r2.y + 34, 200, 52), "상자 열기", UiTheme.ButtonTabOn))
            {
                int g = Random.Range(150, 451);
                p.supplyCrates--; p.gold += g; p.Save(); Toast("보급 상자: 골드 +" + g);
            }
            GUI.enabled = true;
        }

        private void DrawRecords(PlayerProfile p, float W, float H)
        {
            DrawSideMenu(H);
            var panel = new Rect(296, 90, Mathf.Min(1000, W - 320), H - 150);
            UiTheme.PanelBox(panel);
            float rate = p.totalBattles > 0 ? p.totalWins * 100f / p.totalBattles : 0f;
            UiTheme.DrawText(new Rect(panel.x + 16, panel.y + 8, 800, 30), "전적 기록  ·  " + p.totalBattles + "전 " + p.totalWins + "승 (" + rate.ToString("0") + "%)", UiTheme.LabelBold, UiTheme.Gold);
            var view = new Rect(panel.x + 16, panel.y + 48, panel.width - 32, panel.height - 64);
            var content = new Rect(0, 0, view.width - 20, Mathf.Max(1, p.records.Count) * 44);
            _scroll = GUI.BeginScrollView(view, _scroll, content);
            if (p.records.Count == 0) UiTheme.DrawText(new Rect(0, 0, 500, 30), "아직 전투 기록이 없습니다.", UiTheme.Label, UiTheme.TextDim);
            for (int i = 0; i < p.records.Count; i++)
            {
                var m = p.records[i];
                var r = new Rect(0, i * 44, content.width, 40);
                UiTheme.Fill(r, UiTheme.PanelLight, 3f);
                UiTheme.Fill(new Rect(r.x, r.y, 5, r.height), m.victory ? UiTheme.MilBlue : UiTheme.WarnRed);
                int t = Mathf.FloorToInt(m.duration);
                UiTheme.DrawText(new Rect(r.x + 14, r.y, r.width - 20, r.height),
                    (m.victory ? "<color=#5aa0ff><b>승리</b></color>" : "<color=#ff5a4a><b>패배</b></color>") + "   " + m.date + "   " + (t / 60) + "분 " + (t % 60) + "초   격파 " + m.kills + "  손실 " + m.losses + "  구역 " + m.zones + "   난이도 " + m.difficulty + "   골드 +" + m.goldReward,
                    UiTheme.LabelSmall);
            }
            GUI.EndScrollView();
        }

        private void DrawShop(PlayerProfile p, float W, float H)
        {
            DrawSideMenu(H);
            var panel = new Rect(296, 90, Mathf.Min(820, W - 320), 420);
            UiTheme.PanelBox(panel);
            UiTheme.DrawText(new Rect(panel.x + 16, panel.y + 8, 600, 30), "상점", UiTheme.LabelBold, UiTheme.Gold);
            UiTheme.DrawText(new Rect(panel.x + 16, panel.y + 40, 760, 22), "※ 프로토타입: 실제 결제는 구현되어 있지 않습니다. 보석 ↔ 골드 교환만 동작합니다.", UiTheme.LabelSmall, UiTheme.TextDim);
            Offer(p, new Rect(panel.x + 16, panel.y + 76, panel.width - 32, 90), "골드 보급 (소)", 50, 1000);
            Offer(p, new Rect(panel.x + 16, panel.y + 176, panel.width - 32, 90), "골드 보급 (대)", 100, 2200);
            var r = new Rect(panel.x + 16, panel.y + 276, panel.width - 32, 90);
            UiTheme.Fill(r, UiTheme.PanelLight, 4f);
            UiTheme.DrawText(new Rect(r.x + 14, r.y + 10, 500, 26), "테스트용 보석 지급", UiTheme.LabelBold);
            UiTheme.DrawText(new Rect(r.x + 14, r.y + 42, 500, 26), "개발/플레이테스트 편의 기능", UiTheme.Label, UiTheme.TextDim);
            if (UiTheme.Btn(new Rect(r.xMax - 220, r.y + 20, 200, 50), "보석 +50 (무료)", UiTheme.ButtonTabOn)) { p.gems += 50; p.Save(); Toast("보석 +50"); }
        }

        private void Offer(PlayerProfile p, Rect r, string name, int gems, int gold)
        {
            UiTheme.Fill(r, UiTheme.PanelLight, 4f);
            UiTheme.DrawText(new Rect(r.x + 14, r.y + 10, 500, 26), name, UiTheme.LabelBold);
            UiTheme.DrawText(new Rect(r.x + 14, r.y + 42, 500, 26), "보석 " + gems + " → 골드 " + gold.ToString("N0"), UiTheme.Label, UiTheme.TextDim);
            GUI.enabled = p.gems >= gems;
            if (UiTheme.Btn(new Rect(r.xMax - 220, r.y + 20, 200, 50), "교환", UiTheme.ButtonTabOn)) { p.gems -= gems; p.gold += gold; p.Save(); Toast("골드 +" + gold); }
            GUI.enabled = true;
        }
    }
}
