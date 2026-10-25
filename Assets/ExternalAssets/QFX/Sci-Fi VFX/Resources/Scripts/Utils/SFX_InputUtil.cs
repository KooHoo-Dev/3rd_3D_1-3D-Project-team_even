using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// ReSharper disable once CheckNamespace
namespace QFX.SFX
{
    // New Input System wrapper. Every call is null-safe so the effects keep working
    // when no mouse / keyboard device is connected.
    public static class SFX_InputUtil
    {
        // 0 = Left, 1 = Right, 2 = Middle (same convention as the legacy Input.GetMouseButton)
        private static ButtonControl GetMouseButtonControl(int button)
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return null;

            switch (button)
            {
                case 0: return mouse.leftButton;
                case 1: return mouse.rightButton;
                case 2: return mouse.middleButton;
                default: return null;
            }
        }

        public static bool GetMouseButtonDown(int button)
        {
            var control = GetMouseButtonControl(button);
            return control != null && control.wasPressedThisFrame;
        }

        public static bool GetMouseButtonUp(int button)
        {
            var control = GetMouseButtonControl(button);
            return control != null && control.wasReleasedThisFrame;
        }

        public static bool GetMouseButton(int button)
        {
            var control = GetMouseButtonControl(button);
            return control != null && control.isPressed;
        }

        public static bool TryGetMousePosition(out Vector2 position)
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                position = Vector2.zero;
                return false;
            }

            position = mouse.position.ReadValue();
            return true;
        }

        public static bool GetKeyDown(Key key)
        {
            var keyboard = Keyboard.current;
            return keyboard != null && key != Key.None && keyboard[key].wasPressedThisFrame;
        }

        public static bool GetKeyUp(Key key)
        {
            var keyboard = Keyboard.current;
            return keyboard != null && key != Key.None && keyboard[key].wasReleasedThisFrame;
        }

        public static bool GetKey(Key key)
        {
            var keyboard = Keyboard.current;
            return keyboard != null && key != Key.None && keyboard[key].isPressed;
        }

        // Ray from the main camera through the mouse cursor.
        public static bool TryGetMouseRay(out Ray ray)
        {
            var camera = Camera.main;
            if (camera == null || !TryGetMousePosition(out var mousePosition))
            {
                ray = default;
                return false;
            }

            ray = camera.ScreenPointToRay(mousePosition);
            return true;
        }
    }
}
