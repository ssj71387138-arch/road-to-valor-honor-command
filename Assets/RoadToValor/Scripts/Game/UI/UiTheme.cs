using RoadToValor.Core;
using UnityEngine;

namespace RoadToValor.Game
{
    /// <summary>
    /// Military UI theme (black / dark grey / military blue / warning red) for the IMGUI-based HUD and lobby.
    /// Resolution independent: everything is laid out on a 1080p-tall virtual canvas.
    /// Korean text uses an OS font (Malgun Gothic / Apple SD Gothic Neo / Noto Sans CJK) unless a font is
    /// provided at Resources/RoadToValor/Fonts/UIFont.
    /// </summary>
    public static class UiTheme
    {
        public static readonly Color Black = new Color(0.03f, 0.035f, 0.04f, 0.92f);
        public static readonly Color Panel = new Color(0.07f, 0.08f, 0.09f, 0.88f);
        public static readonly Color PanelLight = new Color(0.13f, 0.14f, 0.16f, 0.92f);
        public static readonly Color Line = new Color(0.32f, 0.36f, 0.4f, 0.9f);
        public static readonly Color MilBlue = new Color(0.2f, 0.46f, 0.86f, 1f);
        public static readonly Color MilBlueDark = new Color(0.1f, 0.22f, 0.42f, 1f);
        public static readonly Color WarnRed = new Color(0.86f, 0.18f, 0.15f, 1f);
        public static readonly Color Gold = new Color(0.9f, 0.73f, 0.32f, 1f);
        public static readonly Color Text = new Color(0.9f, 0.92f, 0.94f, 1f);
        public static readonly Color TextDim = new Color(0.6f, 0.64f, 0.68f, 1f);
        public static readonly Color Green = new Color(0.35f, 0.85f, 0.45f, 1f);

        public const float RefHeight = 1080f;
        public static float Scale = 1f;
        public static float W = 1920f;
        public static float H = 1080f;

        private static Font _font;
        private static bool _init;
        public static GUIStyle Label, LabelSmall, LabelBold, LabelCenter, LabelCenterBold, Title, Big, Button, ButtonBig, ButtonTab, ButtonTabOn, Tooltip;

        public static void Begin()
        {
            Scale = Mathf.Clamp(Screen.height / RefHeight, 0.5f, 3f);
            W = Screen.width / Scale;
            H = Screen.height / Scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Scale, Scale, 1f));
            if (!_init) Init();
        }

        /// <summary>Converts Unity screen coordinates (origin bottom-left) to virtual GUI coordinates.</summary>
        public static Vector2 ScreenToGui(Vector2 screen) => new Vector2(screen.x / Scale, (Screen.height - screen.y) / Scale);
        public static Vector2 WorldToGui(Camera cam, Vector3 world, out bool inFront)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            inFront = sp.z > 0f;
            return new Vector2(sp.x / Scale, (Screen.height - sp.y) / Scale);
        }

        private static void Init()
        {
            _init = true;
            _font = Resources.Load<Font>("RoadToValor/Fonts/UIFont");
            if (_font == null)
            {
                try
                {
                    _font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "AppleSDGothicNeo-Regular", "Noto Sans CJK KR", "Noto Sans KR", "NanumGothic", "Gulim", "Arial Unicode MS", "Arial" }, 16);
                }
                catch { _font = null; }
            }
            Label = Make(16, FontStyle.Normal, TextAnchor.MiddleLeft);
            LabelSmall = Make(13, FontStyle.Normal, TextAnchor.MiddleLeft);
            LabelBold = Make(18, FontStyle.Bold, TextAnchor.MiddleLeft);
            LabelCenter = Make(16, FontStyle.Normal, TextAnchor.MiddleCenter);
            LabelCenterBold = Make(18, FontStyle.Bold, TextAnchor.MiddleCenter);
            Title = Make(34, FontStyle.Bold, TextAnchor.MiddleLeft);
            Big = Make(72, FontStyle.Bold, TextAnchor.MiddleCenter);
            Tooltip = Make(14, FontStyle.Normal, TextAnchor.UpperLeft);
            Tooltip.wordWrap = true;
            Button = MakeButton(16, new Color(0.14f, 0.16f, 0.18f, 0.95f), new Color(0.22f, 0.3f, 0.42f, 0.98f));
            ButtonBig = MakeButton(26, new Color(0.12f, 0.3f, 0.6f, 0.98f), new Color(0.2f, 0.45f, 0.85f, 1f));
            ButtonTab = MakeButton(15, new Color(0.1f, 0.11f, 0.13f, 0.9f), new Color(0.2f, 0.24f, 0.3f, 0.95f));
            ButtonTabOn = MakeButton(15, new Color(0.14f, 0.32f, 0.62f, 0.98f), new Color(0.2f, 0.42f, 0.78f, 1f));
        }

        private static GUIStyle Make(int size, FontStyle style, TextAnchor anchor)
        {
            var s = new GUIStyle { fontSize = size, fontStyle = style, alignment = anchor, richText = true, clipping = TextClipping.Clip };
            if (_font != null) s.font = _font;
            s.normal.textColor = Text;
            return s;
        }

        private static GUIStyle MakeButton(int size, Color bg, Color hover)
        {
            var s = Make(size, FontStyle.Bold, TextAnchor.MiddleCenter);
            s.normal.background = Solid(bg);
            s.hover.background = Solid(hover);
            s.active.background = Solid(hover * 1.15f);
            s.normal.textColor = Text;
            s.hover.textColor = Color.white;
            s.active.textColor = Color.white;
            s.border = new RectOffset(2, 2, 2, 2);
            return s;
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color[16];
            for (int i = 0; i < 16; i++)
            {
                int x = i % 4, y = i / 4;
                bool edge = x == 0 || y == 0 || x == 3 || y == 3;
                px[i] = edge ? new Color(c.r * 1.6f + 0.05f, c.g * 1.6f + 0.05f, c.b * 1.6f + 0.05f, c.a) : c;
            }
            t.SetPixels(px);
            t.Apply();
            t.filterMode = FilterMode.Point;
            return t;
        }

        // ------------------------------------------------------------------ drawing helpers

        public static void Fill(Rect r, Color c, float radius = 0f)
        {
            GUI.DrawTexture(r, TextureGen.White, ScaleMode.StretchToFill, true, 0f, c, 0f, radius);
        }

        public static void Frame(Rect r, Color c, float width = 1.5f, float radius = 0f)
        {
            GUI.DrawTexture(r, TextureGen.White, ScaleMode.StretchToFill, true, 0f, c, width, radius);
        }

        public static void PanelBox(Rect r, bool accent = false)
        {
            Fill(r, Panel, 4f);
            Frame(r, accent ? MilBlue : Line, 1.5f, 4f);
        }

        public static void Bar(Rect r, float t, Color fill, Color bg)
        {
            Fill(r, bg, 2f);
            if (t > 0f) Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(t), r.height), fill, 2f);
            Frame(r, new Color(0f, 0f, 0f, 0.7f), 1f, 2f);
        }

        public static void DrawText(Rect r, string text, GUIStyle style, Color? color = null, bool shadow = true)
        {
            var c = color ?? style.normal.textColor;
            var prev = style.normal.textColor;
            if (shadow)
            {
                style.normal.textColor = new Color(0f, 0f, 0f, 0.75f * c.a);
                GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            }
            style.normal.textColor = c;
            GUI.Label(r, text, style);
            style.normal.textColor = prev;
        }

        public static void DrawLine(Vector2 a, Vector2 b, Color c, float width)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            var saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(ang, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), TextureGen.White, ScaleMode.StretchToFill, true, 0f, c, 0f, 0f);
            GUI.matrix = saved;
        }

        public static Color RarityColor(Rarity r)
        {
            switch (r)
            {
                case Rarity.Common: return new Color(0.6f, 0.62f, 0.64f);
                case Rarity.Rare: return new Color(0.3f, 0.55f, 1f);
                case Rarity.Hero: return new Color(0.3f, 0.8f, 0.45f);
                case Rarity.Epic: return new Color(0.68f, 0.38f, 0.95f);
                case Rarity.Special: return new Color(0.95f, 0.55f, 0.15f);
                default: return new Color(0.95f, 0.25f, 0.2f);
            }
        }

        public static string SectionGlyph(UnitDefinition d)
        {
            if (d.IsStructure) return "▣";
            if (d.IsSkill) return "✈";
            if (d.IsAir) return "✈";
            if (d.IsVehicle) return "◆";
            return "▲";
        }

        public static bool Btn(Rect r, string text, GUIStyle style = null)
        {
            return GUI.Button(r, text, style ?? Button);
        }
    }
}
