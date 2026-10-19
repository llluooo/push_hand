using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

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
            controller.SendMessage("OnApplicationFocus", false);
            int tick = sim.Tick;
            yield return new WaitForSeconds(0.10f);
            Assert.AreEqual(RoundPhase.Frozen, sim.Phase); Assert.AreEqual(tick, sim.Tick);
            controller.SendMessage("OnApplicationFocus", true);
            yield return new WaitForSeconds(0.06f);
            Assert.AreEqual(RoundPhase.Frozen, sim.Phase);
            sim.Start(); yield return new WaitForSeconds(0.06f);
            Assert.Greater(sim.Tick, tick);
        }
    }
}
