using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PushHands
{
    public sealed class DeviceRoster : IDisposable
    {
        private readonly Gamepad[] pads = new Gamepad[2];
        private readonly bool[] armed = new bool[2];
        public bool KeyboardDebug { get; private set; }
        public event Action DeviceLost;
        private static bool Connected(Gamepad pad) => pad != null && pad.added && pad.enabled;
        public int Count => (Connected(pads[0]) ? 1 : 0) + (Connected(pads[1]) ? 1 : 0);
        public bool Complete => KeyboardDebug || Count == 2;
        public DeviceRoster() { InputSystem.onDeviceChange += OnDeviceChange; }
        public void Dispose() { InputSystem.onDeviceChange -= OnDeviceChange; }
        public Gamepad Device(int slot) => pads[slot];
        public bool Join(Gamepad pad)
        {
            if (!Connected(pad) || KeyboardDebug) return false;
            if (pads[0] == pad || pads[1] == pad) return false;
            for (int i = 0; i < 2; i++)
                if (!Connected(pads[i])) { pads[i] = pad; armed[i] = false; return true; }
            return false;
        }
        public void ToggleKeyboardDebug()
        { KeyboardDebug = !KeyboardDebug; pads[0] = pads[1] = null; Disarm(); }
        public void Disarm() { armed[0] = armed[1] = false; }
        public PlayerIntent Read(int slot)
        {
            if (KeyboardDebug)
            {
                if (slot != 0 || Keyboard.current == null || Mouse.current == null) return default;
                var k = Keyboard.current; var m = Mouse.current;
                if (!armed[0]) { armed[0] = !m.leftButton.isPressed && !m.rightButton.isPressed; return default; }
                return new PlayerIntent { LeftForce = m.leftButton.isPressed ? 1 : 0, RightForce = m.rightButton.isPressed ? 1 : 0,
                    Lean = Vector2.ClampMagnitude(new Vector2((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0),
                        (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0)), 1), Look = m.delta.ReadValue() };
            }
            var pad = pads[slot];
            if (!Connected(pad)) return default;
            if (!armed[slot])
            {
                armed[slot] = pad.leftTrigger.ReadValue() < 0.03f && pad.rightTrigger.ReadValue() < 0.03f
                    && pad.leftStick.ReadValue().sqrMagnitude < 0.01f;
                return default;
            }
            return new PlayerIntent { LeftForce = pad.leftTrigger.ReadValue(), RightForce = pad.rightTrigger.ReadValue(),
                Lean = pad.leftStick.ReadValue(), Look = pad.rightStick.ReadValue() };
        }
        public bool StartPressed()
        {
            if (KeyboardDebug) return Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame;
            return (pads[0] != null && pads[0].added && pads[0].startButton.wasPressedThisFrame)
                || (pads[1] != null && pads[1].added && pads[1].startButton.wasPressedThisFrame);
        }
        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change != InputDeviceChange.Disconnected && change != InputDeviceChange.Removed && change != InputDeviceChange.Disabled) return;
            if (device == pads[0] || device == pads[1]) { Disarm(); DeviceLost?.Invoke(); }
        }
    }
}
