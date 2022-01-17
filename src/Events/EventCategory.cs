namespace Zergatul.Obs.InputOverlay.Events
{
    public enum EventCategory : int
    {
        Keyboard = 0x0001,
        MouseButtons = 0x0002,
        RawMouseMovement = 0x0004, /* Raw mouse data from driver */
        MouseMovement = 0x0008     /* Not supported */,
        Devices = 0x0010,
        GamepadButtons = 0x0020,
        GamepadAxes = 0x0040
    }
}