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
        }
        [TearDown] public void Cleanup() { Object.DestroyImmediate(config); }
        private void Steps(int count, PlayerIntent a = default, PlayerIntent b = default)
        { for (int i = 0; i < count; i++) match.Step(a, b, 0.02f); }

        [Test] public void StandingRemainsStable()
        { Steps(1000); Assert.AreEqual(Vector2.zero, match.Fighters[0].Center); Assert.AreEqual(RoundResult.None, match.Result); }

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
            Steps(300, default, new PlayerIntent { LeftForce = 1 });
            Assert.AreNotEqual(RoundResult.None, original);
            Assert.AreEqual(original == RoundResult.PlayerOne ? RoundResult.PlayerTwo : RoundResult.PlayerOne, match.Result);
        }

        [Test] public void DisplayFrameGroupingDoesNotChangeFixedStepResult()
        {
            var other = new MatchSimulation(config); other.Ready(); other.Start();
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
