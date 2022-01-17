namespace Zergatul.Obs.InputOverlay.Device
{
    public class GamepadButton
    {
        public int Index { get; }
        public bool Pressed { get; set; }

        public GamepadButton(int index)
        {
            Index = index;
        }
    }
}