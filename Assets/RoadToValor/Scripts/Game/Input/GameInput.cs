using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RoadToValor.Game
{
    public enum GameKey
    {
        W, A, S, D, H, F, Q, E, R, Space, Escape, Tab, Delete,
        LeftCtrl, RightCtrl, LeftShift, RightShift, LeftAlt,
        Up, Down, Left, Right,
        Alpha0, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5, Alpha6, Alpha7, Alpha8, Alpha9,
        F1, F2, F3, F4, F5, F6, F7, F8, F10,
    }

    /// <summary>
    /// Thin input wrapper that works with both the new Input System (default in Unity 6 templates)
    /// and the legacy Input Manager. All game code reads input through here.
    /// </summary>
    public static class GameInput
    {
#if ENABLE_INPUT_SYSTEM
        private static Key Map(GameKey k)
        {
            switch (k)
            {
                case GameKey.W: return Key.W;
                case GameKey.A: return Key.A;
                case GameKey.S: return Key.S;
                case GameKey.D: return Key.D;
                case GameKey.H: return Key.H;
                case GameKey.F: return Key.F;
                case GameKey.Q: return Key.Q;
                case GameKey.E: return Key.E;
                case GameKey.R: return Key.R;
                case GameKey.Space: return Key.Space;
                case GameKey.Escape: return Key.Escape;
                case GameKey.Tab: return Key.Tab;
                case GameKey.Delete: return Key.Delete;
                case GameKey.LeftCtrl: return Key.LeftCtrl;
                case GameKey.RightCtrl: return Key.RightCtrl;
                case GameKey.LeftShift: return Key.LeftShift;
                case GameKey.RightShift: return Key.RightShift;
                case GameKey.LeftAlt: return Key.LeftAlt;
                case GameKey.Up: return Key.UpArrow;
                case GameKey.Down: return Key.DownArrow;
                case GameKey.Left: return Key.LeftArrow;
                case GameKey.Right: return Key.RightArrow;
                case GameKey.Alpha0: return Key.Digit0;
                case GameKey.Alpha1: return Key.Digit1;
                case GameKey.Alpha2: return Key.Digit2;
                case GameKey.Alpha3: return Key.Digit3;
                case GameKey.Alpha4: return Key.Digit4;
                case GameKey.Alpha5: return Key.Digit5;
                case GameKey.Alpha6: return Key.Digit6;
                case GameKey.Alpha7: return Key.Digit7;
                case GameKey.Alpha8: return Key.Digit8;
                case GameKey.Alpha9: return Key.Digit9;
                case GameKey.F1: return Key.F1;
                case GameKey.F2: return Key.F2;
                case GameKey.F3: return Key.F3;
                case GameKey.F4: return Key.F4;
                case GameKey.F5: return Key.F5;
                case GameKey.F6: return Key.F6;
                case GameKey.F7: return Key.F7;
                case GameKey.F8: return Key.F8;
                default: return Key.F10;
            }
        }

        public static bool Held(GameKey k)
        {
            var kb = Keyboard.current;
            return kb != null && kb[Map(k)].isPressed;
        }

        public static bool KeyDown(GameKey k)
        {
            var kb = Keyboard.current;
            return kb != null && kb[Map(k)].wasPressedThisFrame;
        }

        public static bool KeyUp(GameKey k)
        {
            var kb = Keyboard.current;
            return kb != null && kb[Map(k)].wasReleasedThisFrame;
        }

        public static Vector2 MousePosition
        {
            get { var m = Mouse.current; return m != null ? m.position.ReadValue() : Vector2.zero; }
        }

        /// <summary>Wheel notches (+ = forward / zoom in).</summary>
        public static float Scroll
        {
            get
            {
                var m = Mouse.current;
                if (m == null) return 0f;
                float y = m.scroll.ReadValue().y;
                return Mathf.Abs(y) > 10f ? y / 120f : y;
            }
        }

        public static bool MouseDown(int button)
        {
            var m = Mouse.current;
            if (m == null) return false;
            return button == 0 ? m.leftButton.wasPressedThisFrame : (button == 1 ? m.rightButton.wasPressedThisFrame : m.middleButton.wasPressedThisFrame);
        }

        public static bool MouseHeld(int button)
        {
            var m = Mouse.current;
            if (m == null) return false;
            return button == 0 ? m.leftButton.isPressed : (button == 1 ? m.rightButton.isPressed : m.middleButton.isPressed);
        }

        public static bool MouseUp(int button)
        {
            var m = Mouse.current;
            if (m == null) return false;
            return button == 0 ? m.leftButton.wasReleasedThisFrame : (button == 1 ? m.rightButton.wasReleasedThisFrame : m.middleButton.wasReleasedThisFrame);
        }
#else
        private static KeyCode Map(GameKey k)
        {
            switch (k)
            {
                case GameKey.W: return KeyCode.W;
                case GameKey.A: return KeyCode.A;
                case GameKey.S: return KeyCode.S;
                case GameKey.D: return KeyCode.D;
                case GameKey.H: return KeyCode.H;
                case GameKey.F: return KeyCode.F;
                case GameKey.Q: return KeyCode.Q;
                case GameKey.E: return KeyCode.E;
                case GameKey.R: return KeyCode.R;
                case GameKey.Space: return KeyCode.Space;
                case GameKey.Escape: return KeyCode.Escape;
                case GameKey.Tab: return KeyCode.Tab;
                case GameKey.Delete: return KeyCode.Delete;
                case GameKey.LeftCtrl: return KeyCode.LeftControl;
                case GameKey.RightCtrl: return KeyCode.RightControl;
                case GameKey.LeftShift: return KeyCode.LeftShift;
                case GameKey.RightShift: return KeyCode.RightShift;
                case GameKey.LeftAlt: return KeyCode.LeftAlt;
                case GameKey.Up: return KeyCode.UpArrow;
                case GameKey.Down: return KeyCode.DownArrow;
                case GameKey.Left: return KeyCode.LeftArrow;
                case GameKey.Right: return KeyCode.RightArrow;
                case GameKey.Alpha0: return KeyCode.Alpha0;
                case GameKey.Alpha1: return KeyCode.Alpha1;
                case GameKey.Alpha2: return KeyCode.Alpha2;
                case GameKey.Alpha3: return KeyCode.Alpha3;
                case GameKey.Alpha4: return KeyCode.Alpha4;
                case GameKey.Alpha5: return KeyCode.Alpha5;
                case GameKey.Alpha6: return KeyCode.Alpha6;
                case GameKey.Alpha7: return KeyCode.Alpha7;
                case GameKey.Alpha8: return KeyCode.Alpha8;
                case GameKey.Alpha9: return KeyCode.Alpha9;
                case GameKey.F1: return KeyCode.F1;
                case GameKey.F2: return KeyCode.F2;
                case GameKey.F3: return KeyCode.F3;
                case GameKey.F4: return KeyCode.F4;
                case GameKey.F5: return KeyCode.F5;
                case GameKey.F6: return KeyCode.F6;
                case GameKey.F7: return KeyCode.F7;
                case GameKey.F8: return KeyCode.F8;
                default: return KeyCode.F10;
            }
        }

        public static bool Held(GameKey k) => Input.GetKey(Map(k));
        public static bool KeyDown(GameKey k) => Input.GetKeyDown(Map(k));
        public static bool KeyUp(GameKey k) => Input.GetKeyUp(Map(k));
        public static Vector2 MousePosition => Input.mousePosition;
        public static float Scroll => Input.mouseScrollDelta.y;
        public static bool MouseDown(int button) => Input.GetMouseButtonDown(button);
        public static bool MouseHeld(int button) => Input.GetMouseButton(button);
        public static bool MouseUp(int button) => Input.GetMouseButtonUp(button);
#endif

        public static bool Ctrl => Held(GameKey.LeftCtrl) || Held(GameKey.RightCtrl);
        public static bool Shift => Held(GameKey.LeftShift) || Held(GameKey.RightShift);

        public static readonly GameKey[] Digits =
        {
            GameKey.Alpha0, GameKey.Alpha1, GameKey.Alpha2, GameKey.Alpha3, GameKey.Alpha4,
            GameKey.Alpha5, GameKey.Alpha6, GameKey.Alpha7, GameKey.Alpha8, GameKey.Alpha9,
        };

        public static readonly GameKey[] CardKeys = { GameKey.F1, GameKey.F2, GameKey.F3, GameKey.F4, GameKey.F5, GameKey.F6, GameKey.F7, GameKey.F8 };
    }
}
