using UnityEngine;
using UnityEngine.InputSystem;

namespace PushHands.Physical
{
    public sealed class PhysicalInput : System.IDisposable
    {
        public readonly DeviceRoster Roster = new DeviceRoster();
        public readonly PlayerIntent[] Intents = new PlayerIntent[2];
        private readonly bool[] armed = new bool[2], withdraw = new bool[2], feint = new bool[2];
        private readonly bool[] withdrawTap = new bool[2], feintTap = new bool[2];
        public void Dispose() => Roster.Dispose();
        public void Disarm()
        {
            Roster.Disarm();
            for (int i = 0; i < 2; i++) { Intents[i] = default; armed[i] = withdraw[i] = feint[i] = withdrawTap[i] = feintTap[i] = false; }
        }
        public void Sample(int slot)
        {
            Intents[slot] = Roster.Read(slot);
            bool w = false, f = false, wt = false, ft = false;
            if (Roster.KeyboardDebug && slot == 0 && Keyboard.current != null)
            {
                w = Keyboard.current.qKey.isPressed; f = Keyboard.current.eKey.isPressed;
                wt = Keyboard.current.qKey.wasPressedThisFrame; ft = Keyboard.current.eKey.wasPressedThisFrame;
            }
            else if (!Roster.KeyboardDebug && Roster.Device(slot) != null)
            {
                var pad = Roster.Device(slot);
                w = pad.leftShoulder.isPressed; f = pad.rightShoulder.isPressed;
                wt = pad.leftShoulder.wasPressedThisFrame; ft = pad.rightShoulder.wasPressedThisFrame;
            }
            if (!armed[slot]) { armed[slot] = !w && !f; return; }
            withdraw[slot] = w; feint[slot] = f; withdrawTap[slot] |= wt; feintTap[slot] |= ft;
        }
        public void ConsumeTechniques(int slot, out bool w, out bool f)
        { w = withdraw[slot] || withdrawTap[slot]; f = feint[slot] || feintTap[slot]; withdrawTap[slot] = feintTap[slot] = false; }
    }
}
