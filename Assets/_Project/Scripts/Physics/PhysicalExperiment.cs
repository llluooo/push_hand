using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PushHands.Physical
{
    [Serializable] public sealed class ReplayIntent
    { public PlayerIntent first, second; public bool withdrawOne, feintOne, withdrawTwo, feintTwo; }
    [Serializable] public sealed class ExperimentCandidate
    {
        public string id;
        public LinearBalancePolicy policy;
        public float armSpringScale = 1, recoveryScale = 1, effortScale = 1;
    }
    [Serializable] public sealed class ExperimentRequest
    {
        public string version = PhysicalConfig.Version;
        public int seed = 1729, scenarios = 3;
        public float seconds = 6;
        public bool holdout;
        public ExperimentCandidate[] candidates;
        public ReplayIntent[] replay;
    }
    [Serializable] public sealed class ExperimentScore
    {
        public string id;
        public double loss;
        public int falls, samples;
        public int idleFailures, contactFrames, contactTrials, contactSuccesses, separationPasses, forwardMomentumTrials;
        public float minimumPelvisHeight = 10;
        public int idleSamples;
        public float idleAngularRms, maximumIdleHeadTravel, minimumIdlePalmHeight = 10;
        public bool finite = true;
    }
    [Serializable] public sealed class ExperimentResponse
    {
        public string version = PhysicalConfig.Version, engine = "Unity PhysX", unity;
        public int seed;
        public bool holdout;
        public ExperimentScore[] scores;
    }

    // 只在显式命令行参数下运行。评分来自真正的 Physics.Simulate，不使用另一个近似模拟器。
    public sealed class PhysicalExperiment : MonoBehaviour
    {
        private IEnumerator Start()
        {
            var match = GetComponent<PhysicalMatch>(); match.Initialize(); match.ExternalSimulation = true;
            string requestPath = Argument("-experiment-request"), outputPath = Argument("-experiment-output");
            if (string.IsNullOrEmpty(requestPath) || string.IsNullOrEmpty(outputPath))
            { Debug.LogError("实验缺少请求／输出路径"); Application.Quit(2); yield break; }
            ExperimentRequest request;
            try
            {
                request = JsonUtility.FromJson<ExperimentRequest>(File.ReadAllText(requestPath));
                if (request == null || request.version != PhysicalConfig.Version || request.candidates == null || request.candidates.Length == 0)
                    throw new InvalidDataException("实验请求版本或候选列表无效");
                foreach (var candidate in request.candidates)
                    if (candidate.policy != null && !candidate.policy.Valid) throw new InvalidDataException("候选模型格式不合法");
            }
            catch (Exception e) { Debug.LogError("实验请求失败：" + e.Message); Application.Quit(2); yield break; }
            SimulationMode previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script; Application.runInBackground = true;
            var scores = new List<ExperimentScore>();
            float baseSpring = match.Config.ArmSpring, baseRecovery = match.Config.RecoveryGain, baseEffort = match.Config.EffortDegrees;
            try
            {
                foreach (var candidate in request.candidates)
                {
                    var score = new ExperimentScore { id = candidate.id };
                    double idleAngularSquared = 0;
                    match.Config.ArmSpring = baseSpring * Mathf.Clamp(candidate.armSpringScale, 0.75f, 1.25f);
                    match.Config.RecoveryGain = baseRecovery * Mathf.Clamp(candidate.recoveryScale, 0.75f, 1.25f);
                    match.Config.EffortDegrees = baseEffort * Mathf.Clamp(candidate.effortScale, 0.75f, 1.25f);
                    foreach (var fighter in match.Fighters)
                        foreach (var bone in fighter.Bones)
                            if (bone.Region == ContactRegion.Palm || bone.Region == ContactRegion.Forearm || bone.Region == ContactRegion.UpperArm)
                            { var drive = bone.Joint.slerpDrive; drive.positionSpring = match.Config.ArmSpring; bone.Joint.slerpDrive = drive; }
                    // 两个换边版本，训练集与留出集采用不同种子、扰动和动作时序。
                    for (int scenario = 0; scenario < Mathf.Clamp(request.scenarios, 1, 8); scenario++)
                        for (int swap = 0; swap < 2; swap++)
                        {
                            match.ResetRound(); match.Round.Reset(true); match.Round.Start();
                            match.Tick(default, default, false, false, false, false, 3.01f);
                            Physics.SyncTransforms();
                            var random = new System.Random(request.seed + scenario * 137 + swap * 919);
                            float release = (request.holdout ? 1.7f : 1.4f) + (float)random.NextDouble() * 0.25f;
                            int steps = Mathf.CeilToInt(Mathf.Clamp(request.seconds, 1, 20) / match.Config.FixedStep);
                            bool hadContact = false, separated = false, forwardMomentum = false;
                            var idleHead = new Vector3[2];
                            if (scenario > 0) score.contactTrials++;
                            for (int tick = 0; tick < steps; tick++)
                            {
                                float t = tick * match.Config.FixedStep;
                                PlayerIntent first = default, second = default;
                                bool w0 = false, f0 = false, w1 = false, f1 = false;
                                if (request.replay != null && request.replay.Length > 0 && scenario == request.scenarios - 1)
                                {
                                    var replay = request.replay[Mathf.Min(tick, request.replay.Length - 1)];
                                    first = replay.first; second = replay.second; w0 = replay.withdrawOne; f0 = replay.feintOne; w1 = replay.withdrawTwo; f1 = replay.feintTwo;
                                }
                                else if (scenario != 0)
                                {
                                    float press = t > 0.35f && t < release + 0.65f ? 0.85f : 0;
                                    first = new PlayerIntent { LeftForce = press, RightForce = press, Lean = new Vector2(0, press * 0.5f) };
                                    second = new PlayerIntent { LeftForce = t < release ? press * 0.7f : 0, RightForce = t < release ? press * 0.7f : 0 };
                                    w1 = t >= release && t < release + 0.08f;
                                    f0 = scenario == 2 && t > 0.15f && t < 0.23f;
                                }
                                if (swap != 0) { var temp = first; first = second; second = temp; bool wb = w0, fb = f0; w0 = w1; f0 = f1; w1 = wb; f1 = fb; }
                                if (tick == 100 && scenario > 0)
                                {
                                    Vector3 impulse = new Vector3((float)random.NextDouble() - 0.5f, 0, (float)random.NextDouble() - 0.5f) * (request.holdout ? 11 : 7);
                                    match.Fighters[swap].Pelvis.AddForce(impulse, ForceMode.Impulse);
                                }
                                for (int side = 0; side < 2; side++)
                                    match.Fighters[side].LearningCorrection = candidate.policy == null ? Vector2.zero
                                        : candidate.policy.Evaluate(LinearBalancePolicy.Observe(match.Fighters[side], side == 0 ? first : second));
                                match.Tick(first, second, w0, f0, w1, f1, match.Config.FixedStep);
                                Physics.Simulate(match.Config.FixedStep);
                                bool touching = match.Fighters[0].ContactCount > 0 || match.Fighters[1].ContactCount > 0;
                                if (touching) score.contactFrames++;
                                if (scenario > 0 && t < release) hadContact |= touching;
                                if (scenario > 0 && hadContact && t > release + 0.10f && t < release + 0.55f && !touching)
                                {
                                    separated = true;
                                    forwardMomentum |= match.Fighters[swap].CaptureBalance().Velocity.y > 0.02f;
                                }
                                for (int side = 0; side < 2; side++)
                                {
                                    var fighter = match.Fighters[side]; var state = fighter.CaptureBalance();
                                    score.finite &= state.Finite;
                                    if (!state.Finite) { score.loss += 100000; continue; }
                                    Vector2 intended = (side == 0 ? first.Lean : second.Lean) * 0.07f;
                                    score.loss += ((state.Center - intended).sqrMagnitude * 3 + state.Velocity.sqrMagnitude * 0.08f
                                        + Mathf.Max(0, 0.8f - fighter.Pelvis.position.y) * 8) * match.Config.FixedStep;
                                    score.minimumPelvisHeight = Mathf.Min(score.minimumPelvisHeight, fighter.Pelvis.position.y); score.samples++;
                                    if (scenario == 0 && t >= 3)
                                    {
                                        var pose = fighter.CapturePose();
                                        if (idleHead[side] == Vector3.zero) idleHead[side] = pose.Head;
                                        idleAngularSquared += pose.MeanAngularSpeedSquared; score.idleSamples++;
                                        score.maximumIdleHeadTravel = Mathf.Max(score.maximumIdleHeadTravel, Vector3.Distance(idleHead[side], pose.Head));
                                        score.minimumIdlePalmHeight = Mathf.Min(score.minimumIdlePalmHeight, pose.LeftPalm.y, pose.RightPalm.y);
                                        score.loss += (pose.MeanAngularSpeedSquared * 0.1f
                                            + Mathf.Max(0, 1.34f - Mathf.Min(pose.LeftPalm.y, pose.RightPalm.y)) * 10) * match.Config.FixedStep;
                                    }
                                }
                                if (match.Round.Phase == RoundPhase.Finished || !score.finite)
                                {
                                    score.falls += (match.Fighters[0].Stepping ? 1 : 0) + (match.Fighters[1].Stepping ? 1 : 0);
                                    if (scenario == 0) score.idleFailures++;
                                    score.loss += 20 + (steps - tick) * match.Config.FixedStep;
                                    break;
                                }
                                if (tick % 50 == 0) yield return null;
                            }
                            if (scenario > 0)
                            { if (hadContact) score.contactSuccesses++; if (separated) score.separationPasses++; if (forwardMomentum) score.forwardMomentumTrials++; }
                        }
                    score.idleAngularRms = score.idleSamples > 0 ? Mathf.Sqrt((float)(idleAngularSquared / score.idleSamples)) : 0;
                    scores.Add(score);
                }
            }
            finally
            {
                Physics.simulationMode = previousMode; match.Config.ArmSpring = baseSpring; match.Config.RecoveryGain = baseRecovery; match.Config.EffortDegrees = baseEffort;
            }
            var response = new ExperimentResponse { unity = Application.unityVersion, seed = request.seed, holdout = request.holdout, scores = scores.ToArray() };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
            File.WriteAllText(outputPath, JsonUtility.ToJson(response, true));
            Debug.Log("真实物理实验完成：" + outputPath); Application.Quit(0);
        }
        private static string Argument(string name)
        { var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
    }
}
