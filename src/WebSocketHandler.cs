using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Zergatul.Obs.InputOverlay.Events;
using Zergatul.Obs.InputOverlay.Keyboard;
using Zergatul.Obs.InputOverlay.Mouse;
using Zergatul.Obs.InputOverlay.RawInput;
using Zergatul.Obs.InputOverlay.RawInput.Device;

namespace Zergatul.Obs.InputOverlay
{
    public class WebSocketHandler : IWebSocketHandler
    {
        private readonly IRawDeviceInput _input;
        private readonly ILogger _logger;
        private readonly Random _rnd = new Random();
        private readonly List<WebSocketWrapper> _webSockets = new List<WebSocketWrapper>();
        private readonly EnumCache<KeyboardButton, string> _keyboardButtonCache;
        private readonly EnumCache<MouseButton, string> _mouseButtonCache;

        private Dictionary<KeyboardButton, int> keyboardPresses;
        private Dictionary<MouseButton, int> mousePresses;

        public WebSocketHandler(IRawDeviceInput input, ILogger<WebSocketHandler> logger)
        {
            _input = input;
            _logger = logger;

            _input.ButtonAction += OnButtonAction;
            _input.DeviceAction += OnDeviceAction;

            keyboardPresses = new Dictionary<KeyboardButton, int>();
            List<string> kbstr = new List<string>(); 
            foreach (KeyboardButton kb in Enum.GetValues<KeyboardButton>()) {
                kbstr.Add(kb.ToString());
                keyboardPresses.Add(kb, 0);
            }
            _keyboardButtonCache = new EnumCache<KeyboardButton, string>(kbstr);

            mousePresses = new Dictionary<MouseButton, int>();
            List<string> mstr = new List<string>();
            foreach (MouseButton mb in Enum.GetValues<MouseButton>()) {
                mstr.Add(mb.ToString());
                mousePresses.Add(mb, 0);
            }
            _mouseButtonCache = new EnumCache<MouseButton, string>(mstr);
        }

        private async void OnButtonAction(ButtonEvent evt)
        {
            EventCategory category = GetCategory(evt);
            using var copy = GetWebsockets(category);
            if (copy.Count == 0)
            {
                return;
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(256);
            try
            {
                // TODO: stop allocations
                var bufferWriter = new StaticSizeArrayBufferWriter(buffer);
                using (var writer = new Utf8JsonWriter(bufferWriter))
                {
                    SerializeButtonEvent(writer, evt, category);
                }

                for (int i = 0; i < copy.Count; i++)
                {
                    var wrapper = copy.Array[i];
                    if (wrapper.EventCategoryMask.HasFlag(category))
                    {
                        try
                        {
                            // TODO: can this be done in parallel?
                            await wrapper.WebSocket.SendAsync(bufferWriter.GetWritten(), WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                        catch (WebSocketException)
                        {
                            _logger?.LogInformation("WebSocketException on SendAsync.");
                            RemoveWebSocket(wrapper);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"OnButtonAction exception: {ex.Message}.");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private async void OnDeviceAction(DeviceEvent evt)
        {
            using var copy = GetWebsockets(EventCategory.RawInputDevices);
            if (copy.Count == 0)
            {
                return;
            }

            try
            {
                // TODO: stop allocations
                var bufferWriter = new ArrayBufferWriter<byte>();
                using (var writer = new Utf8JsonWriter(bufferWriter))
                {
                    SerializeDeviceEvent(writer, evt);
                }

                for (int i = 0; i < copy.Count; i++)
                {
                    var wrapper = copy.Array[i];
                    try
                    {
                        // TODO: can this be done in parallel?
                        await wrapper.WebSocket.SendAsync(bufferWriter.WrittenMemory, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    catch (WebSocketException)
                    {
                        _logger?.LogInformation("WebSocketException on SendAsync.");
                        RemoveWebSocket(wrapper);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"OnDeviceAction exception: {ex.Message}.");
            }
        }

        public async Task HandleWebSocket(WebSocket ws)
        {
            var wrapper = new WebSocketWrapper
            {
                WebSocket = ws,
                CancellationSource = new CancellationTokenSource()
            };
            lock (_webSockets)
            {
                _webSockets.Add(wrapper);
            }

            _logger?.LogInformation($"New WebSocket. Total WebSockets: {_webSockets.Count}.");

            var receive = ReceiveLoop(wrapper);
            var ping = PingLoop(wrapper);
            await Task.WhenAll(receive, ping);

            wrapper.CancellationSource.Dispose();
            _logger?.LogInformation("HandleWebSocket ended successfully.");
        }

        public void Dispose()
        {
            _input.Dispose();

            lock (_webSockets)
            {
                for (int i = 0; i < _webSockets.Count; i++)
                {
                    RemoveWebSocket(_webSockets[i]);
                    i--;
                }
            }

            _logger?.LogDebug("Disposed.");
        }

        private void RemoveWebSocket(WebSocketWrapper wrapper)
        {
            lock (wrapper)
            {
                if (wrapper.Closing)
                    return;

                wrapper.Closing = true;
            }

            _logger?.LogInformation("WebSocket disconnected.");

            wrapper.CancellationSource.Cancel();

            lock (_webSockets)
            {
                int index = _webSockets.IndexOf(wrapper);
                if (index >= 0)
                {
                    _webSockets.RemoveAt(index);
                }
            }

            _logger?.LogInformation($"Total WebSockets: {_webSockets.Count}.");
        }

        private async Task ReceiveLoop(WebSocketWrapper wrapper)
        {
            const int bufferSize = 256;
            var segment = new ArraySegment<byte>(new byte[bufferSize]);
            try
            {
                while (true)
                {
                    WebSocketReceiveResult result;
                    try
                    {
                        result = await wrapper.WebSocket.ReceiveAsync(segment, wrapper.CancellationSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (WebSocketException)
                    {
                        _logger?.LogInformation("WebSocketException on ReceiveAsync.");
                        RemoveWebSocket(wrapper);
                        return;
                    }

                    if (result.Count == 0)
                    {
                        _logger?.LogInformation("Empty response.");
                        RemoveWebSocket(wrapper);
                        return;
                    }

                    if (!result.EndOfMessage)
                    {
                        _logger?.LogError("Message received partially.");
                        RemoveWebSocket(wrapper);
                        return;
                    }

                    ClientMessage msg = DeserializeClientMessage(segment.AsSpan(0, result.Count));
                    if (msg.Listen != null)
                    {
                        EventCategory category = GetCategory(msg.Listen);
                        if (!wrapper.EventCategoryMask.HasFlag(category))
                        {
                            wrapper.EventCategoryMask |= category;

                            if (category == EventCategory.RawInputDevices)
                            {
                                // send already attached devices
                                var devices = new List<RawDevice>();
                                lock (_input.Devices)
                                {
                                    foreach (var device in _input.Devices.Values)
                                    {
                                        devices.Add(device);
                                    }
                                }
                            }
                        }
                    }
                    if (msg.Ping != null)
                    {
                        if (wrapper.LastPing != msg.Ping)
                        {
                            _logger?.LogWarning("Received ping data doesn't match.");
                            RemoveWebSocket(wrapper);
                            return;
                        }
                        wrapper.LastPing = null;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError("ReceiveLoop -> " + ex.GetType().ToString() + " " + ex.Message);
                RemoveWebSocket(wrapper);
            }
        }

        private async Task PingLoop(WebSocketWrapper wrapper)
        {
            try
            {
                while (true)
                {
                    if (wrapper.LastPing != null)
                    {
                        _logger?.LogInformation("Ping response not received;");
                        RemoveWebSocket(wrapper);
                        return;
                    }

                    lock (_rnd)
                    {
                        wrapper.LastPing = _rnd.Next();
                    }

                    byte[] buffer = ArrayPool<byte>.Shared.Rent(256);
                    try
                    {
                        // TODO: stop allocations
                        var bufferWriter = new StaticSizeArrayBufferWriter(buffer);
                        using (var writer = new Utf8JsonWriter(bufferWriter))
                        {
                            SerializePingMessage(writer, wrapper.LastPing.Value);
                        }

                        try
                        {
                            await wrapper.WebSocket.SendAsync(bufferWriter.GetWritten(), WebSocketMessageType.Text, true, wrapper.CancellationSource.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (WebSocketException)
                        {
                            _logger?.LogInformation("WebSocketException on SendAsync.");
                            RemoveWebSocket(wrapper);
                            return;
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }

                    try
                    {
                        await Task.Delay(1000, wrapper.CancellationSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError("PingLoop -> " + ex.GetType().ToString() + " " + ex.Message);
            }
        }

        private static ClientMessage DeserializeClientMessage(Span<byte> span)
        {
            string listen = null;
            int? ping = null;

            Utf8JsonReader reader = new Utf8JsonReader(span);
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        if (reader.CurrentDepth != 0)
                        {
                            throw new InvalidClientRequestException("Client message can have only simple object.");
                        }
                        break;

                    case JsonTokenType.EndObject:
                        break;

                    case JsonTokenType.PropertyName:
                        if (reader.ValueTextEquals(ClientMessage.ListenEventProperty))
                        {
                            reader.Read();
                            if (reader.TokenType == JsonTokenType.String)
                            {
                                listen = reader.GetString();
                            }
                            else
                            {
                                throw new InvalidClientRequestException("Listen property can only be string.");
                            }
                            break;
                        }
                        if (reader.ValueTextEquals(ClientMessage.PingProperty))
                        {
                            reader.Read();
                            if (reader.TokenType == JsonTokenType.Number)
                            {
                                ping = reader.GetInt32();
                            }
                            else
                            {
                                throw new InvalidClientRequestException("Ping property can only be integer.");
                            }
                            break;
                        }
                        throw new InvalidClientRequestException($"Invalid property in client message: {reader.GetString()}.");

                    default:
                        throw new InvalidClientRequestException($"Invalid token in client message: {reader.TokenType}.");
                }
            }

            return new ClientMessage(listen, ping);
        }

        private void SerializePingMessage(Utf8JsonWriter writer, int ping)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "Ping");
            writer.WriteNumber("ping", ping);
            writer.WriteEndObject();
        }

        private void SerializeButtonEvent(Utf8JsonWriter writer, ButtonEvent evt, EventCategory category)
        {
            writer.WriteStartObject();
            switch (category)
            {
                case EventCategory.Keyboard:
                    if (evt.Pressed && !evt.Held == true) ++keyboardPresses[evt.KeyboardButton];
                    writer.WriteString("type", nameof(EventCategory.Keyboard));
                    writer.WriteString("button", _keyboardButtonCache[evt.KeyboardButton]);
                    writer.WriteBoolean("pressed", evt.Pressed);
                    writer.WriteNumber("presses", keyboardPresses[evt.KeyboardButton]);
                    writer.WritePropertyName("raw");
                    writer.WriteStartObject();
                    writer.WriteNumber("makecode", evt.RawKeyboard.MakeCode);
                    writer.WriteNumber("flags", evt.RawKeyboard.Flags);
                    writer.WriteNumber("vkey", evt.RawKeyboard.VKey);
                    _logger.LogInformation(keyboardPresses[evt.KeyboardButton].ToString());
                    writer.WriteEndObject();
                    break;

                case EventCategory.MouseButtons:
                    if (evt.Pressed == true) ++mousePresses[evt.MouseButton];
                    writer.WriteString("type", nameof(EventCategory.MouseButtons));
                    writer.WriteString("button", _mouseButtonCache[evt.MouseButton]);
                    if (evt.MouseButton == MouseButton.MouseWheelDown || evt.MouseButton == MouseButton.MouseWheelUp)
                    {
                        writer.WriteNumber("count", evt.Count.Value);
                    }
                    else
                    {
                        writer.WriteBoolean("pressed", evt.Pressed);
                    }
                    writer.WriteNumber("presses", mousePresses[evt.MouseButton]);
                    _logger.LogInformation(mousePresses[evt.MouseButton].ToString());
                    break;

                default:
                    throw new NotImplementedException();
            }

            writer.WriteEndObject();
        }

        private void SerializeDeviceEvent(Utf8JsonWriter writer, DeviceEvent evt)
        {
            writer.WriteStartObject();
            writer.WriteString("type", nameof(EventCategory.RawInputDevices));
            writer.WriteString("hDevice", evt.Device.HDeviceStr);
            writer.WriteBoolean("attached", evt.Attached);

            writer.WriteEndObject();
        }

        private static EventCategory GetCategory(string category)
        {
            return category switch
            {
                nameof(EventCategory.Keyboard) => EventCategory.Keyboard,
                nameof(EventCategory.MouseButtons) => EventCategory.MouseButtons,
                nameof(EventCategory.RawMouseMovement) => EventCategory.RawMouseMovement,
                nameof(EventCategory.MouseMovement) => EventCategory.MouseMovement,
                nameof(EventCategory.RawInputDevices) => EventCategory.RawInputDevices,
                _ => throw new NotImplementedException(),
            };
        }

        private static EventCategory GetCategory(ButtonEvent evt)
        {
            if (evt.KeyboardButton != KeyboardButton.None)
            {
                return EventCategory.Keyboard;
            }

            if (evt.MouseButton != MouseButton.None)
            {
                return EventCategory.MouseButtons;
            }

            throw new InvalidOperationException("Unknown category.");
        }

        private CopyDisposable GetWebsockets(EventCategory category)
        {
            lock (_webSockets)
            {
                if (_webSockets.Count == 0)
                {
                    return new CopyDisposable(null, 0);
                }

                WebSocketWrapper[] array = ArrayPool<WebSocketWrapper>.Shared.Rent(_webSockets.Count);
                int count = 0;
                for (int i = 0; i < _webSockets.Count; i++)
                {
                    if (_webSockets[i].EventCategoryMask.HasFlag(category))
                    {
                        array[count++] = _webSockets[i];
                    }
                }

                return new CopyDisposable(array, count);
            }
        }

        private class WebSocketWrapper
        {
            public WebSocket WebSocket;
            public EventCategory EventCategoryMask;
            public CancellationTokenSource CancellationSource;
            public int? LastPing;
            public volatile bool Closing;
        }

        private readonly struct ClientMessage
        {
            public string Listen { get; }
            public int? Ping { get; }

            public ClientMessage(string listen, int? ping)
            {
                Listen = listen;
                Ping = ping;
            }

            public static readonly byte[] ListenEventProperty = Encoding.UTF8.GetBytes("listen");
            public static readonly byte[] PingProperty = Encoding.UTF8.GetBytes("ping");
        }

        private struct CopyDisposable : IDisposable
        {
            public WebSocketWrapper[] Array { get; }
            public int Count { get; }

            public CopyDisposable(WebSocketWrapper[] array, int count)
            {
                Array = array;
                Count = count;
            }

            public void Dispose()
            {
                if (Array != null)
                {
                    ArrayPool<WebSocketWrapper>.Shared.Return(Array);
                }
            }
        }
    }
}