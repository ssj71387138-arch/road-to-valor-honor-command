// Compile-check stub mirroring the subset of com.unity.inputsystem used by GameInput.cs.
// NOT shipped to Unity (lives outside Assets/).
namespace UnityEngine.InputSystem
{
    public enum Key
    {
        None, Space, Enter, Tab, Backquote, Quote, Semicolon, Comma, Period, Slash, Backslash, LeftBracket, RightBracket, Minus, Equals,
        A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9, Digit0,
        LeftShift, RightShift, LeftAlt, RightAlt, LeftCtrl, RightCtrl, LeftMeta, RightMeta, ContextMenu, Escape,
        LeftArrow, RightArrow, UpArrow, DownArrow, Backspace, PageDown, PageUp, Home, End, Insert, Delete, CapsLock, NumLock, PrintScreen, ScrollLock, Pause,
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    }
    public class ButtonControl { public bool isPressed => false; public bool wasPressedThisFrame => false; public bool wasReleasedThisFrame => false; }
    public class KeyControl : ButtonControl { }
    public class Vector2Control { public Vector2 ReadValue() => Vector2.zero; }
    public class Keyboard { public static Keyboard current => null; public KeyControl this[Key key] => null; }
    public class Mouse
    {
        public static Mouse current => null;
        public Vector2Control position => null;
        public Vector2Control scroll => null;
        public ButtonControl leftButton => null;
        public ButtonControl rightButton => null;
        public ButtonControl middleButton => null;
    }
}
