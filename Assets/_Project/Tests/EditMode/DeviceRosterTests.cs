using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace PushHands.Tests
{
    public sealed class DeviceRosterTests
    {
        private DeviceRoster roster;
        private Gamepad first, second, third;
        [SetUp] public void Setup()
        {
            first = InputSystem.AddDevice<Gamepad>(); second = InputSystem.AddDevice<Gamepad>();
            third = InputSystem.AddDevice<Gamepad>(); roster = new DeviceRoster();
        }
        [TearDown] public void Cleanup()
        {
            roster.Dispose();
            foreach (var pad in new[] { first, second, third }) if (pad.added) InputSystem.RemoveDevice(pad);
        }
        private static void State(Gamepad pad, GamepadState state)
        { InputSystem.QueueStateEvent(pad, state); InputSystem.Update(); }

        [Test] public void JoiningCannotAssignOneDeviceTwice()
        {
            Assert.IsTrue(roster.Join(first)); Assert.IsFalse(roster.Join(first));
            Assert.IsTrue(roster.Join(second)); Assert.IsFalse(roster.Join(third)); Assert.IsTrue(roster.Complete);
        }
        [Test] public void DeviceInputAndTriggerStrengthAreIndependent()
        {
            roster.Join(first); roster.Join(second); roster.Read(0); roster.Read(1);
            State(first, new GamepadState { leftTrigger = 0.7f, rightStick = Vector2.right });
            var one = roster.Read(0); var two = roster.Read(1);
            Assert.AreEqual(0.7f, one.LeftForce, 0.01f); Assert.AreEqual(0, one.RightForce);
            Assert.Greater(one.Look.x, 0.9f); Assert.AreEqual(0, two.LeftForce); Assert.AreEqual(Vector2.zero, two.Look);
        }
        [Test] public void DisconnectFreezesAndReplacementRetainsOtherPlayer()
        {
            roster.Join(first); roster.Join(second); bool lost = false; roster.DeviceLost += () => lost = true;
            InputSystem.RemoveDevice(first);
            Assert.IsTrue(lost); Assert.IsFalse(roster.Complete); Assert.AreEqual(default(PlayerIntent), roster.Read(0));
            Assert.IsTrue(roster.Join(third)); Assert.AreSame(second, roster.Device(1));
        }
        [Test] public void HeldTriggerMustBeReleasedAfterResume()
        {
            roster.Join(first); roster.Read(0);
            State(first, new GamepadState { leftTrigger = 1 }); roster.Disarm();
            Assert.AreEqual(0, roster.Read(0).LeftForce);
            State(first, new GamepadState()); roster.Read(0);
            State(first, new GamepadState { leftTrigger = 0.5f });
            Assert.AreEqual(0.5f, roster.Read(0).LeftForce, 0.01f);
        }

        [Test] public void DisabledDeviceCannotKeepRoundReady()
        {
            roster.Join(first); roster.Join(second);
            InputSystem.DisableDevice(first);
            Assert.IsFalse(roster.Complete); Assert.AreEqual(default(PlayerIntent), roster.Read(0));
            InputSystem.EnableDevice(first); Assert.IsTrue(roster.Complete);
        }

        [Test] public void KeyboardDebugMapsHandsAndLeanOnlyToPlayerOne()
        {
            var previousKeyboard = Keyboard.current; var previousMouse = Mouse.current;
            var keyboard = InputSystem.AddDevice<Keyboard>(); var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                keyboard.MakeCurrent(); mouse.MakeCurrent(); roster.ToggleKeyboardDebug(); roster.Read(0);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1, delta = new Vector2(2, 1) });
                InputSystem.Update();
                Assert.AreEqual(1, roster.Read(0).LeftForce); Assert.AreEqual(0, roster.Read(0).RightForce);
                Assert.AreEqual(Vector2.up, roster.Read(0).Lean);
                Assert.AreEqual(default(PlayerIntent), roster.Read(1));
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse);
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            }
        }

        [Test] public void KeyboardCannotArmWhileMovementKeyIsHeld()
        {
            var previousKeyboard = Keyboard.current; var previousMouse = Mouse.current;
            var keyboard = InputSystem.AddDevice<Keyboard>(); var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                keyboard.MakeCurrent(); mouse.MakeCurrent(); roster.ToggleKeyboardDebug();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                InputSystem.Update();
                Assert.AreEqual(default(PlayerIntent), roster.Read(0));
                Assert.AreEqual(default(PlayerIntent), roster.Read(0), "持续按住 WASD 时不得解除保护。");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); roster.Read(0);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); InputSystem.Update();
                Assert.AreEqual(Vector2.up, roster.Read(0).Lean);
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse);
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            }
        }
    }
}
