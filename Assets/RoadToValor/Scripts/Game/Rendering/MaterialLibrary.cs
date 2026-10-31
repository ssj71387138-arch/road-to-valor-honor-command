using UnityEngine;

namespace RoadToValor.Game
{
    /// <summary>
    /// Creates materials that work in both URP (target) and the Built-in pipeline (fallback when the
    /// project has no URP asset assigned), so the game never renders magenta.
    /// </summary>
    public static class MaterialLibrary
    {
        private static Material _palette;
        private static Material _paletteNoShadow;
        private static Material _particle;
        private static Shader _lit;
        private static Shader _unlit;

        public static bool IsUrp => UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;

        public static Shader LitShader
        {
            get
            {
                if (_lit != null) return _lit;
                string[] names = IsUrp
                    ? new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Unlit" }
                    : new[] { "Standard", "Legacy Shaders/Diffuse", "Unlit/Texture" };
                foreach (var n in names)
                {
                    _lit = Shader.Find(n);
                    if (_lit != null) break;
                }
                if (_lit == null) Debug.LogError("[RTV] No lit shader found. Add 'Universal Render Pipeline/Lit' (URP) or 'Standard' to Always Included Shaders.");
                return _lit;
            }
        }

        public static Shader UnlitShader
        {
            get
            {
                if (_unlit != null) return _unlit;
                _unlit = Shader.Find("Sprites/Default");
                if (_unlit == null) _unlit = Shader.Find(IsUrp ? "Universal Render Pipeline/Unlit" : "Unlit/Color");
                return _unlit;
            }
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public static void SetTexture(Material m, Texture t)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
        }

        public static Material CreateLit(Color color, Texture tex = null, float smoothness = 0.15f, float metallic = 0f)
        {
            var m = new Material(LitShader) { name = "RTV_Lit" };
            SetColor(m, color);
            if (tex != null) SetTexture(m, tex);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            m.enableInstancing = true;
            return m;
        }

        public static Material CreateEmissive(Color baseColor, Color emission)
        {
            var m = CreateLit(baseColor, null, 0.6f);
            m.EnableKeyword("_EMISSION");
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return m;
        }

        /// <summary>Unlit, alpha-blended, vertex-coloured (rings, markers, tracers, beams).</summary>
        public static Material CreateUnlit(Color color)
        {
            var m = new Material(UnlitShader) { name = "RTV_Unlit" };
            m.color = color;
            SetColor(m, color);
            return m;
        }

        /// <summary>The one shared material used by all palette-mapped procedural meshes.</summary>
        public static Material PaletteMaterial
        {
            get
            {
                if (_palette != null) return _palette;
                _palette = CreateLit(Color.white, Palette.Texture, 0.12f);
                _palette.name = "RTV_Palette";
                return _palette;
            }
        }

        public static Material PaletteNoShadowMaterial
        {
            get
            {
                if (_paletteNoShadow != null) return _paletteNoShadow;
                _paletteNoShadow = new Material(PaletteMaterial) { name = "RTV_PaletteWreck" };
                SetColor(_paletteNoShadow, new Color(0.32f, 0.3f, 0.29f));
                return _paletteNoShadow;
            }
        }

        public static Material ParticleMaterial
        {
            get
            {
                if (_particle != null) return _particle;
                _particle = new Material(UnlitShader) { name = "RTV_Particle" };
                _particle.mainTexture = TextureGen.SoftCircle;
                return _particle;
            }
        }

        public static Material CreateWater()
        {
            var m = CreateLit(new Color(0.12f, 0.2f, 0.26f), null, 0.92f, 0.1f);
            m.name = "RTV_Water";
            return m;
        }

        public static Material CreateTerrainMaterial()
        {
            string name = IsUrp ? "Universal Render Pipeline/Terrain/Lit" : "Nature/Terrain/Standard";
            var s = Shader.Find(name);
            if (s == null && !IsUrp) s = Shader.Find("Nature/Terrain/Diffuse");
            return s != null ? new Material(s) { name = "RTV_Terrain" } : null;
        }
    }
}
