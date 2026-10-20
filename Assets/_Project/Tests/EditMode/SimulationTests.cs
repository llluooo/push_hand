using NUnit.Framework;
using UnityEngine;

namespace PushHands.Tests
{
    public sealed class SimulationTests
    {
        private PrototypeConfig config;
        private MatchSimulation match;
        [SetUp] public void Setup()
        {
            config = ScriptableObject.CreateInstance<PrototypeConfig>();
            match = new MatchSimulation(config);
            match.Ready(); match.Start();
            match.Step(default, default, 3);
        }
        [TearDown] public void Cleanup() { Object.DestroyImmediate(config); }
        private void Steps(int count, PlayerIntent a = default, PlayerIntent b = default)
        { for (int i = 0; i < count; i++) match.Step(a, b, 0.02f); }

        [Test] public void StandingRemainsStable()
        { Steps(1000); Assert.AreEqual(Vector2.zero, match.Fighters[0].Center); Assert.AreEqual(RoundResult.None, match.Result); }

        [Test] public void StartRequiresThreeSecondsWithoutAdvancingCombat()
        {
            match.Reset(); match.Start();
            var held = new PlayerIntent { LeftForce = 1, RightForce = 1, Lean = Vector2.one };
            for (int i = 0; i < 149; i++) match.Step(held, held, 0.02f);
            Assert.AreEqual("Countdown", match.Phase.ToString());
            Assert.AreEqual(0, match.Tick);
            Assert.AreEqual(Vector2.zero, match.Fighters[0].Center);
            Assert.AreEqual(0, match.Fighters[0].Left.Extension);
            match.Step(held, held, 0.02f);
            Assert.AreEqual(RoundPhase.Fighting, match.Phase);
            Assert.AreEqual(0, match.Tick, "倒计时结束所在模拟步仍不可施力。");
            match.Step(held, default, 0.02f);
            Assert.AreEqual(1, match.Tick);
        }

        [Test] public void InterruptedCountdownRestartsAndPreservesFighterState()
        {
            match.Fighters[0].Center = new Vector2(0.04f, 0.03f);
            match.Fighters[0].Velocity = new Vector2(0.02f, 0.01f);
            match.Freeze(); match.Start(); match.Step(default, default, 1.5f);
            match.Freeze();
            int tick = match.Tick;
            match.Step(default, default, 5);
            Assert.AreEqual(RoundPhase.Frozen, match.Phase);
            Assert.AreEqual(new Vector2(0.04f, 0.03f), match.Fighters[0].Center);
            Assert.AreEqual(new Vector2(0.02f, 0.01f), match.Fighters[0].Velocity);
            match.Start(); match.Step(default, default, 2.99f);
            Assert.AreEqual("Countdown", match.Phase.ToString());
            match.Step(default, default, 0.01f);
            Assert.AreEqual(RoundPhase.Fighting, match.Phase);
            Assert.AreEqual(tick, match.Tick);
        }

        [Test] public void RepeatedStartCannotSkipOrRestartCountdown()
        {
            match.Reset(); match.Start(); match.Step(default, default, 1);
            match.Start(); match.Step(default, default, 1);
            Assert.AreEqual("Countdown", match.Phase.ToString());
            match.Start(); match.Step(default, default, 1);
            Assert.AreEqual(RoundPhase.Fighting, match.Phase);
            Assert.AreEqual(0, match.Tick);
        }

        [Test] public void CountdownShowsThreeTwoOneAtOneSecondIntervals()
        {
            match.Reset(); match.Start(); Assert.AreEqual(3, match.CountdownNumber);
            Steps(49); Assert.AreEqual(3, match.CountdownNumber);
            Steps(1); Assert.AreEqual(2, match.CountdownNumber);
            Steps(50); Assert.AreEqual(1, match.CountdownNumber);
            Steps(50); Assert.AreEqual(0, match.CountdownNumber);
            Assert.AreEqual(RoundPhase.Fighting, match.Phase);
        }

        [Test] public void ResetAndWaitingCannotAccidentallyKeepCountdownRunning()
        {
            match.Reset(); match.Start(); match.Step(default, default, 1);
            match.Reset(); match.Step(default, default, 5);
            Assert.AreEqual(RoundPhase.Ready, match.Phase);
            match.WaitForPlayers(); match.Start(); match.Step(default, default, 5);
            Assert.AreEqual(RoundPhase.Waiting, match.Phase);
            Assert.AreEqual(0, match.Tick);
        }

        [Test] public void BriefExcursionCanRecover()
        {
            match.Fighters[0].Center = new Vector2(0.205f, 0);
            Steps(3); Assert.IsFalse(match.Fighters[0].Stepping);
            Steps(150); Assert.AreEqual(RoundResult.None, match.Result);
            Assert.Less(match.Fighters[0].Center.magnitude, 0.01f);
        }

        [Test] public void SustainedExcursionCommitsStepAndResolvesOnce()
        {
            config.MaxRecoveryAcceleration = 0;
            match.Fighters[0].Center = new Vector2(0.23f, 0);
            Steps(20); Assert.IsTrue(match.Fighters[0].Stepping);
            Assert.AreEqual(RoundResult.PlayerTwo, match.Result);
            Steps(100); Assert.AreEqual(1, match.ResolutionCount);
            Assert.AreEqual(1f, match.Fighters[0].StepProgress);
        }

        [Test] public void SameTickFailureIsDraw()
        {
            config.MaxRecoveryAcceleration = 0;
            foreach (var f in match.Fighters) f.Center = new Vector2(0.23f, 0);
            Steps(20); Assert.AreEqual(RoundResult.Draw, match.Result);
        }

        [Test] public void VelocityPredictsImminentLossOfBalance()
        {
            config.MaxRecoveryAcceleration = 0;
            match.Fighters[0].Velocity = new Vector2(1, 0);
            Steps(1);
            Assert.Greater(match.Fighters[0].PredictedCenter.x, config.SupportHalfSize.x);
            Assert.Greater(match.Fighters[0].UnstableSeconds, 0);
        }

        [Test] public void NoContactProducesNoForce()
        {
            config.PlayerSpacing = 5f;
            config.EffortLean = 0;
            Steps(100, new PlayerIntent { LeftForce = 1, RightForce = 1 });
            Assert.IsFalse(match.Fighters[0].Left.Contact);
            Assert.AreEqual(Vector2.zero, match.Fighters[1].Center);
            Assert.AreEqual(Vector2.zero, match.Fighters[0].Center);
        }

        [Test] public void RetractingOneHandDoesNotCancelTheOther()
        {
            config.SharedForce = 0; config.EffortLean = 0;
            Steps(50, new PlayerIntent { LeftForce = 1, RightForce = 1 });
            Steps(1, new PlayerIntent { RightForce = 1 });
            Assert.AreEqual(HandPhase.Retracting, match.Fighters[0].Left.Phase);
            Assert.IsFalse(match.Fighters[0].Left.Contact);
            Assert.IsTrue(match.Fighters[0].Right.Contact);
        }

        [Test] public void ContactPushesBothBodiesAndReleaseDoesNotPull()
        {
            config.EffortLean = 0; // 隔离主动前倾，单独验证接触反作用。
            Steps(20, new PlayerIntent { LeftForce = 1 });
            Assert.Less(match.Fighters[1].Velocity.y, 0);
            Assert.Less(match.Fighters[0].Velocity.y, 0);
            Steps(1); Assert.IsFalse(match.Fighters[0].Left.Contact);
            Assert.AreEqual(0, match.Fighters[0].Left.Strength);
        }

        [Test] public void ResetClearsEveryTransientState()
        {
            Steps(20, new PlayerIntent { LeftForce = 1 }); match.Reset();
            Assert.AreEqual(RoundResult.None, match.Result); Assert.AreEqual(0, match.Tick);
            foreach (var f in match.Fighters)
            {
                Assert.AreEqual(Vector2.zero, f.Center); Assert.AreEqual(Vector2.zero, f.Velocity);
                Assert.IsFalse(f.Stepping); Assert.AreEqual(0, f.UnstableSeconds);
                Assert.AreEqual(HandPhase.Rest, f.Left.Phase); Assert.AreEqual(0, f.Left.Strength);
                Assert.IsFalse(f.Right.Contact);
            }
        }

        [Test] public void FrozenRoundDoesNotAdvance()
        { match.Freeze(); Steps(20, new PlayerIntent { LeftForce = 1 }); Assert.AreEqual(0, match.Tick); }

        [Test] public void SwappingPlayersPreservesOutcome()
        {
            Steps(300, new PlayerIntent { LeftForce = 1 });
            var original = match.Result;
            match.Reset(); match.Start();
            match.Step(default, default, 3);
            Steps(300, default, new PlayerIntent { LeftForce = 1 });
            Assert.AreNotEqual(RoundResult.None, original);
            Assert.AreEqual(original == RoundResult.PlayerOne ? RoundResult.PlayerTwo : RoundResult.PlayerOne, match.Result);
        }

        [Test] public void DisplayFrameGroupingDoesNotChangeFixedStepResult()
        {
            var other = new MatchSimulation(config); other.Ready(); other.Start();
            other.Step(default, default, 3);
            var intent = new PlayerIntent { LeftForce = 1, RightForce = 0.5f };
            for (int frame = 0; frame < 150; frame++)
            {
                match.Step(intent, default, 0.02f); match.Step(intent, default, 0.02f);
            }
            for (int frame = 0; frame < 300; frame++) other.Step(intent, default, 0.02f);
            Assert.AreEqual(match.Result, other.Result);
            Assert.AreEqual(match.Tick, other.Tick);
            Assert.AreEqual(match.Fighters[1].Center, other.Fighters[1].Center);
        }
    }
}
