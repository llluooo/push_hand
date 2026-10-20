using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using TMPro;

namespace PushHands.Tests
{
    public sealed class GreyboxTests
    {
        private PrototypeController controller;
        [UnitySetUp] public IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("PushHandsGreybox");
            yield return null;
            controller = Object.FindObjectOfType<PrototypeController>();
        }
        [UnityTest] public IEnumerator SceneWiresBothFirstPersonViewsAndChineseHud()
        {
            Assert.IsNotNull(controller); Assert.IsNotNull(controller.Config);
            Assert.AreEqual(2, controller.Views.Length);
            Assert.AreEqual(new Rect(0, 0, 0.5f, 1), controller.Views[0].ViewCamera.rect);
            Assert.AreEqual(new Rect(0.5f, 0, 0.5f, 1), controller.Views[1].ViewCamera.rect);
            Assert.AreEqual(1, Object.FindObjectsOfType<AudioListener>().Length);
            foreach (var text in controller.Hud)
            { Assert.IsNotNull(text.font); Assert.IsTrue(text.font.HasCharacter('推', true, true)); }
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator StepPresentationFollowsVerdictAndResetRestoresFeet()
        {
            var sim = controller.Simulation;
            sim.Ready(); sim.Start(); sim.Fighters[0].Center = new Vector2(0.9f, 0);
            sim.Step(default, default, 3);
            for (int i = 0; i < 100; i++) sim.Step(default, default, 0.02f);
            yield return null;
            Assert.AreEqual(RoundPhase.Finished, sim.Phase);
            Assert.AreEqual(RoundResult.PlayerTwo, sim.Result);
            Assert.Greater(controller.Views[0].Feet[1].localPosition.x, 0.4f);
            Assert.Greater(controller.Views[0].ViewCamera.transform.localPosition.x,
                sim.Fighters[0].Center.x * 0.65f + 0.15f, "迈步应带动身体和视点，第一人称下也能看出保护步。");
            controller.ResetRound(); yield return null;
            Assert.AreEqual(0.14f, controller.Views[0].Feet[1].localPosition.x, 0.001f);
            Assert.AreEqual(Vector2.zero, sim.Fighters[0].Center);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest] public IEnumerator FirstContactPlacesPalmOnOpponentTorso()
        {
            var sim = controller.Simulation; sim.Ready(); sim.Start();
            sim.Step(default, default, 3);
            for (int i = 0; i < 40 && !sim.Fighters[0].Left.Contact; i++)
                sim.Step(new PlayerIntent { LeftForce = 1 }, default, 0.02f);
            Assert.IsTrue(sim.Fighters[0].Left.Contact);
            controller.Views[0].Render(sim.Fighters[0], sim.Fighters[1], controller.Views[1], controller.Config);
            Vector3 target = controller.Views[1].transform.TransformPoint(new Vector3(0.16f + sim.Fighters[1].Center.x,
                1.36f, controller.Config.TorsoRadius + sim.Fighters[1].Center.y));
            Assert.Less(Vector3.Distance(controller.Views[0].Palms[0].position, target), 0.01f);
            yield return null;
        }

        [UnityTest] public IEnumerator LosingFocusFreezesUntilExplicitResume()
        {
            var sim = controller.Simulation; sim.Ready(); sim.Start();
            sim.Step(default, default, 3);
            controller.SendMessage("OnApplicationFocus", false);
            int tick = sim.Tick;
            yield return new WaitForSeconds(0.10f);
            Assert.AreEqual(RoundPhase.Frozen, sim.Phase); Assert.AreEqual(tick, sim.Tick);
            controller.SendMessage("OnApplicationFocus", true);
            yield return new WaitForSeconds(0.06f);
            Assert.AreEqual(RoundPhase.Frozen, sim.Phase);
            sim.Start();
            yield return new WaitForSeconds(0.06f);
            Assert.AreEqual(RoundPhase.Countdown, sim.Phase); Assert.AreEqual(tick, sim.Tick);
            yield return new WaitForSeconds(3.1f);
            Assert.Greater(sim.Tick, tick);
        }

        private static IEnumerator Menu(Gamepad pad)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.Start));
            yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null;
        }

        [UnityTest] public IEnumerator JoiningWaitsForStartAndBothScreensCountDownBeforeCombat()
        {
            var first = InputSystem.AddDevice<Gamepad>(); var second = InputSystem.AddDevice<Gamepad>();
            try
            {
                yield return Menu(first); yield return Menu(second);
                Assert.IsTrue(controller.Roster.Complete);
                Assert.AreEqual(RoundPhase.Ready, controller.Simulation.Phase);
                yield return Menu(first);
                Assert.AreEqual(RoundPhase.Countdown, controller.Simulation.Phase);
                var labels = new TMP_Text[2];
                for (int i = 0; i < 2; i++)
                {
                    labels[i] = controller.Hud[i].canvas.transform.Find("RoundCountdown").GetComponent<TMP_Text>();
                    Assert.AreEqual("3", labels[i].text);
                    Assert.AreEqual(i * 0.5f, labels[i].rectTransform.anchorMin.x);
                    Assert.AreEqual((i + 1) * 0.5f, labels[i].rectTransform.anchorMax.x);
                }
                var rotation = controller.Views[0].ViewCamera.transform.localRotation;
                InputSystem.QueueStateEvent(first, new GamepadState
                    { leftTrigger = 1, rightTrigger = 1, leftStick = Vector2.up, rightStick = Vector2.right });
                yield return new WaitForSeconds(1);
                Assert.AreEqual("2", labels[0].text); Assert.AreEqual(labels[0].text, labels[1].text);
                Assert.AreEqual(0, controller.Simulation.Tick);
                Assert.AreEqual(rotation, controller.Views[0].ViewCamera.transform.localRotation);
                yield return new WaitForSeconds(1);
                Assert.AreEqual("1", labels[0].text);
                yield return new WaitForSeconds(1);
                Assert.AreEqual(RoundPhase.Fighting, controller.Simulation.Phase);
                Assert.AreEqual("开始", labels[0].text);
                Assert.AreEqual(Vector2.zero, controller.Simulation.Fighters[0].Center);
                Assert.AreEqual(0, controller.Simulation.Fighters[0].Left.Extension);
                InputSystem.QueueStateEvent(first, new GamepadState()); yield return null; yield return null;
                InputSystem.QueueStateEvent(first, new GamepadState { leftTrigger = 1 });
                yield return new WaitForSeconds(0.1f);
                Assert.Greater(controller.Simulation.Fighters[0].Left.Extension, 0);
                LogAssert.NoUnexpectedReceived();
            }
            finally { InputSystem.RemoveDevice(first); InputSystem.RemoveDevice(second); }
        }

        [UnityTest] public IEnumerator CountdownStopsOnDeviceLossAndReplacementNeedsExplicitStart()
        {
            var first = InputSystem.AddDevice<Gamepad>(); var second = InputSystem.AddDevice<Gamepad>();
            var replacement = InputSystem.AddDevice<Gamepad>();
            try
            {
                yield return Menu(first); yield return Menu(second); yield return Menu(first);
                yield return new WaitForSeconds(1.1f);
                InputSystem.RemoveDevice(first); yield return null;
                Assert.AreEqual(RoundPhase.Frozen, controller.Simulation.Phase);
                Assert.AreEqual("", controller.Hud[0].canvas.transform.Find("RoundCountdown").GetComponent<TMP_Text>().text);
                yield return Menu(replacement);
                Assert.AreEqual(RoundPhase.Frozen, controller.Simulation.Phase);
                yield return Menu(replacement);
                Assert.AreEqual(3, controller.Simulation.CountdownNumber);
                Assert.AreEqual(0, controller.Simulation.Tick);
            }
            finally
            {
                if (first.added) InputSystem.RemoveDevice(first);
                InputSystem.RemoveDevice(second); InputSystem.RemoveDevice(replacement);
            }
        }

        [UnityTest] public IEnumerator FocusLossCancelsCountdownUntilExplicitThreeSecondRestart()
        {
            var sim = controller.Simulation; sim.Ready(); sim.Start();
            sim.Step(default, default, 1.1f);
            controller.SendMessage("OnApplicationFocus", false);
            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(RoundPhase.Frozen, sim.Phase);
            controller.SendMessage("OnApplicationFocus", true); yield return null;
            Assert.AreEqual(RoundPhase.Frozen, sim.Phase);
            sim.Start(); Assert.AreEqual(3, sim.CountdownNumber);
        }

        [UnityTest] public IEnumerator MenuCancelsCountdownAndFinishedRoundRestartsThroughCountdown()
        {
            var first = InputSystem.AddDevice<Gamepad>(); var second = InputSystem.AddDevice<Gamepad>();
            try
            {
                yield return Menu(first); yield return Menu(second); yield return Menu(first);
                yield return new WaitForSeconds(1.1f);
                yield return Menu(first);
                Assert.AreEqual(RoundPhase.Frozen, controller.Simulation.Phase);
                yield return Menu(first);
                Assert.AreEqual(3, controller.Simulation.CountdownNumber);
                var sim = controller.Simulation;
                sim.Step(default, default, 3);
                sim.Fighters[0].Center = new Vector2(0.9f, 0);
                for (int i = 0; i < 20; i++) sim.Step(default, default, 0.02f);
                Assert.AreEqual(RoundPhase.Finished, sim.Phase);
                yield return Menu(first);
                Assert.AreEqual(RoundPhase.Countdown, sim.Phase);
                Assert.AreEqual(3, sim.CountdownNumber);
                Assert.AreEqual(RoundResult.None, sim.Result);
                Assert.AreEqual(Vector2.zero, sim.Fighters[0].Center);
                Assert.AreEqual(0, sim.Tick);
            }
            finally { InputSystem.RemoveDevice(first); InputSystem.RemoveDevice(second); }
        }
    }
}
