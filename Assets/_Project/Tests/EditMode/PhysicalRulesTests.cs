using NUnit.Framework;
using PushHands.Physical;

namespace PushHands.Tests
{
    public sealed class PhysicalRulesTests
    {
        [TestCase(0)] [TestCase(1)]
        public void GeneratedFighterStartsAndResetsWithParallelFeet(int index)
        {
            var config = UnityEngine.ScriptableObject.CreateInstance<PhysicalConfig>();
            PhysicalFighter fighter = null;
            try
            {
                fighter = PhysicalFighter.Create(index, config, null);
                AssertParallelStance(fighter);
            }
            finally
            {
                if (fighter != null) UnityEngine.Object.DestroyImmediate(fighter.gameObject);
                UnityEngine.Object.DestroyImmediate(config);
            }
        }
        [TestCase(0)] [TestCase(1)]
        public void SavedFighterStartsAndResetsWithParallelFeet(int index)
        {
            var root = UnityEditor.PrefabUtility.LoadPrefabContents($"Assets/_Project/Prefabs/Physical/Fighter{index}.prefab");
            try { AssertParallelStance(root.GetComponent<PhysicalFighter>()); }
            finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
        }
        [Test] public void PlayableSceneStoresParallelFeetForBothPlayers()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                "Assets/_Project/Scenes/PushHandsPhysical.unity", UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                int count = 0;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var fighter in root.GetComponentsInChildren<PhysicalFighter>())
                    { AssertParallelStance(fighter); count++; }
                Assert.AreEqual(2, count);
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true); }
        }
        private static void AssertParallelStance(PhysicalFighter fighter)
        {
            PhysicalBone left = System.Array.Find(fighter.Bones, bone => bone.Name == "LeftFoot");
            PhysicalBone right = System.Array.Find(fighter.Bones, bone => bone.Name == "RightFoot");
            var l = fighter.transform.InverseTransformPoint(left.StartPosition);
            var r = fighter.transform.InverseTransformPoint(right.StartPosition);
            Assert.That(l.z, Is.EqualTo(r.z).Within(0.0001f), "开局左右脚应在同一前后位置");
            Assert.That(r.x - l.x, Is.EqualTo(0.30f).Within(0.0001f));
            Assert.That(fighter.SupportMaximum.y - fighter.SupportMinimum.y, Is.EqualTo(0.26f).Within(0.0001f),
                "前后支撑范围应等于并排脚掌长度，不能残留错步的额外支撑");
            foreach (var foot in new[] { left, right })
            {
                Assert.That(UnityEngine.Vector3.Distance(foot.Body.transform.position, foot.StartPosition), Is.LessThan(0.0001f),
                    "保存的可见脚位必须与重开姿态一致");
                Assert.That(UnityEngine.Quaternion.Angle(foot.StartRotation, fighter.transform.rotation), Is.LessThan(0.001f));
                var joint = foot.Joint;
                var anchor = joint.transform.TransformPoint(joint.anchor);
                var connected = joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);
                Assert.That(UnityEngine.Vector3.Distance(anchor, connected), Is.LessThan(0.001f), "踝关节两端锚点必须重合");
                foot.Body.position += fighter.transform.forward * 0.25f;
            }
            foreach (var bone in fighter.Bones)
                if (bone.Name.EndsWith("Shin"))
                    Assert.That(UnityEngine.Vector3.Dot(bone.Body.transform.TransformDirection(bone.Joint.secondaryAxis), fighter.transform.up),
                        Is.GreaterThan(0.9999f), "更新腿部姿态后膝关节参考轴也应同步");
            fighter.ResetPose();
            foreach (var foot in new[] { left, right })
            {
                Assert.That(UnityEngine.Vector3.Distance(foot.Body.position, foot.StartPosition), Is.LessThan(0.0001f));
                var anchor = foot.Body.GetComponents<UnityEngine.ConfigurableJoint>()[1].connectedAnchor;
                Assert.That(UnityEngine.Vector3.Distance(anchor, foot.StartPosition), Is.LessThan(0.0001f), "重开足部锚点应与并排脚位一致");
            }
        }
        [Test] public void SavedFighterRestoresRuntimePhysicsSolverSettings()
        {
            var root = UnityEditor.PrefabUtility.LoadPrefabContents("Assets/_Project/Prefabs/Physical/Fighter0.prefab");
            try
            {
                var fighter = root.GetComponent<PhysicalFighter>();
                Assert.IsNotNull(fighter);
                TestContext.WriteLine($"资源载入原始求解设置={fighter.Pelvis.solverIterations}/{fighter.Pelvis.solverVelocityIterations} 最大角速度={fighter.Pelvis.maxAngularVelocity}");
                fighter.ResetPose();
                foreach (var bone in fighter.Bones)
                {
                    Assert.AreEqual(16, bone.Body.solverIterations, "已保存资源必须恢复与运行时创建人偶一致的物理精度");
                    Assert.AreEqual(8, bone.Body.solverVelocityIterations);
                    Assert.AreEqual(12f, bone.Body.maxAngularVelocity);
                }
            }
            finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
        }
        [Test] public void JoiningOnlyReadiesAndCountdownLocksForThreeSeconds()
        {
            var round = new PhysicalRound(); round.Ready();
            Assert.AreEqual(RoundPhase.Ready, round.Phase); round.Start();
            Assert.AreEqual(3, round.CountdownNumber); round.Advance(2.99f);
            Assert.AreEqual(RoundPhase.Countdown, round.Phase); round.Advance(0.02f);
            Assert.AreEqual(RoundPhase.Fighting, round.Phase);
        }
        [Test] public void CountdownCannotBeSkippedByRepeatedStart()
        {
            var round = new PhysicalRound(); round.Ready(); round.Start(); round.Advance(1);
            round.Start(); Assert.AreEqual(2, round.CountdownNumber);
        }
        [Test] public void ResumeAlwaysStartsFullCountdown()
        {
            var round = new PhysicalRound(); round.Ready(); round.Start(); round.Advance(3);
            round.Freeze(); round.Start(); Assert.AreEqual(3, round.CountdownNumber);
        }
        [Test] public void BothFailuresInSameStepResolveOnlyOnce()
        {
            var round = new PhysicalRound(); round.Ready(); round.Start(); round.Advance(3);
            round.Resolve(true, true); round.Resolve(false, true);
            Assert.AreEqual(RoundResult.Draw, round.Result); Assert.AreEqual(1, round.Resolutions);
            round.Reset(false); Assert.AreEqual(RoundPhase.Waiting, round.Phase);
            Assert.AreEqual(RoundResult.None, round.Result); Assert.AreEqual(0, round.Resolutions);
        }
        [Test] public void InvalidTimeDoesNotAdvanceCountdown()
        {
            var round = new PhysicalRound(); round.Ready(); round.Start(); round.Advance(float.PositiveInfinity);
            round.Advance(float.NaN); round.Advance(-1); Assert.AreEqual(3, round.CountdownNumber);
        }
        [Test] public void PalmAndForearmAreLegalInBothOrders()
        {
            Assert.IsTrue(ContactRules.IsLegal(ContactRegion.Palm, ContactRegion.Palm));
            Assert.IsTrue(ContactRules.IsLegal(ContactRegion.Palm, ContactRegion.Forearm));
            Assert.IsTrue(ContactRules.IsLegal(ContactRegion.Forearm, ContactRegion.Palm));
            Assert.IsTrue(ContactRules.IsLegal(ContactRegion.Forearm, ContactRegion.Forearm));
        }
        [Test] public void TorsoAndHeadNeverTransmitCombatForce()
        {
            foreach (ContactRegion region in System.Enum.GetValues(typeof(ContactRegion)))
            {
                Assert.IsFalse(ContactRules.IsLegal(region, ContactRegion.Torso));
                Assert.IsFalse(ContactRules.IsLegal(ContactRegion.Head, region));
            }
        }
        [Test] public void WithdrawOverridesSimultaneousFeint()
        {
            var state = new TechniqueState(); state.Tick(true, true, 0.02f);
            Assert.AreEqual(Technique.Withdraw, state.Active);
            Assert.AreEqual(1, state.TriggerCount);
        }
        [Test] public void HeldButtonDoesNotRepeatAfterRecovery()
        {
            var state = new TechniqueState();
            for (int i = 0; i < 200; i++) state.Tick(true, false, 0.02f);
            Assert.AreEqual(1, state.TriggerCount);
            Assert.AreEqual(Technique.None, state.Active);
            state.Tick(false, false, 0.02f); state.Tick(true, false, 0.02f);
            Assert.AreEqual(2, state.TriggerCount);
        }
        [Test] public void PressDuringRecoveryIsNotQueued()
        {
            var state = new TechniqueState(); state.Tick(true, false, 0.02f);
            state.Tick(false, false, 0.02f); state.Tick(false, true, 0.02f);
            for (int i = 0; i < 200; i++) state.Tick(false, true, 0.02f);
            Assert.AreEqual(1, state.TriggerCount);
        }
        [Test] public void FeintReturnsToRestWithoutSustainedPush()
        {
            var state = new TechniqueState(); state.Tick(false, true, 0.10f);
            Assert.AreEqual(Technique.Feint, state.Active);
            Assert.Greater(state.FeintWeight, 0);
            state.Tick(false, false, 2);
            Assert.AreEqual(Technique.None, state.Active);
            Assert.AreEqual(0, state.FeintWeight);
        }
        [Test] public void InvalidTimeCannotConsumePress()
        {
            var state = new TechniqueState(); state.Tick(true, false, float.NaN);
            state.Tick(true, false, 0); state.Tick(true, false, -1);
            Assert.AreEqual(0, state.TriggerCount);
            state.Tick(true, false, 0.02f);
            Assert.AreEqual(1, state.TriggerCount);
        }
        [Test] public void ResetClearsTechniqueAndPressLatch()
        {
            var state = new TechniqueState(); state.Tick(true, false, 0.02f); state.Reset();
            Assert.AreEqual(0, state.TriggerCount);
            Assert.AreEqual(Technique.None, state.Active);
            state.Tick(true, false, 0.02f);
            Assert.AreEqual(1, state.TriggerCount);
        }
    }
}
