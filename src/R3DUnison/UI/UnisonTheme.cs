using System;
using System.Linq;
using UnityEngine;

namespace R3DUnison.UI
{
    /// <summary>
    /// The R3D design language, ported from r3d.css: near-black warm ink surfaces,
    /// crimson accent (#E0455E), mono type for buttons/metadata, rounded cards.
    /// All textures are generated at runtime (rounded-rect SDF), authored for a
    /// 1080p-scaled GUI.matrix.
    /// </summary>
    public static class UnisonTheme
    {
        // Palette (sRGB approximations of the oklch tokens)
        public static readonly Color Red400 = Hex("EF5F72");
        public static readonly Color Red500 = Hex("E0455E");
        public static readonly Color Red600 = Hex("BF2F45");
        public static readonly Color Ink0 = Hex("0D0A0B");
        public static readonly Color Ink100 = Hex("1B1617");
        public static readonly Color Ink200 = Hex("262021");
        public static readonly Color Ink300 = Hex("383132");
        public static readonly Color Ink500 = Hex("6F6667");
        public static readonly Color Ink700 = Hex("B5AEAF");
        public static readonly Color Ink800 = Hex("E4DFDF");
        public static readonly Color Green = Hex("55C57E");

        public static GUIStyle Window, Title, TitleTag, Header, Label, Name, Value, Dim, Status,
            LevelText, Card, Row, Button, ButtonPrimary, TextField, Overlay, OverlayHead,
            DotOn, DotOff, DotDead, DeadText, ChipHost, ChipYou;

        private static GUIStyle _accentBar, _toggleOn, _toggleOff;
        private static bool _ready;

        public static void Ensure()
        {
            if (_ready) return;
            _ready = true;
            Build();
        }

        private static Color Hex(string hex, float a = 1f)
        {
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return new Color(r / 255f, g / 255f, b / 255f, a);
        }

        private static Color WithA(Color c, float a)
        {
            c.a = a;
            return c;
        }

        // Rounded-rect SDF texture; border ring drawn inside the edge when requested.
        private static Texture2D Rounded(int size, int radius, Color fill, Color? border = null, int borderW = 0)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color[size * size];
            float half = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float qx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - (half - radius), 0f);
                    float qy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - (half - radius), 0f);
                    float d = Mathf.Sqrt(qx * qx + qy * qy) - radius; // < 0 inside
                    Color c = border.HasValue && borderW > 0 && d > -borderW ? border.Value : fill;
                    c.a *= Mathf.Clamp01(0.5f - d);
                    px[y * size + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private static Texture2D Solid(Color c)
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixels(Enumerable.Repeat(c, 16).ToArray());
            tex.Apply();
            return tex;
        }

        private static void Build()
        {
            // NOTE: no runtime OS fonts — Unity's dynamic font atlas corrupts IMGUI text
            // (blank buttons, glyphs swapped between labels; seen 2026-07-02). Default
            // font + weights only; the R3D look is carried by color/shape.
            var windowTex = Rounded(64, 18, WithA(Ink0, 0.97f), Ink300, 2);
            Window = new GUIStyle
            {
                normal = { background = windowTex, textColor = Ink800 },
                border = new RectOffset(24, 24, 24, 24),
                padding = new RectOffset(24, 24, 20, 18),
            };

            Title = new GUIStyle { fontSize = 32, fontStyle = FontStyle.Bold, normal = { textColor = Ink800 } };

            TitleTag = new GUIStyle { fontSize = 13, normal = { textColor = Ink500 }, alignment = TextAnchor.MiddleRight, padding = new RectOffset(0, 0, 12, 0) };

            Header = new GUIStyle { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = Ink700 } };

            Label = new GUIStyle { fontSize = 17, normal = { textColor = Ink700 }, wordWrap = false };

            Name = new GUIStyle(Label) { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = Ink800 } };

            Value = new GUIStyle(Name) { alignment = TextAnchor.MiddleCenter };

            Dim = new GUIStyle(Label) { fontSize = 15, normal = { textColor = Ink500 } };

            Status = new GUIStyle { fontSize = 13, normal = { textColor = Ink500 }, wordWrap = true };

            LevelText = new GUIStyle { fontSize = 14, normal = { textColor = Red400 } };

            Card = new GUIStyle
            {
                normal = { background = Rounded(48, 12, Ink100, Ink300, 1) },
                border = new RectOffset(16, 16, 16, 16),
                padding = new RectOffset(16, 16, 12, 12),
                margin = new RectOffset(0, 0, 0, 8),
            };

            Row = new GUIStyle(Card)
            {
                padding = new RectOffset(14, 14, 9, 9),
                margin = new RectOffset(0, 0, 0, 6),
            };

            Button = new GUIStyle
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = Rounded(40, 10, Ink200, Ink300, 1), textColor = Ink700 },
                hover = { background = Rounded(40, 10, Ink200, Red500, 1), textColor = Ink800 },
                active = { background = Rounded(40, 10, Ink300, Red500, 1), textColor = Ink800 },
                border = new RectOffset(12, 12, 12, 12),
                padding = new RectOffset(16, 16, 9, 9),
            };

            ButtonPrimary = new GUIStyle(Button)
            {
                normal = { background = Rounded(40, 10, Red500), textColor = Color.white },
                hover = { background = Rounded(40, 10, Red400), textColor = Color.white },
                active = { background = Rounded(40, 10, Red600), textColor = Color.white },
            };

            TextField = new GUIStyle
            {
                fontSize = 16,
                normal = { background = Rounded(40, 10, Ink100, Ink300, 1), textColor = Ink800 },
                focused = { background = Rounded(40, 10, Ink100, Red500, 1), textColor = Ink800 },
                hover = { background = Rounded(40, 10, Ink100, Ink300, 1), textColor = Ink800 },
                border = new RectOffset(12, 12, 12, 12),
                padding = new RectOffset(12, 12, 8, 8),
                clipping = TextClipping.Clip,
            };

            Overlay = new GUIStyle
            {
                normal = { background = Rounded(48, 12, WithA(Ink0, 0.85f), Ink300, 1) },
                border = new RectOffset(16, 16, 16, 16),
                padding = new RectOffset(14, 14, 10, 10),
            };

            OverlayHead = new GUIStyle { fontSize = 11, normal = { textColor = Red400 }, fontStyle = FontStyle.Bold };

            DotOn = new GUIStyle { fontSize = 15, normal = { textColor = Green }, padding = new RectOffset(0, 6, 2, 0) };
            DotOff = new GUIStyle(DotOn) { normal = { textColor = Ink500 } };
            DotDead = new GUIStyle(DotOn) { normal = { textColor = Red500 } };
            DeadText = new GUIStyle { fontSize = 14, normal = { textColor = Red500 } };

            ChipHost = new GUIStyle { fontSize = 11, normal = { textColor = Red400 }, padding = new RectOffset(8, 0, 6, 0) };
            ChipYou = new GUIStyle(ChipHost) { normal = { textColor = Ink500 } };

            _accentBar = new GUIStyle { normal = { background = Solid(Red500) }, fixedHeight = 3, stretchWidth = true, margin = new RectOffset(0, 0, 6, 0) };
            _toggleOn = new GUIStyle { normal = { background = Rounded(26, 7, Red500) }, hover = { background = Rounded(26, 7, Red400) }, fixedWidth = 24, fixedHeight = 24 };
            _toggleOff = new GUIStyle { normal = { background = Rounded(26, 7, Ink100, Ink500, 2) }, hover = { background = Rounded(26, 7, Ink100, Red500, 2) }, fixedWidth = 24, fixedHeight = 24 };
        }

        public static void AccentBar() => GUILayout.Box(GUIContent.none, _accentBar);

        public static bool Toggle(bool value, string label)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(GUIContent.none, value ? _toggleOn : _toggleOff)) value = !value;
            GUILayout.Space(8);
            GUILayout.Label(label, Dim);
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
