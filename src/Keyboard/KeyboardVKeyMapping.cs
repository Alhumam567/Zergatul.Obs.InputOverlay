using System.Collections.Generic;
using static System.Net.Mime.MediaTypeNames;
using System.Threading;

namespace Zergatul.Obs.InputOverlay.Keyboard
{
    public static class KeyboardVKeyMapping
    {
        public static readonly Dictionary<int, KeyboardButton> Dictionary = new Dictionary<int, KeyboardButton>
        {
            [0xAD] = KeyboardButton.Mute,
            [0xAE] = KeyboardButton.VolumeDown,
            [0xAF] = KeyboardButton.VolumeUp,
            [0xB0] = KeyboardButton.Next,
            [0xB1] = KeyboardButton.Previous,
            [0xB2] = KeyboardButton.Stop,
            [0xB3] = KeyboardButton.PlayPause
        };
    }
}
