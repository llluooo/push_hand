using System.Collections;
using NUnit.Framework;
using PushHands.Physical;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PushHands.Tests
{
    public sealed class PhysicalTests
    {
        private Scene scene;
        private PhysicalConfig config;
        private PhysicalFighter a, b;
        private readonly System.Collections.Generic.List<AudioListener> previousListeners = new System.Collections.Generic.List<AudioListener>();
        private readonly System.Collections.Generic.List<Material> previewMaterials = new System.Collections.Generic.List<Material>();
        private readonly System.Collections.Generic.List<PhysicMaterial> supportMaterials = new System.Collections.Generic.List<PhysicMaterial>();
        private void PrepareResultPreview()
        {
            var lightObject = new GameObject("ResultPreviewLight"); SceneManager.MoveGameObjectToScene(lightObject, scene);
            lightObject.layer = 18; lightObject.transform.rotation = Quaternion.Euler(45, -25, 0);
            var lamp = lightObject.AddComponent<Light>(); lamp.type = LightType.Directional; lamp.intensity = 1.4f;
            foreach (var fighter in new[] { a, b })
            {
                var material = new Material(Shader.Find("Standard"))
                    { color = fighter == a ? new Color(0.18f, 0.48f, 0.72f) : new Color(0.86f, 0.42f, 0.20f) };
                previewMaterials.Add(material);
                foreach (var bone in fighter.Bones)
                {
                    bone.Body.GetComponent<Renderer>().sharedMaterial = material;
                    bone.Body.gameObject.layer = bone.Region == ContactRegion.Head ? 16 + fighter.PlayerIndex : 14 + fighter.PlayerIndex;
                }
                fighter.ViewCamera.cullingMask = (1 << 14) | (1 << 15) | (1 << (17 - fighter.PlayerIndex)) | (1 << 18);
            }
            foreach (var root in scene.GetRootGameObjects()) if (root.name == "Cube") root.layer = 18;
        }
        private PhysicalMatch CreateMatch()
        {
            var obj = new GameObject("FeedbackTestMatch"); SceneManager.MoveGameObjectToScene(obj, scene);
            var match = obj.AddComponent<PhysicalMatch>(); match.Config = config;
            match.Fighters = new[] { a, b }; match.ExternalSimulation = true; match.Initialize();
            match.Round.Ready(); match.Round.Start(); match.Tick(default, default, false, false, false, false, 3);
            return match;
        }
        [Test] public void ResultDisplayWaitsThreeSecondsAndPauseStopsItsClock()
        {
            var match = CreateMatch(); match.Round.Resolve(false, true);
            match.Tick(default, default, false, false, false, false, 2.99f);
            Assert.IsFalse(match.CanRestart); Assert.AreEqual(1, match.Round.Resolutions);
            float before = match.ResultSeconds; match.Freeze("测试暂停");
            match.Tick(default, default, false, false, false, false, 1);
            Assert.AreEqual(before, match.ResultSeconds); Assert.IsFalse(match.CanRestart);
            match.ResetRound(); Assert.AreEqual(0, match.ResultSeconds); Assert.IsFalse(match.ResultPaused);
            match.Round.Ready(); match.Round.Start(); match.Tick(default, default, false, false, false, false, 3);
            match.Round.Resolve(true, true);
            match.Tick(default, default, false, false, false, false, 3.01f);
            Assert.IsTrue(match.CanRestart); Assert.AreEqual(RoundResult.Draw, match.Round.Result);
        }
        [Test] public void ResultImmediatelyClearsBothPlayersEffortAndTechnique()
        {
            var match = CreateMatch();
            a.Drive(new PlayerIntent { LeftForce = 1, RightForce = 1, Lean = Vector2.up }, false, true, b, 0.1f);
            match.Round.Resolve(false, true);
            match.Tick(new PlayerIntent { LeftForce = 1, Lean = Vector2.up }, default, true, true, true, true, 0.01f);
            foreach (var fighter in new[] { a, b })
            {
                Assert.AreEqual(0, fighter.Effort[0]); Assert.AreEqual(0, fighter.Effort[1]);
                Assert.AreEqual(Technique.None, fighter.Technique.Active);
                Assert.AreEqual(Vector3.zero, fighter.ActivePushForce);
            }
        }
        [TestCase(0, 0.42f)] [TestCase(1, 0.42f)] [TestCase(0, 0.25f)]
        public void DefeatShowsActualLandingAndRecoveryBeforeRestart(int attackerIndex, float stepSeconds)
        {
            config.StepSeconds = stepSeconds;
            var match = CreateMatch();
            PrepareResultPreview();
            var attacker = attackerIndex == 0 ? a : b; var defender = attackerIndex == 0 ? b : a;
            var attack = new PlayerIntent { LeftForce = 1, RightForce = 1, Lean = Vector2.up * 0.8f };
            for (int i = 0; i < 300; i++)
            {
                match.Tick(default, default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f);
            }
            int pressureFrames = 0;
            for (int i = 0; i < 600 && match.Round.Phase == RoundPhase.Fighting; i++)
            {
                match.Tick(attackerIndex == 0 ? attack : default, attackerIndex == 0 ? default : attack,
                    false, false, false, false, 0.01f);
                scene.GetPhysicsScene().Simulate(0.01f);
                if (match.Feedback.Hand(attackerIndex, 0).Pressure > 0.05f) pressureFrames++;
            }
            Assert.AreEqual(attackerIndex == 0 ? RoundResult.PlayerOne : RoundResult.PlayerTwo, match.Round.Result);
            Assert.Greater(pressureFrames, 0, "实际合法推压必须进入表现层");
            Assert.IsFalse(match.CanRestart);
            if (attackerIndex == 0) SaveSplitView("defeat-start");
            bool capturedLanding = false;
            int landedTick = -1;
            for (int i = 0; i < 400; i++)
            {
                match.Tick(attack, attack, true, true, true, true, 0.01f);
                scene.GetPhysicsScene().Simulate(0.01f);
                if (defender.StepLanded && !capturedLanding)
                { capturedLanding = true; landedTick = i; if (attackerIndex == 0) SaveSplitView("defeat-landed"); }
                if (landedTick >= 0 && i - landedTick < 89)
                    Assert.IsFalse(defender.StepPresentationComplete, "落脚之后仍应保留约0.9秒恢复，不能从起步计时提前冻结");
                Assert.AreEqual(Vector3.zero, attacker.ActivePushForce);
                if (i == 100) Assert.IsFalse(match.CanRestart);
            }
            TestContext.WriteLine($"进攻方={attackerIndex} 落脚={defender.StepLanded} 完成={defender.StepPresentationComplete} 足部位移={defender.CaptureBalance().FootDisplacement:F3} 输家骨盆高度={defender.Pelvis.position.y:F3} 赢家骨盆高度={attacker.Pelvis.position.y:F3}");
            Assert.IsTrue(defender.StepLanded); Assert.IsTrue(defender.StepPresentationComplete);
            Assert.IsTrue(match.CanRestart); Assert.AreEqual(1, match.Round.Resolutions);
            Assert.Greater(defender.CaptureBalance().FootDisplacement, 0.12f);
            Assert.Greater(attacker.Pelvis.position.y, 0.7f);
            if (attackerIndex == 0) SaveSplitView("defeat-settled");
            match.ResetRound(); Assert.IsFalse(defender.StepLanded); Assert.AreEqual(0, match.Feedback.LowMotor(0));
        }
        [Test] public void MatchOwnsFeedbackThatCanBeClearedWithItsLifecycle()
        {
            var match = CreateMatch();
            Assert.IsNotNull(match.GetComponent("PhysicalFeedback"), "对局初始化必须接入推动反馈，而非仅有未使用的反馈脚本");
        }
        [Test] public void CameraFeedbackCannotAccumulateWithRepeatedRendering()
        {
            a.RenderView(0); Vector3 eye = a.ViewCamera.transform.position;
            a.SetFeedbackView(new Vector3(0, 0, -0.012f), Vector3.zero);
            for (int i = 0; i < 100; i++) a.RenderView(0.01f);
            Assert.LessOrEqual(Vector3.Distance(eye, a.ViewCamera.transform.position), 0.0121f,
                "镜头反馈必须相对身体观察基准叠加，不能被跟随平滑再次积累");
            a.SetFeedbackView(Vector3.zero, Vector3.zero); a.RenderView(0.01f);
            Assert.Less(Vector3.Distance(eye, a.ViewCamera.transform.position), 0.0001f);
        }
        [Test] public void FeedbackClearStopsPressureMotorsAndCameraForBothPlayers()
        {
            var match = CreateMatch();
            match.Feedback.Hand(0, 0).Tick(true, 110, 0.1f, config);
            match.Feedback.Hand(1, 1).Tick(true, 55, 0.1f, config);
            Assert.Greater(match.Feedback.LowMotor(0), 0);
            match.Freeze("暂停");
            for (int player = 0; player < 2; player++)
            {
                Assert.AreEqual(0, match.Feedback.LowMotor(player)); Assert.AreEqual(0, match.Feedback.HighMotor(player));
                Assert.AreEqual(0, match.Feedback.Hand(player, 0).Pressure);
                Assert.AreEqual(0, match.Feedback.Hand(player, 1).Pressure);
            }
        }
        [Test] public void FinishedRoundCannotBeRestartedBeforeResultPresentation()
        {
            var match = CreateMatch();
            var pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            var other = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            try
            {
                match.Input.Roster.Join(pad); match.Input.Roster.Join(other);
                match.Round.Resolve(false, true);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,
                    new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.Start));
                UnityEngine.InputSystem.InputSystem.Update();
                match.ExternalSimulation = false; match.SendMessage("Update"); match.ExternalSimulation = true;
                Assert.AreEqual(RoundPhase.Finished, match.Round.Phase, "判负展示不能被菜单键立即截断");
                Assert.AreEqual(RoundResult.PlayerOne, match.Round.Result);
            }
            finally
            {
                UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
                UnityEngine.InputSystem.InputSystem.RemoveDevice(other);
            }
        }
        [Test] public void FinishedRoundStillAcceptsObservationWhileTriggersAreHeld()
        {
            var match = CreateMatch();
            var pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            try
            {
                match.Input.Roster.Join(pad); match.Round.Resolve(false, true);
                a.RenderView(0); Quaternion before = a.ViewCamera.transform.rotation;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,
                    new UnityEngine.InputSystem.LowLevel.GamepadState { rightStick = Vector2.right, leftTrigger = 1, leftStick = Vector2.up });
                UnityEngine.InputSystem.InputSystem.Update();
                match.ExternalSimulation = false; match.SendMessage("Update"); match.ExternalSimulation = true;
                a.RenderView(0);
                Assert.Greater(Quaternion.Angle(before, a.ViewCamera.transform.rotation), 0.001f,
                    "展示阶段即使还按住推手／重心，也应能独立观察");
                Assert.AreEqual(RoundResult.PlayerOne, match.Round.Result);
            }
            finally { UnityEngine.InputSystem.InputSystem.RemoveDevice(pad); }
        }
        private static void Diagnostic(PhysicalFighter fighter, string name, int tick)
        {
            System.IO.Directory.CreateDirectory("Artifacts/Verification");
            string path = "Artifacts/Verification/physical-" + name + "-dynamics.txt";
            if (tick == 0) System.IO.File.WriteAllText(path, "");
            var state = fighter.CaptureBalance();
            string text = $"tick={tick} center={state.Center} velocity={state.Velocity} feet={state.FootDisplacement:F3}\n";
            foreach (var bone in fighter.Bones)
                text += $"{bone.Name}: pos={bone.Body.position:F3} rot={bone.Body.rotation.eulerAngles:F1} target={bone.Joint.targetRotation.eulerAngles:F1}\n";
            System.IO.File.AppendAllText(path, text);
        }
        [SetUp] public void Setup()
        {
            previousListeners.Clear();
            foreach (var listener in Object.FindObjectsOfType<AudioListener>())
                if (listener.enabled) { previousListeners.Add(listener); listener.enabled = false; }
            scene = SceneManager.CreateScene("PhysicalTest", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            config = ScriptableObject.CreateInstance<PhysicalConfig>();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(floor, scene);
            floor.transform.position = new Vector3(0, -0.06f, 0); floor.transform.localScale = new Vector3(8, 0.1f, 8);
            a = PhysicalFighter.Create(0, config, null); b = PhysicalFighter.Create(1, config, null);
            SceneManager.MoveGameObjectToScene(a.gameObject, scene); SceneManager.MoveGameObjectToScene(b.gameObject, scene);
            PhysicalFighter.ConfigureInteraction(a, b);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            Object.DestroyImmediate(config); yield return SceneManager.UnloadSceneAsync(scene);
            foreach (var listener in previousListeners) if (listener != null) listener.enabled = true;
            foreach (var material in previewMaterials) Object.DestroyImmediate(material); previewMaterials.Clear();
            foreach (var material in supportMaterials) Object.DestroyImmediate(material); supportMaterials.Clear();
        }

        [Test] public void BodyHasTwentyMassSegmentsAndFiniteJointDrives()
        {
            Assert.AreEqual(20, a.Bones.Length);
            float mass = 0;
            foreach (var bone in a.Bones)
            {
                mass += bone.Body.mass;
                Assert.IsNotNull(bone.Joint);
                Assert.Greater(bone.Joint.slerpDrive.maximumForce, 0);
                Assert.Less(bone.Joint.slerpDrive.maximumForce, 1000);
            }
            Assert.AreEqual(70, mass, 0.01f);
        }
        [Test] public void RecordedIntentsAndBodySamplesSurviveJsonRoundTrip()
        {
            var frame = new PhysicalFrame { first = new PlayerIntent { LeftForce = 0.7f, Lean = new Vector2(0.2f, -0.3f) },
                one = new BalanceSample { Center = new Vector2(0.1f, 0.2f), Finite = true } };
            string json = JsonUtility.ToJson(frame);
            StringAssert.Contains("\"first\"", json); StringAssert.Contains("\"one\"", json);
            var restored = JsonUtility.FromJson<PhysicalFrame>(json);
            Assert.AreEqual(0.7f, restored.first.LeftForce); Assert.AreEqual(frame.one.Center, restored.one.Center);
            Assert.IsTrue(restored.one.Finite);
        }
        [Test] public void MalformedOrNonfinitePolicyCannotProduceCorrection()
        {
            var policy = new LinearBalancePolicy(); policy.weights[0] = float.NaN;
            Assert.IsFalse(policy.Valid); Assert.AreEqual(Vector2.zero, policy.Evaluate(new float[10]));
            policy.weights[0] = 1; var observation = new float[10]; observation[0] = float.PositiveInfinity;
            Assert.AreEqual(Vector2.zero, policy.Evaluate(observation));
        }
        [Test] public void MalformedOrUnvalidatedPolicyJsonFallsBackToBaseline()
        {
            Assert.IsNull(LinearBalancePolicy.LoadValidated("{"));
            Assert.IsNull(LinearBalancePolicy.LoadValidated(JsonUtility.ToJson(new LinearBalancePolicy())));
            var policy = new LinearBalancePolicy { validated = true };
            Assert.IsNotNull(LinearBalancePolicy.LoadValidated(JsonUtility.ToJson(policy)));
        }
        [Test] public void BothPushingThenOneWithdrawingSeparatesActualContact()
        {
            a.SetFrozen(false); b.SetFrozen(false); bool sawContact = false;
            var push = new PlayerIntent { LeftForce = 0.8f, RightForce = 0.8f };
            for (int i = 0; i < 150; i++)
            {
                a.Drive(push, false, false, b, 0.01f); b.Drive(push, false, false, a, 0.01f);
                scene.GetPhysicsScene().Simulate(0.01f); sawContact |= a.ContactCount > 0;
                if (i % 30 == 0) Diagnostic(a, "contact", i);
            }
            Assert.IsTrue(sawContact, "双方推掌必须能形成真实合法接触");
            for (int i = 0; i < 30; i++)
            {
                a.Drive(push, false, false, b, 0.01f); b.Drive(default, true, false, a, 0.01f);
                scene.GetPhysicsScene().Simulate(0.01f);
            }
            Assert.AreEqual(0, b.ContactCount, "收力后撤应能分离接触");
        }
        [Test] public void OnlyHandsAndForearmsCollideAcrossFighters()
        {
            foreach (var x in a.Bones) foreach (var y in b.Bones)
                Assert.AreEqual(!ContactRules.IsLegal(x.Region, y.Region), Physics.GetIgnoreCollision(x.Collider, y.Collider));
        }
        [Test] public void FreezeAndResumePreserveMomentum()
        {
            a.SetFrozen(false); a.Pelvis.velocity = new Vector3(0.2f, 0, 0.5f);
            a.SetFrozen(true); Assert.IsTrue(a.Pelvis.isKinematic);
            a.SetFrozen(false); Assert.AreEqual(new Vector3(0.2f, 0, 0.5f), a.Pelvis.velocity);
        }
        [Test] public void ResetClearsMomentumAndTechnique()
        {
            a.SetFrozen(false); a.Pelvis.velocity = Vector3.one;
            a.Technique.Tick(true, false, 0.02f); a.ResetPose(); a.SetFrozen(false);
            Assert.AreEqual(Vector3.zero, a.Pelvis.velocity);
            Assert.AreEqual(Technique.None, a.Technique.Active);
        }
        [Test] public void WithdrawRequestsShorterReachThanPush()
        {
            a.SetFrozen(false); b.SetFrozen(false);
            a.Drive(new PlayerIntent { LeftForce = 1, RightForce = 1 }, false, false, b, 0.1f);
            float forward = a.transform.InverseTransformPoint(a.PalmTargets[0]).z;
            a.Drive(default, true, false, b, 0.1f);
            Assert.Less(a.transform.InverseTransformPoint(a.PalmTargets[0]).z, forward);
        }
        [Test] public void IdleStanceSurvivesFiveSecondsOfActualPhysics()
        {
            a.SetFrozen(false); b.SetFrozen(false);
            for (int i = 0; i < 250; i++)
            {
                a.Drive(default, false, false, b, 0.02f); b.Drive(default, false, false, a, 0.02f);
                scene.GetPhysicsScene().Simulate(0.02f);
                if (i % 50 == 0) Diagnostic(a, "idle", i);
            }
            Assert.Greater(a.Pelvis.position.y, 0.70f);
            Assert.Less(a.CaptureBalance().Center.magnitude, 0.25f);
        }
        [Test] public void FrozenBodiesDoNotDriftDuringCountdown()
        {
            Vector3 position = a.Pelvis.position;
            a.SetFrozen(true);
            for (int i = 0; i < 150; i++) scene.GetPhysicsScene().Simulate(0.02f);
            Assert.AreEqual(position, a.Pelvis.position);
        }
        [Test] public void RaisedGuardStaysVisibleWithoutIdleContact()
        {
            a.SetFrozen(false); b.SetFrozen(false);
            float lowestPalm = 10; int contacts = 0;
            for (int i = 0; i < 600; i++)
            {
                a.Drive(default, false, false, b, 0.01f); b.Drive(default, false, false, a, 0.01f);
                scene.GetPhysicsScene().Simulate(0.01f);
                contacts += a.ContactCount;
                if (i < 100) continue;
                lowestPalm = Mathf.Min(lowestPalm, a.Palm(0).position.y, a.Palm(1).position.y);
            }
            Assert.AreEqual(0, contacts, "准备架势不得自动接触对手");
            Assert.Greater(lowestPalm, 1.34f, "手掌应维持在胸肩之间，而不是下垂到腹部");
            a.RenderView(0);
            for (int side = 0; side < 2; side++)
            {
                Vector3 view = a.ViewCamera.WorldToViewportPoint(a.Palm(side).position);
                Assert.Greater(view.z, 0); Assert.That(view.x, Is.InRange(0.06f, 0.94f));
                Assert.That(view.y, Is.InRange(0.12f, 0.70f), "准备手位必须完整出现在第一人称视口内");
            }
        }
        [Test] public void IdleBodySettlesInsteadOfContinuouslyOscillating()
        {
            a.SetFrozen(false); b.SetFrozen(false);
            Vector3 min = Vector3.one * 100, max = Vector3.one * -100;
            float angularSquared = 0, sampledMass = 0;
            Vector3 palmMin = Vector3.one * 100, palmMax = Vector3.one * -100;
            float[] boneSquared = new float[a.Bones.Length];
            for (int i = 0; i < 1000; i++)
            {
                a.Drive(default, false, false, b, 0.01f); b.Drive(default, false, false, a, 0.01f);
                scene.GetPhysicsScene().Simulate(0.01f);
                if (i < 300) continue;
                foreach (var bone in a.Bones)
                    if (bone.Name == "Head")
                    { min = Vector3.Min(min, bone.Body.position); max = Vector3.Max(max, bone.Body.position); }
                palmMin = Vector3.Min(palmMin, a.Palm(0).position); palmMax = Vector3.Max(palmMax, a.Palm(0).position);
                for (int j = 0; j < a.Bones.Length; j++)
                { float value = a.Bones[j].Body.angularVelocity.sqrMagnitude; angularSquared += value * a.Bones[j].Body.mass;
                    sampledMass += a.Bones[j].Body.mass; boneSquared[j] += value; }
            }
            float speed = Mathf.Sqrt(angularSquared / sampledMass);
            TestContext.WriteLine($"静止头部峰峰值={(max - min):F4}，手掌峰峰值={(palmMax - palmMin):F4}，质量加权角速度RMS={speed:F4}");
            for (int j = 0; j < a.Bones.Length; j++) TestContext.WriteLine($"{a.Bones[j].Name}: {Mathf.Sqrt(boneSquared[j] / 700):F4}");
            Assert.Less((max - min).magnitude, 0.025f, "稳定后头部不应持续大幅晃动");
            Assert.Less((palmMax - palmMin).magnitude, 0.015f, "手掌也应稳定，不能只检查头部");
            Assert.Less(speed, 0.06f, "真实关节应稳定，不能只用镜头平滑掩盖抖动");
        }
        [Test] public void StartingWithoutInputDoesNotAutomaticallyRetractArms()
        {
            Vector3 start = a.Palm(0).position;
            a.SetFrozen(false);
            for (int i = 0; i < 10; i++) a.Drive(default, false, false, b, 0.01f);
            Assert.Less(Vector3.Distance(start, a.PalmTargets[0]), 0.02f, "初始架势与运行时准备架势应一致");
        }
        [Test] public void ModerateImpulseRecoversButLargeImpulseCanStillCauseStep()
        {
            a.SetFrozen(false); b.SetFrozen(false);
            for (int i = 0; i < 200; i++) SimulatePair(default, default);
            a.Pelvis.AddForce(new Vector3(4, 0, 4), ForceMode.Impulse);
            bool stepped = false;
            for (int i = 0; i < 400; i++) { SimulatePair(default, default); stepped |= a.AssessStep(0.01f); }
            Assert.IsFalse(stepped, "适度扰动后应能恢复");
            Assert.Less(a.CaptureBalance().Velocity.magnitude, 0.04f);
            a.Pelvis.AddForce(Vector3.forward * 250, ForceMode.Impulse);
            for (int i = 0; i < 50; i++) { SimulatePair(default, default); stepped |= a.AssessStep(0.01f); }
            Assert.IsTrue(stepped, "稳定辅助必须有限，强扰动仍能失衡迈步");
        }
        [Test] public void SustainedPushDoesNotChaseHandsUpToTheFace()
        {
            a.SetFrozen(false); b.SetFrozen(false);
            for (int i = 0; i < 200; i++) SimulatePair(default, default);
            float highest = 0, narrowest = 1;
            var push = new PlayerIntent { LeftForce = 0.8f, RightForce = 0.8f };
            for (int i = 0; i < 200; i++)
            {
                SimulatePair(push, push);
                for (int side = 0; side < 2; side++)
                {
                    highest = Mathf.Max(highest, a.Palm(side).position.y);
                    narrowest = Mathf.Min(narrowest, Mathf.Abs(a.transform.InverseTransformPoint(a.Palm(side).position).x));
                }
                if (a.AssessStep(0.01f) || b.AssessStep(0.01f)) break;
            }
            TestContext.WriteLine($"持续推掌最高={highest:F3}，最小手间半宽={narrowest:F3}");
            Assert.Less(highest, 1.55f, "双方手位不能因相互追踪一路抬到面部");
            Assert.Greater(narrowest, 0.09f, "左右手保持各自的操作通道");
        }
        [TestCase(0, 0f)] [TestCase(1, 0f)] [TestCase(0, 0.8f)] [TestCase(1, 0.8f)]
        public void LegalPushMovesIdleBodyAndForwardCommitmentCanForceStep(int attackerIndex, float leanAmount)
        {
            var friction = new PhysicMaterial("ActualSceneFriction") { staticFriction = 0.9f, dynamicFriction = 0.75f,
                frictionCombine = PhysicMaterialCombine.Average };
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var collider in root.GetComponentsInChildren<Collider>()) collider.sharedMaterial = friction;
                a.SetFrozen(false); b.SetFrozen(false);
                var attacker = attackerIndex == 0 ? a : b; var defender = attackerIndex == 0 ? b : a;
                for (int i = 0; i < 300; i++) SimulatePair(default, default);
                var attack = new PlayerIntent { LeftForce = 1, RightForce = 1, Lean = new Vector2(0, leanAmount) };
                int contacts = 0; float maximumShift = 0, nearestGap = 10; bool attackerStep = false, defenderStep = false;
                float impulse = 0, activePressure = 0; int ticks = 0;
                Vector2 start = defender.CaptureBalance().Center;
                for (int i = 0; i < 600; i++)
                {
                    SimulatePair(attackerIndex == 0 ? attack : default, attackerIndex == 0 ? default : attack);
                    contacts += attacker.ContactCount > 0 ? 1 : 0;
                    Vector3 transferred = Vector3.zero;
                    foreach (var bone in defender.Bones) transferred += bone.Body.GetComponent<PhysicalContact>().Impulse;
                    impulse += Mathf.Abs(Vector3.Dot(transferred, attacker.transform.forward)); ticks++;
                    activePressure += Vector3.Dot(attacker.ActivePushForce, attacker.transform.forward);
                    maximumShift = Mathf.Max(maximumShift, Vector2.Distance(start, defender.CaptureBalance().Center));
                    nearestGap = Mathf.Min(nearestGap, Vector3.Distance(attacker.Palm(0).position, defender.Palm(1).position));
                    attackerStep |= attacker.AssessStep(0.01f); defenderStep |= defender.AssessStep(0.01f);
                    if (defenderStep || attackerStep) break;
                }
                TestContext.WriteLine($"攻击方={attackerIndex} 接触帧={contacts} 防守重心最大位移={maximumShift:F4} 最近掌心距离={nearestGap:F4} 被动接触力={impulse / (ticks * 0.01f):F1}N 主动压力={activePressure / ticks:F1}N 攻击迈步={attackerStep} 防守迈步={defenderStep}");
                TestContext.WriteLine($"进攻重心={attacker.CaptureBalance().Center:F3} 趋势={attacker.CaptureBalance().Predicted:F3} 防守重心={defender.CaptureBalance().Center:F3} 趋势={defender.CaptureBalance().Predicted:F3}");
                Assert.Greater(contacts, 10, "单方发力必须能触及静止对手的手／前臂");
                Assert.Greater(maximumShift, 0.04f, "接触载荷应让身体产生可见位移，再触发保护步");
                Assert.IsFalse(attackerStep, "合理发力不应固定由攻击者先失衡");
                // 仅手臂顶压也要推得动身体；保证逼出保护步的情形是配合主动重心前压。
                if (leanAmount <= 0) return;
                Assert.IsTrue(defenderStep, "合理的持续发力与前倾应能逼迫完全不操作的对手出步");
                float stepDistance = 0;
                for (int i = 0; i < 60; i++)
                {
                    a.PresentStep(0.01f); b.PresentStep(0.01f); scene.GetPhysicsScene().Simulate(0.01f);
                    stepDistance = Mathf.Max(stepDistance, defender.CaptureBalance().FootDisplacement);
                }
                TestContext.WriteLine($"保护步实际足部位移={stepDistance:F3}m");
                Assert.Greater(stepDistance, 0.10f, "判负必须伴随真实保护步，不能站着不动只改结果");
            }
            finally { Object.DestroyImmediate(friction); }
        }
        [Test] public void SingleHandPushAndReleaseRemainReadableInSplitView()
        {
            a.SetFrozen(false); b.SetFrozen(false);
            var firstMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.18f, 0.48f, 0.72f) };
            var secondMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.86f, 0.42f, 0.20f) };
            var lampObject = new GameObject("PreviewLight"); SceneManager.MoveGameObjectToScene(lampObject, scene);
            lampObject.layer = 18; lampObject.transform.rotation = Quaternion.Euler(45, -25, 0);
            var lamp = lampObject.AddComponent<Light>(); lamp.type = LightType.Directional; lamp.intensity = 1.4f;
            try
            {
                foreach (var fighter in new[] { a, b })
                {
                    foreach (var bone in fighter.Bones)
                    {
                        bone.Body.GetComponent<Renderer>().sharedMaterial = fighter == a ? firstMaterial : secondMaterial;
                        bone.Body.gameObject.layer = bone.Region == ContactRegion.Head ? 16 + fighter.PlayerIndex : 14 + fighter.PlayerIndex;
                    }
                    fighter.ViewCamera.cullingMask = (1 << 14) | (1 << 15) | (1 << (17 - fighter.PlayerIndex)) | (1 << 18);
                }
                foreach (var root in scene.GetRootGameObjects()) if (root.name == "Cube") root.layer = 18;
                for (int i = 0; i < 200; i++) SimulatePair(default, default);
                SaveSplitView("guard");
                for (int i = 0; i < 80; i++) SimulatePair(new PlayerIntent { LeftForce = 0.8f }, new PlayerIntent { RightForce = 0.8f });
                Assert.Greater(a.Effort[0], 0.7f); Assert.AreEqual(0, a.Effort[1]);
                Assert.Greater(a.transform.InverseTransformPoint(a.PalmTargets[0]).z,
                    a.transform.InverseTransformPoint(a.PalmTargets[1]).z + 0.04f);
                SaveSplitView("left-push");
                for (int i = 0; i < 30; i++) SimulatePair(default, new PlayerIntent { RightForce = 0.8f });
                Assert.AreEqual(0, a.Effort[0]);
                Assert.That(a.transform.InverseTransformPoint(a.PalmTargets[0]).z, Is.InRange(config.GuardReach - 0.03f, config.GuardReach + 0.03f));
                SaveSplitView("release");
            }
            finally { Object.DestroyImmediate(firstMaterial); Object.DestroyImmediate(secondMaterial); }
        }
        [TestCase(0)] [TestCase(1)] public void ActiveWeightShiftImprovesDefenseComparedWithIdleGuard(int attackerIndex)
        {
            var friction = new PhysicMaterial("ActualDefenseFriction") { staticFriction = 0.9f, dynamicFriction = 0.75f,
                frictionCombine = PhysicMaterialCombine.Average };
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var collider in root.GetComponentsInChildren<Collider>()) collider.sharedMaterial = friction;
                float idle = MeasureDefenseTime(attackerIndex, 0, out bool idleFailed, out bool idleAttackerFailed);
                float active = MeasureDefenseTime(attackerIndex, 0.7f, out bool activeFailed, out bool activeAttackerFailed);
                TestContext.WriteLine($"同样进攻：待机={idle:F2}s，主动调整={active:F2}s");
                Assert.IsTrue(idleFailed); Assert.IsFalse(idleAttackerFailed);
                // 并排后迎推可通过真实反作用先逼进攻者迈步，不能把提前反制获胜误判为防守变差。
                bool countered = activeAttackerFailed && !activeFailed;
                Assert.IsTrue(countered || active > idle + 0.4f,
                    "主动调整重心应争取反应时间，或在自己未迈步时通过真实接触反制获胜");
                Assert.Greater(active, config.StepDelay, "应保留实际接触和重心反应过程，不能瞬间结算");
            }
            finally { Object.DestroyImmediate(friction); }
        }
        private float MeasureDefenseTime(int attackerIndex, float braceAmount, out bool defenderFailed, out bool attackerFailed)
        {
            a.ResetPose(); b.ResetPose(); a.SetFrozen(false); b.SetFrozen(false); Physics.SyncTransforms();
            for (int i = 0; i < 300; i++) SimulatePair(default, default);
            var attack = new PlayerIntent { LeftForce = 1, RightForce = 1, Lean = Vector2.up * 0.8f };
            var brace = new PlayerIntent { Lean = Vector2.up * braceAmount };
            for (int i = 0; i < 100 && braceAmount > 0; i++)
            {
                SimulatePair(attackerIndex == 0 ? default : brace, attackerIndex == 0 ? brace : default);
                Assert.IsFalse(a.AssessStep(0.01f), "进攻前准备迎推不能让玩家一自行迈步");
                Assert.IsFalse(b.AssessStep(0.01f), "进攻前准备迎推不能让玩家二自行迈步");
            }
            var attacker = attackerIndex == 0 ? a : b; var defender = attackerIndex == 0 ? b : a;
            TestContext.WriteLine($"防守准备：攻击方={attackerIndex} 前倾={braceAmount:F2} 进攻重心={attacker.CaptureBalance().Center:F3} 防守重心={defender.CaptureBalance().Center:F3} 防守预测={defender.CaptureBalance().Predicted:F3}");
            defenderFailed = attackerFailed = false;
            for (int i = 0; i < 500; i++)
            {
                SimulatePair(attackerIndex == 0 ? attack : brace, attackerIndex == 0 ? brace : attack);
                defenderFailed = defender.AssessStep(0.01f); attackerFailed = attacker.AssessStep(0.01f);
                if (defenderFailed || attackerFailed)
                {
                    TestContext.WriteLine($"防守结束：进攻迈步={attackerFailed} 防守迈步={defenderFailed} 进攻重心={attacker.CaptureBalance().Center:F3} 进攻需求={attacker.CaptureBalance().RequiredSupport:F3} 进攻预测={attacker.CaptureBalance().Predicted:F3} 防守重心={defender.CaptureBalance().Center:F3} 防守需求={defender.CaptureBalance().RequiredSupport:F3} 防守预测={defender.CaptureBalance().Predicted:F3}");
                    return (i + 1) * 0.01f;
                }
            }
            return 5;
        }
        [Test] public void PushCannotApplyActivePressureWithoutLegalContact()
        {
            b.SetFrozen(true); a.SetFrozen(false);
            b.transform.position += Vector3.right * 3; Physics.SyncTransforms();
            var push = new PlayerIntent { LeftForce = 1, RightForce = 1 };
            Vector3 position = b.Pelvis.position;
            for (int i = 0; i < 100; i++)
            {
                a.Drive(push, false, false, b, 0.01f); scene.GetPhysicsScene().Simulate(0.01f);
                Assert.AreEqual(Vector3.zero, a.ActivePushForce, "没有目标和合法接触时不能凭空推人");
            }
            Assert.AreEqual(position, b.Pelvis.position);
        }
        [TestCase(0)] [TestCase(1)] public void EarlyWithdrawCanAvoidTheInitialCommittedPush(int attackerIndex)
        {
            a.SetFrozen(false); b.SetFrozen(false);
            for (int i = 0; i < 300; i++) SimulatePair(default, default);
            var attacker = attackerIndex == 0 ? a : b; var defender = attackerIndex == 0 ? b : a;
            var attack = new PlayerIntent { LeftForce = 1, RightForce = 1, Lean = Vector2.up * 0.8f };
            bool defenderStep = false, attackerStep = false; int unloaded = 0;
            for (int i = 0; i < 50; i++)
            {
                attacker.Drive(attack, false, false, defender, 0.01f);
                defender.Drive(default, i == 8, false, attacker, 0.01f);
                scene.GetPhysicsScene().Simulate(0.01f);
                if (i >= 15 && attacker.ActivePushForce.sqrMagnitude < 0.001f) unloaded++;
                attackerStep |= attacker.AssessStep(0.01f); defenderStep |= defender.AssessStep(0.01f);
                if (attackerStep || defenderStep) break;
            }
            TestContext.WriteLine($"提前撤手：防守迈步={defenderStep}，进攻迈步={attackerStep}，卸载帧={unloaded}");
            Assert.IsFalse(defenderStep, "正确时机撤手应能避开原本打破待机架势的首轮进攻");
            Assert.Greater(unloaded, 10, "撤手要实际撤去接触压力，而非只播放动作");
        }
        [TestCase(-0.55f)] [TestCase(0.55f)]
        public void OrdinaryLeanCanBeHeldAndReleasedWithoutProtectiveStep(float amount)
        {
            PrepareLowArena();
            a.SetFrozen(false); b.SetFrozen(false);
            for (int i = 0; i < 250; i++) SimulatePair(default, default);
            bool stepped = false; float firstStep = -1, peakSpeed = 0;
            for (int i = 0; i < 650; i++)
            {
                float input = i < 450 ? amount : 0;
                SimulatePair(new PlayerIntent { Lean = Vector2.up * input }, default);
                bool failed = a.AssessStep(0.01f);
                if (failed && firstStep < 0) firstStep = i * 0.01f;
                stepped |= failed; peakSpeed = Mathf.Max(peakSpeed, a.CaptureBalance().Velocity.magnitude);
            }
            TestContext.WriteLine($"普通倾身={amount} 首次迈步={firstStep:F2}s 峰值速度={peakSpeed:F3} 重心={a.CaptureBalance().Center:F3}");
            Assert.IsFalse(stepped, "普通幅度主动倾身应稳得住并能松开恢复");
            Assert.Less(a.CaptureBalance().Velocity.magnitude, 0.06f);
        }
        [TestCase(-1f)] [TestCase(1f)]
        public void ExtremeLeanHasRecoveryWindowButCanEventuallyLoseBalance(float amount)
        {
            a.SetFrozen(false); b.SetFrozen(false);
            for (int i = 0; i < 250; i++) SimulatePair(default, default);
            float firstStep = -1;
            for (int i = 0; i < 700; i++)
            {
                SimulatePair(new PlayerIntent { Lean = Vector2.up * amount }, default);
                if (a.AssessStep(0.01f)) { firstStep = i * 0.01f; break; }
            }
            TestContext.WriteLine($"极限倾身={amount} 首次迈步={firstStep:F2}s 重心={a.CaptureBalance().Center:F3}");
            Assert.Greater(firstStep, 1.0f, "极限动作也应留出察觉和收回的机会");
            Assert.Less(firstStep, 7f, "持续极限倾身本身应有失衡风险");
        }
        [TestCase(-1f)] [TestCase(1f)]
        public void BriefFullLeanCanBeReleasedBeforeLosingBalance(float amount)
        {
            PrepareLowArena();
            a.SetFrozen(false); b.SetFrozen(false);
            for (int i = 0; i < 250; i++) SimulatePair(default, default);
            for (int i = 0; i < 300; i++)
            {
                SimulatePair(new PlayerIntent { Lean = i < 45 ? Vector2.up * amount : Vector2.zero }, default);
                Assert.IsFalse(a.AssessStep(0.01f), $"短按极限倾身后收回应能恢复，方向={amount} tick={i}");
            }
            Assert.Less(a.CaptureBalance().Velocity.magnitude, 0.06f);
        }
        [Test] public void LeanDirectionChangesDoNotThrowTheBodyAcrossItsSupport()
        {
            PrepareLowArena();
            a.SetFrozen(false); b.SetFrozen(false);
            for (int i = 0; i < 250; i++) SimulatePair(default, default);
            for (int i = 0; i < 600; i++)
            {
                float amount = i < 400 ? (i / 100 % 2 == 0 ? 0.55f : -0.55f) : 0;
                SimulatePair(new PlayerIntent { Lean = Vector2.up * amount }, default);
                Assert.IsFalse(a.AssessStep(0.01f), $"普通倾身反向不应把自己甩出支撑范围，tick={i}");
            }
        }
        private PhysicalArena PrepareLowArena(float height = 0.55f, float width = -1, float depth = -1)
        {
            if (width < 0) width = config.PlatformSize.x;
            if (depth < 0) depth = config.PlatformSize.y;
            var floor = scene.GetRootGameObjects()[0];
            floor.transform.position = new Vector3(0, -height - 0.06f, 0);
            var arena = floor.AddComponent<PhysicalArena>();
            var platforms = new BoxCollider[2];
            for (int i = 0; i < 2; i++)
            {
                var platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(platform, scene);
                platform.name = "LowPlatform" + i;
                platform.transform.rotation = (i == 0 ? a : b).transform.rotation;
                platform.transform.position = new Vector3(0, -height * 0.5f - 0.01f, (i == 0 ? -1 : 1) * config.Spacing * 0.5f)
                    + platform.transform.forward * config.PlatformForwardOffset;
                arena.SpawnOffsets[i] = Vector3.back * config.PlatformForwardOffset;
                platform.transform.localScale = new Vector3(width, height, depth);
                platforms[i] = platform.GetComponent<BoxCollider>();
            }
            arena.Platforms = platforms; arena.LandingGround = floor.GetComponent<Collider>();
            var friction = new PhysicMaterial("LowArenaSceneFriction")
            { staticFriction = 0.9f, dynamicFriction = 0.75f, bounciness = 0, frictionCombine = PhysicMaterialCombine.Average };
            supportMaterials.Add(friction);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var collider in root.GetComponentsInChildren<Collider>()) collider.sharedMaterial = friction;
            a.Arena = b.Arena = arena;
            Physics.SyncTransforms(); return arena;
        }
        [TestCase(0)] [TestCase(1)]
        public void LowPlatformSpawnOffsetKeepsBothStandingFeetOverTheirOwnPlatform(int playerIndex)
        {
            var arena = PrepareLowArena();
            var fighter = playerIndex == 0 ? a : b;
            Vector3 originalSpawn = fighter.transform.position;
            fighter.RelocateSpawn(arena.SpawnPosition(playerIndex));
            Physics.SyncTransforms();
            Assert.Less(Vector3.Distance(originalSpawn, fighter.transform.position), 0.0001f,
                "台面偏移不应改变双方接触距离，重建出生位置也不能抵消台沿调校");
            var top = arena.Platforms[playerIndex].bounds;
            foreach (var bone in fighter.Bones)
            {
                if (!bone.Name.EndsWith("Foot")) continue;
                var sole = bone.Collider.bounds;
                Assert.GreaterOrEqual(sole.min.x, top.min.x - 0.0001f);
                Assert.LessOrEqual(sole.max.x, top.max.x + 0.0001f);
                Assert.GreaterOrEqual(sole.min.z, top.min.z - 0.0001f);
                Assert.LessOrEqual(sole.max.z, top.max.z + 0.0001f);
            }
        }
        [TestCase(0)] [TestCase(1)]
        public void LosingOnLowPlatformFallsOntoActualLowerGround(int attackerIndex)
        {
            PrepareLowArena(); var match = CreateMatch(); PrepareResultPreview();
            var attacker = attackerIndex == 0 ? a : b; var loser = attackerIndex == 0 ? b : a;
            for (int i = 0; i < 300; i++)
            { match.Tick(default, default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f); }
            var push = new PlayerIntent { LeftForce = 1, RightForce = 1, Lean = Vector2.up * 0.8f };
            var retreat = new PlayerIntent { Lean = Vector2.down * 0.75f };
            for (int i = 0; i < 650 && match.Round.Phase == RoundPhase.Fighting; i++)
            {
                match.Tick(attackerIndex == 0 ? push : retreat, attackerIndex == 0 ? retreat : push, false, false, false, false, 0.01f);
                scene.GetPhysicsScene().Simulate(0.01f);
            }
            Assert.AreEqual(attackerIndex == 0 ? RoundResult.PlayerOne : RoundResult.PlayerTwo, match.Round.Result);
            TestContext.WriteLine($"判负速度={loser.CaptureBalance().Velocity:F3} 趋势={loser.CaptureBalance().Predicted:F3} 支撑需求={loser.CaptureBalance().RequiredSupport:F3}");
            bool sawLandingFeedback = false, sawGround = false, checkedFootLanding = false;
            for (int i = 0; i < 650; i++)
            {
                match.Tick(push, push, true, true, true, true, 0.01f);
                if (loser.StepLanded && !checkedFootLanding)
                {
                    PhysicalBone moved = null; float farthest = -1;
                    foreach (var bone in loser.Bones)
                    {
                        if (!bone.Name.EndsWith("Foot")) continue;
                        Vector3 delta = bone.Body.position - bone.StartPosition; delta.y = 0;
                        if (delta.sqrMagnitude > farthest) { moved = bone; farthest = delta.sqrMagnitude; }
                    }
                    Assert.IsTrue(loser.Arena.HasPlatformContact(moved.Body.GetComponent<PhysicalContact>()), "搭沿落脚必须对应真实足部接触");
                    checkedFootLanding = true;
                }
                if (loser.GroundLanded && !sawGround)
                { sawGround = true; sawLandingFeedback = match.Feedback.LowMotor(1 - attackerIndex) > 0.05f; }
                scene.GetPhysicsScene().Simulate(0.01f);
            }
            TestContext.WriteLine($"低台：赢家={attackerIndex} 输家骨盆={loser.Pelvis.position:F3} 赢家骨盆={attacker.Pelvis.position:F3} 完成={loser.StepPresentationComplete}");
            Assert.Less(loser.Pelvis.position.y, 0.2f, "保护步踩空后必须跌落，不能被旧的站姿锚点托在台面高度");
            Assert.Greater(attacker.Pelvis.position.y, 0.7f, "清空发力后赢家应保持站立");
            Assert.IsTrue(loser.StepPresentationComplete); Assert.IsTrue(match.CanRestart);
            Assert.IsTrue(loser.Falling); Assert.IsTrue(loser.GroundLanded);
            if (loser.StepLanded) Assert.IsTrue(checkedFootLanding, "允许先真实搭沿再滚落，不伪报落脚");
            Assert.IsFalse(loser.PresentationTimedOut);
            Assert.IsTrue(sawLandingFeedback, "实际摔落接地应触发反馈，不能继续只监听成功保护步");
            foreach (var bone in loser.Bones)
                Assert.AreEqual(0, loser.ViewCamera.cullingMask & (1 << bone.Body.gameObject.layer),
                    "倒地视角不能被自己的肩膀或躯干堵住，但对手仍应看得到身体");
            Assert.AreEqual(1, match.Round.Resolutions);
            attacker.RenderView(0);
            bool bodyVisible = false;
            foreach (var bone in loser.Bones)
            {
                if (bone.Region != ContactRegion.Torso) continue;
                Vector3 view = attacker.ViewCamera.WorldToViewportPoint(bone.Body.position);
                bodyVisible |= view.z > 0 && view.x > 0.08f && view.x < 0.92f && view.y > 0.08f && view.y < 0.92f;
            }
            Assert.IsTrue(bodyVisible, "胜者正常第一人称视野内应能看清对手摔落后的身体，而非只剩屏幕底边的一角");
            if (attackerIndex == 0) SaveSplitView("arena-settled");
            match.ResetRound();
            Assert.Greater(loser.Pelvis.position.y, 0.85f);
            Assert.IsFalse(loser.StepPresentationComplete);
            foreach (var bone in loser.Bones)
                if (bone.Region == ContactRegion.Torso)
                    Assert.AreNotEqual(0, loser.ViewCamera.cullingMask & (1 << bone.Body.gameObject.layer), "重开后应恢复正常第一人称身体可见性");
        }
        [TestCase(-1f)] [TestCase(1f)]
        public void ExtremeLeanOnLowPlatformUsesItsActualFallDirection(float amount)
        {
            PrepareLowArena(); var match = CreateMatch();
            var lean = new PlayerIntent { Lean = Vector2.up * amount };
            for (int i = 0; i < 350 && match.Round.Phase == RoundPhase.Fighting; i++)
            { match.Tick(lean, default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f); }
            Assert.AreEqual(RoundResult.PlayerTwo, match.Round.Result);
            for (int i = 0; i < 850 && !match.CanRestart; i++)
            { match.Tick(default, default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f); }
            TestContext.WriteLine($"极限失足方向={amount} 前后位移={a.Pelvis.position.z - a.transform.position.z:F3} 落地={a.GroundLanded} 超时={a.PresentationTimedOut}");
            Assert.IsFalse(a.PresentationTimedOut);
            if (a.Falling)
            {
                Assert.IsTrue(a.GroundLanded);
                Assert.Greater((a.Pelvis.position.z - a.transform.position.z) * amount, 0.12f);
            }
            else
            {
                Assert.IsTrue(a.StepLanded, "脚底实际仍搭台面时应保留真实保护步");
                Assert.Greater(a.CaptureBalance().FootDisplacement, 0.12f);
            }
            Assert.IsTrue(match.CanRestart);
        }
        [TestCase("backward", 0)] [TestCase("backward", 1)]
        [TestCase("forward", 0)] [TestCase("forward", 1)]
        [TestCase("push-idle", 0)] [TestCase("push-idle", 1)]
        public void DefaultLowPlatformShowsRealFallsForForwardBackwardAndIdleDefense(string scenario, int playerIndex)
        {
            PrepareLowArena(); var match = CreateMatch();
            for (int i = 0; i < 300; i++)
            { match.Tick(default, default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f); }
            bool pushing = scenario.StartsWith("push");
            var input = pushing
                ? new PlayerIntent { LeftForce = 1, RightForce = 1, Lean = Vector2.up * 0.8f }
                : new PlayerIntent { Lean = scenario == "backward" ? Vector2.down : Vector2.up };
            var active = playerIndex == 0 ? a : b;
            var loser = pushing ? (playerIndex == 0 ? b : a) : active;
            for (int i = 0; i < 700 && match.Round.Phase == RoundPhase.Fighting; i++)
            { match.Tick(playerIndex == 0 ? input : default, playerIndex == 1 ? input : default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f); }
            Assert.AreEqual(loser == b ? RoundResult.PlayerOne : RoundResult.PlayerTwo, match.Round.Result);
            var balance = loser.CaptureBalance();
            TestContext.WriteLine($"{scenario} 判负时: 重心={balance.Center:F3} 速度={balance.Velocity:F3} 需求={balance.RequiredSupport:F3} 预测={balance.Predicted:F3}");
            for (int i = 0; i < 850 && !match.CanRestart; i++)
            {
                match.Tick(default, default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f);
            }
            TestContext.WriteLine($"{scenario} 最终: 摔落={loser.Falling} 接地={loser.GroundLanded} 落脚={loser.StepLanded} 骨盆={loser.transform.InverseTransformPoint(loser.Pelvis.position):F3}");
            Assert.IsTrue(loser.Falling, "默认小台上此代表性失衡应体现实际失足，不能只回归向前扑空");
            Assert.IsTrue(loser.GroundLanded); Assert.IsFalse(loser.PresentationTimedOut);
        }
        [TestCase(0.35f)] [TestCase(0.95f)]
        public void RaisedPlatformsUseSpawnRelativeStandingAndFailureHeights(float elevation)
        {
            var arena = PrepareLowArena();
            foreach (var platform in arena.Platforms) platform.transform.position += Vector3.up * elevation;
            Physics.SyncTransforms();
            a.RelocateSpawn(arena.SpawnPosition(0)); b.RelocateSpawn(arena.SpawnPosition(1));
            a.SetFrozen(false); b.SetFrozen(false);
            for (int i = 0; i < 400; i++)
            { SimulatePair(default, default); Assert.IsFalse(a.AssessStep(0.01f)); }
            Assert.That(a.Pelvis.position.y - elevation, Is.InRange(0.85f, 1.02f));
            // 整个人偶压低至台面附近；高台不能让旧的绝对高度阈值掩盖塌陷。
            foreach (var bone in a.Bones) bone.Body.position -= Vector3.up * 0.45f;
            for (int i = 0; i < 25; i++) a.AssessStep(0.01f);
            Assert.IsTrue(a.Stepping);
        }
        [Test] public void WidePlatformKeepsARealSupportedStepInsteadOfForcingAFall()
        {
            PrepareLowArena(0.55f, 0.82f, 0.86f); var match = CreateMatch();
            for (int i = 0; i < 300; i++)
            { match.Tick(default, default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f); }
            var push = new PlayerIntent { LeftForce = 1, RightForce = 1, Lean = Vector2.up * 0.8f };
            for (int i = 0; i < 650 && match.Round.Phase == RoundPhase.Fighting; i++)
            { match.Tick(push, default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f); }
            Assert.AreEqual(RoundResult.PlayerOne, match.Round.Result);
            for (int i = 0; i < 500; i++)
            { match.Tick(default, default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f); }
            Assert.IsTrue(b.StepLanded); Assert.IsFalse(b.Falling); Assert.IsFalse(b.GroundLanded);
            Assert.IsTrue(match.CanRestart); Assert.IsFalse(b.PresentationTimedOut);
        }
        [Test] public void FootStillOverlappingTheEdgeIsNotTreatedAsCompletelyUnsupported()
        {
            var arena = PrepareLowArena();
            PhysicalBone foot = null;
            foreach (var bone in a.Bones) if (bone.Name == "RightFoot") foot = bone;
            foot.Body.position = new Vector3(arena.Platforms[0].bounds.max.x + 0.02f, foot.Body.position.y, foot.Body.position.z);
            Physics.SyncTransforms();
            // 中心越界，但足底仍覆盖台面；不能等同于整只脚踩空。
            Assert.IsTrue(arena.HasPlatformBelow(foot.Collider));
        }
        [TestCase(-1f)] [TestCase(1f)]
        public void FallBeginsOnlyAfterTheActualSteppingFootLeavesPlatformFootprint(float amount)
        {
            var arena = PrepareLowArena(); var match = CreateMatch();
            for (int i = 0; i < 350 && match.Round.Phase == RoundPhase.Fighting; i++)
            { match.Tick(new PlayerIntent { Lean = Vector2.up * amount }, default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f); }
            for (int i = 0; i < 100 && !a.Falling; i++)
            { match.Tick(default, default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f); }
            if (!a.Falling) { Assert.IsTrue(a.StepLanded, "实际仍有支撑时应完成真实落脚"); return; }
            PhysicalBone moved = null; float distance = -1;
            foreach (var bone in a.Bones)
            {
                if (!bone.Name.EndsWith("Foot")) continue;
                Vector3 delta = bone.Body.position - bone.StartPosition; delta.y = 0;
                if (delta.sqrMagnitude > distance) { moved = bone; distance = delta.sqrMagnitude; }
            }
            // 核对脚底投影，不能拿未来落点或抬脚产生的离地高度代替越沿。
            bool overlaps = false;
            foreach (var platform in arena.Platforms)
            {
                var x = moved.Collider.bounds; var p = platform.bounds;
                overlaps |= x.max.y >= p.max.y - 0.025f && x.min.x < p.max.x && x.max.x > p.min.x && x.min.z < p.max.z && x.max.z > p.min.z;
            }
            Assert.IsFalse(overlaps, "Falling切换时实际迈步脚仍覆盖台面，不能预先撤去支撑");
        }
        [Test] public void SideWallContactCannotBeUsedAsFootSupport()
        {
            var arena = PrepareLowArena();
            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(probe, scene);
            probe.transform.localScale = Vector3.one * 0.08f;
            probe.transform.position = new Vector3(arena.Platforms[0].bounds.max.x + 0.02f, -0.25f, a.transform.position.z);
            var body = probe.AddComponent<Rigidbody>(); body.useGravity = false;
            var sensor = probe.AddComponent<PhysicalContact>(); sensor.Owner = a; sensor.Region = ContactRegion.Leg;
            Physics.SyncTransforms(); scene.GetPhysicsScene().Simulate(0.01f);
            Assert.IsTrue(sensor.TouchesEnvironment(arena.Platforms[0]), "确认真实侧壁碰撞已发生");
            Assert.IsFalse(arena.HasPlatformContact(sensor), "碰到台侧面不能伪报落脚支撑");
        }
        [Test] public void ProtectiveStepPreservesTheCommittedTorsoPoseBeforeLanding()
        {
            PrepareLowArena(); var match = CreateMatch();
            for (int i = 0; i < 350 && match.Round.Phase == RoundPhase.Fighting; i++)
            { match.Tick(new PlayerIntent { Lean = Vector2.up }, default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f); }
            Assert.IsTrue(a.Stepping);
            PhysicalBone chest = null;
            foreach (var bone in a.Bones) if (bone.Name == "UpperThorax") chest = bone;
            Quaternion before = chest.GoalRotation;
            Assert.Greater(Quaternion.Angle(before, chest.StartRotation), 1f, "确认失衡时身体有实际倾斜目标");
            match.Tick(default, default, false, false, false, false, 0.01f);
            Assert.Less(Quaternion.Angle(before, chest.GoalRotation), 0.5f, "保护步不能一帧切回开局直立目标，将失衡动作强行拉回");
        }
        [Test] public void MissingLandingSurfacesHasAnExplicitRestartFallbackWithoutInventingLanding()
        {
            var arena = PrepareLowArena(); arena.LandingGround.enabled = false;
            var match = CreateMatch(); var lean = new PlayerIntent { Lean = Vector2.up };
            for (int i = 0; i < 350 && match.Round.Phase == RoundPhase.Fighting; i++)
            { match.Tick(lean, default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f); }
            Assert.AreEqual(RoundResult.PlayerTwo, match.Round.Result);
            // 连台面的躯干承托也移除，确保测试的确不存在任何真实落地面。
            foreach (var platform in arena.Platforms) platform.enabled = false;
            LogAssert.Expect(LogType.Warning, "玩家1结束动作超时，已开放重开；未将其记为真实落地。");
            for (int i = 0; i < 850 && !match.CanRestart; i++)
            { match.Tick(default, default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f); }
            Assert.IsTrue(a.PresentationTimedOut); Assert.IsFalse(a.GroundLanded); Assert.IsTrue(match.CanRestart);
            Assert.AreEqual(RoundResult.PlayerTwo, match.Round.Result); Assert.AreEqual(1, match.Round.Resolutions);
        }
        [TestCase(-1f)] [TestCase(1f)]
        public void SidewaysStepLeavesTheCorrectSideOfThePlatform(float direction)
        {
            PrepareLowArena(); var match = CreateMatch();
            for (int i = 0; i < 250; i++)
            { match.Tick(default, default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f); }
            a.Pelvis.AddForce(Vector3.right * direction * 70, ForceMode.Impulse);
            for (int i = 0; i < 650 && match.Round.Phase == RoundPhase.Fighting; i++)
            { match.Tick(default, default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f); }
            Assert.AreEqual(RoundResult.PlayerTwo, match.Round.Result);
            for (int i = 0; i < 850 && !match.CanRestart; i++)
            {
                match.Tick(default, default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f);
            }
            TestContext.WriteLine($"侧向={direction} 位移={a.Pelvis.position.x:F3} 落地={a.GroundLanded} 摔落={a.Falling} 落脚={a.StepLanded}");
            Assert.IsTrue(a.GroundLanded); Assert.IsFalse(a.PresentationTimedOut);
            Assert.Greater(a.Pelvis.position.x * direction, 0.12f);
        }
        [Test] public void FallingCanPauseResumeAndResetForAnotherPhysicalRound()
        {
            PrepareLowArena(); var match = CreateMatch();
            var pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            var other = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            try
            {
                match.Input.Roster.Join(pad); match.Input.Roster.Join(other);
                for (int round = 0; round < 2; round++)
                {
                    if (round > 0)
                    {
                        match.ResetRound(); match.Round.Ready(); match.Round.Start();
                        match.Tick(default, default, false, false, false, false, 3);
                    }
                    for (int i = 0; i < 350 && match.Round.Phase == RoundPhase.Fighting; i++)
                    { match.Tick(new PlayerIntent { Lean = Vector2.up }, default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f); }
                    for (int i = 0; i < 80 && !a.Falling; i++)
                    { match.Tick(default, default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f); }
                    Assert.IsTrue(a.Falling);
                    match.Freeze("测试摔落暂停"); Vector3 position = a.Pelvis.position; float time = match.ResultSeconds;
                    for (int i = 0; i < 50; i++)
                    { match.Tick(default, default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f); }
                    Assert.AreEqual(position, a.Pelvis.position); Assert.AreEqual(time, match.ResultSeconds);
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,
                        new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.Start));
                    UnityEngine.InputSystem.InputSystem.Update(); match.ExternalSimulation = false; match.SendMessage("Update"); match.ExternalSimulation = true;
                    Assert.IsFalse(match.ResultPaused);
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState());
                    UnityEngine.InputSystem.InputSystem.Update();
                    for (int i = 0; i < 850 && !match.CanRestart; i++)
                    { match.Tick(default, default, false, false, false, false, 0.01f); scene.GetPhysicsScene().Simulate(0.01f); }
                    Assert.IsTrue(a.GroundLanded); Assert.IsFalse(a.PresentationTimedOut);
                    Assert.IsTrue(match.CanRestart); Assert.AreEqual(1, match.Round.Resolutions);
                }
            }
            finally
            {
                UnityEngine.InputSystem.InputSystem.RemoveDevice(pad); UnityEngine.InputSystem.InputSystem.RemoveDevice(other);
            }
        }
        private void SimulatePair(PlayerIntent first, PlayerIntent second)
        {
            a.Drive(first, false, false, b, 0.01f); b.Drive(second, false, false, a, 0.01f);
            scene.GetPhysicsScene().Simulate(0.01f);
        }
        private void SaveSplitView(string phase)
        {
            var target = new RenderTexture(1600, 900, 24);
            var previousActive = RenderTexture.active;
            Texture2D image = null;
            try
            {
                foreach (var fighter in new[] { a, b })
                {
                    fighter.RenderView(0); fighter.ViewCamera.targetTexture = target;
                    fighter.ViewCamera.Render(); fighter.ViewCamera.targetTexture = null;
                }
                RenderTexture.active = target; image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
                System.IO.Directory.CreateDirectory("Artifacts/Verification");
                System.IO.File.WriteAllBytes("Artifacts/Verification/stance-" + phase + "-split.png", image.EncodeToPNG());
            }
            finally
            {
                a.ViewCamera.targetTexture = b.ViewCamera.targetTexture = null; RenderTexture.active = previousActive;
                if (image != null) Object.DestroyImmediate(image);
                target.Release(); Object.DestroyImmediate(target);
            }
        }
        [Test] public void SameStepFailureResolvesOnceAsDraw()
        {
            var rules = new PhysicalRound(); rules.Ready(); rules.Start(); rules.Advance(3.01f);
            rules.Resolve(true, true); rules.Resolve(true, false);
            Assert.AreEqual(RoundResult.Draw, rules.Result); Assert.AreEqual(1, rules.Resolutions);
        }
    }
}
