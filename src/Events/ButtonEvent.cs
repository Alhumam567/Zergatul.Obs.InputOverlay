using Zergatul.Obs.InputOverlay.Keyboard;
using Zergatul.Obs.InputOverlay.Mouse;
using Zergatul.Obs.InputOverlay.RawInput.Device;

namespace Zergatul.Obs.InputOverlay.Events
{
    public readonly struct ButtonEvent
    {
        public KeyboardButton KeyboardButton { get; }
        public RawKeyboardEvent RawKeyboard { get; }
        public MouseButton MouseButton { get; }
        public bool Pressed { get; }
        public bool Held { get; }

        public int? Count { get; }

        public ButtonEvent(KeyboardButton button, RawKeyboardEvent rawKeyboard, bool pressed, bool held)
        {
            KeyboardButton = button;
            RawKeyboard = rawKeyboard;
            MouseButton = MouseButton.None;
            Pressed = pressed;
            Held = held;
            Count = null;
        }

        public ButtonEvent(MouseButton button, bool pressed)
        {
            KeyboardButton = KeyboardButton.None;
            RawKeyboard = default;
            MouseButton = button;
            Pressed = pressed;
            Held = false;
            Count = null;
        }

        public ButtonEvent(MouseButton button, int count)
        {
            KeyboardButton = KeyboardButton.None;
            RawKeyboard = default;
            MouseButton = button;
            Pressed = false;
            Held = false;
            Count = count;
        }
    }
}