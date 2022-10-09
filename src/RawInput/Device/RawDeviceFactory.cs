using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Zergatul.Obs.InputOverlay.RawInput.Device
{
    using static WinApi.User32;
    using static WinApiHelper;

    public class RawDeviceFactory : IRawDeviceFactory
    {
        private readonly ILogger _logger;
        private readonly NativeMemoryBuffer _buffer;

        public RawDeviceFactory(ILogger<RawDeviceFactory> logger)
        {
            _logger = logger;
            _buffer = new NativeMemoryBuffer();
        }

        public void Dispose()
        {
            _buffer.Dispose();
        }

        public RawDevice FromHDevice(IntPtr hDevice)
        {
            RID_DEVICE_INFO info = default;
            int size = Marshal.SizeOf<RID_DEVICE_INFO>();
            if (GetRawInputDeviceInfoW(hDevice, GetRawDeviceInfoCommand.RIDI_DEVICEINFO, ref info, ref size) < 0)
            {
                _logger.LogError($"Cannot get raw input device info {FormatWin32Error(Marshal.GetLastWin32Error())}.");
                return null;
            }

            size = 0;
            if (GetRawInputDeviceInfoW(hDevice, GetRawDeviceInfoCommand.RIDI_DEVICENAME, null, ref size) < 0)
            {
                _logger.LogError($"Cannot get raw input device name length {FormatWin32Error(Marshal.GetLastWin32Error())}.");
                return null;
            }

            var sb = new StringBuilder(size);
            if (GetRawInputDeviceInfoW(hDevice, GetRawDeviceInfoCommand.RIDI_DEVICENAME, sb, ref size) < 0)
            {
                _logger.LogError($"Cannot get raw input device name {FormatWin32Error(Marshal.GetLastWin32Error())}.");
                return null;
            }

            return info.dwType switch
            {
                RawInputType.RIM_TYPEMOUSE => new RawMouseDevice(hDevice, info.mouse),
                RawInputType.RIM_TYPEKEYBOARD => new RawKeyboardDevice(hDevice, info.keyboard),
                _ => throw new InvalidOperationException("Invalid dwType."),
            };
        }
    }
}