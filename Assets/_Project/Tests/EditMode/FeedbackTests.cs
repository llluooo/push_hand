using NUnit.Framework;
using PushHands.Physical;
using UnityEngine;

namespace PushHands.Tests
{
    public sealed class FeedbackTests
    {
        private PhysicalConfig config;
        [SetUp] public void Setup() => config = ScriptableObject.CreateInstance<PhysicalConfig>();
        [TearDown] public void Cleanup() => Object.DestroyImmediate(config);
        [Test] public void NoContactCannotCreatePressureOrImpactEvenWithForceSample()
        {
            var state = new ContactFeedbackState();
            for (int i = 0; i < 100; i++) state.Tick(false, 1000, 0.01f, config);
            Assert.AreEqual(0, state.Pressure); Assert.AreEqual(0, state.PulseCount); Assert.AreEqual(0, state.Impact);
        }
        [Test] public void SustainedContactProducesOneArrivalAndSmoothPressure()
        {
            var state = new ContactFeedbackState();
            for (int i = 0; i < 200; i++) state.Tick(true, 55, 0.01f, config);
            Assert.AreEqual(1, state.PulseCount); Assert.That(state.Pressure, Is.EqualTo(0.5f).Within(0.01f));
            for (int i = 0; i < 30; i++) state.Tick(false, 0, 0.01f, config);
            Assert.Less(state.Pressure, 0.01f); Assert.Less(state.Impact, 0.01f);
        }
        [Test] public void SingleFrameContactGapsDoNotTurnSteadyPressureIntoRepeatedHits()
        {
            var state = new ContactFeedbackState();
            for (int i = 0; i < 250; i++) state.Tick(i % 9 != 8, 70, 0.01f, config);
            Assert.AreEqual(1, state.PulseCount, "接触微抖不能每隔几帧变成新撞击");
        }
        [Test] public void RealSeparationAndRecontactCreatesAnotherArrival()
        {
            var state = new ContactFeedbackState(); state.Tick(true, 55, 0.01f, config);
            for (int i = 0; i < 25; i++) state.Tick(false, 0, 0.01f, config);
            state.Tick(true, 55, 0.01f, config); Assert.AreEqual(2, state.PulseCount);
            state.Clear(); Assert.AreEqual(0, state.Pressure); Assert.AreEqual(0, state.Impact);
            Assert.AreEqual(0, state.PulseCount); Assert.IsFalse(state.ContactPulse);
        }
        [Test] public void AlternatingSingleFrameGapsAtFullPressureDoNotCreateRepeatedHits()
        {
            var state = new ContactFeedbackState();
            for (int i = 0; i < 300; i++) state.Tick(i % 2 == 0, 110, 0.01f, config);
            Assert.AreEqual(1, state.PulseCount, "频繁短断触也不能被当成反复加力");
        }
        [Test] public void InvalidTimeCannotGenerateFeedbackAndStrengthIsBounded()
        {
            var state = new ContactFeedbackState(); state.Tick(true, 100, float.NaN, config);
            state.Tick(true, 100, -1, config); Assert.AreEqual(0, state.PulseCount);
            state.Tick(true, float.PositiveInfinity, 0.01f, config); Assert.AreEqual(0, state.Pressure);
            state.Clear(); state.Tick(true, 1e9f, 1, config);
            Assert.That(state.Pressure, Is.InRange(0f, 1f)); Assert.That(state.Impact, Is.InRange(0f, 1f));
        }
    }
}
