#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace PushHands.Physical
{
    // 开发包显式参数触发的端到端验收，与现有灰盒 DevelopmentSmoke 保持相同做法。
    public sealed class PhysicalFeedbackSmoke : MonoBehaviour
    {
        private PhysicalMatch match;
        private Gamepad first, second;
        private string output;
        private string scenario;
        private int loserIndex = 1;
        private RoundResult expectedResult = RoundResult.PlayerOne;
        private float started, resultAt, settledAt, maxPressure, maximumRms, stepDistance;
        private readonly float[] audioSamples = new float[256];
        private int errors, frames, countdownMask;
        private bool sawResult, earlyMenuSent, settled, restartSent, sawLanding, sawFall, resultStable = true;
        private bool sawLowerGround, sawPresentationTimeout;
        private int lastSecond = -1;
        private void Start()
        {
            match = GetComponent<PhysicalMatch>();
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-feedback-output");
            output = index >= 0 && index + 1 < args.Length ? args[index + 1] : "Artifacts/Verification/FeedbackSmoke";
            int scenarioIndex = Array.IndexOf(args, "-feedback-scenario");
            scenario = scenarioIndex >= 0 && scenarioIndex + 1 < args.Length ? args[scenarioIndex + 1] : "push-retreat";
            if (scenario == "backward" || scenario == "forward")
            { loserIndex = 0; expectedResult = RoundResult.PlayerTwo; }
            else if (scenario != "push-idle" && scenario != "push-retreat")
                throw new ArgumentException("未知的反馈验收情景：" + scenario);
            Directory.CreateDirectory(output); Application.runInBackground = true;
            // 隐藏窗口验收不能让 Input System 禁用虚拟设备；仅作用于显式开发测试进程。
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;
            first = InputSystem.AddDevice<Gamepad>(); second = InputSystem.AddDevice<Gamepad>();
            match.Input.Roster.Join(first); match.Input.Roster.Join(second); match.Round.Ready(); match.Round.Start();
            started = Time.unscaledTime; Application.logMessageReceived += CountErrors;
        }
        private void CountErrors(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++; }
        private void Update()
        {
            frames++; float now = Time.unscaledTime;
            if (Mathf.FloorToInt(now - started) != lastSecond)
            {
                lastSecond = Mathf.FloorToInt(now - started);
                Debug.Log($"反馈自动验证：秒={lastSecond} 阶段={match.Round.Phase} 对抗={match.FightSeconds:F2} 展示={match.ResultSeconds:F2} 设备={match.Input.Roster.Count} 焦点={Application.isFocused}");
            }
            var one = new GamepadState(); var two = new GamepadState();
            if (match.Round.Phase == RoundPhase.Countdown && !restartSent)
                countdownMask |= 1 << Mathf.Clamp(match.Round.CountdownNumber - 1, 0, 2);
            // 与物理专项使用相同的三秒自然站立基线，独立验收前倾、后仰及静止防守。
            if (match.Round.Phase == RoundPhase.Fighting && match.FightSeconds > 3f)
            {
                if (scenario == "backward") one.leftStick = Vector2.down;
                else if (scenario == "forward") one.leftStick = Vector2.up;
                else
                {
                    one.leftTrigger = one.rightTrigger = 1; one.leftStick = Vector2.up * 0.8f;
                    if (scenario == "push-retreat") two.leftStick = Vector2.down * 0.75f;
                }
            }
            maxPressure = Mathf.Max(maxPressure, match.Feedback.Hand(0, 0).Pressure, match.Feedback.Hand(0, 1).Pressure);
            AudioListener.GetOutputData(audioSamples, 0); float sum = 0;
            foreach (float value in audioSamples) sum += value * value;
            maximumRms = Mathf.Max(maximumRms, Mathf.Sqrt(sum / audioSamples.Length));
            if (match.Round.Phase == RoundPhase.Finished)
            {
                if (!sawResult) { sawResult = true; resultAt = now; CaptureViews("defeat-start.png"); }
                var loser = match.Fighters[loserIndex];
                resultStable &= match.Round.Result == expectedResult && match.Round.Resolutions == 1;
                sawPresentationTimeout |= loser.PresentationTimedOut;
                if (loser.Falling)
                    foreach (var bone in loser.Bones)
                        if ((bone.Region == ContactRegion.Torso || bone.Region == ContactRegion.Head)
                            && bone.Body.GetComponent<PhysicalContact>().TouchesEnvironment(match.Arena.LandingGround)) sawLowerGround = true;
                one.leftTrigger = one.rightTrigger = 1; one.leftStick = Vector2.up;
                if (!earlyMenuSent && now - resultAt > 0.4f)
                { one = one.WithButton(GamepadButton.Start); earlyMenuSent = true; }
                if (loser.Falling && !sawFall)
                { sawFall = true; CaptureViews("defeat-falling.png"); }
                if (loser.GroundLanded && !sawLanding)
                { sawLanding = true; CaptureViews("defeat-landed.png"); }
                stepDistance = Mathf.Max(stepDistance, loser.CaptureBalance().FootDisplacement);
                if (match.CanRestart && !settled)
                { settled = true; settledAt = now; CaptureViews("defeat-settled.png"); }
                if (settled && now - settledAt > 0.3f && !restartSent)
                { one = one.WithButton(GamepadButton.Start); restartSent = true; }
            }
            if (sawResult && !settled && match.Round.Phase != RoundPhase.Finished) resultStable = false;
            InputSystem.QueueStateEvent(first, one); InputSystem.QueueStateEvent(second, two);
            if ((restartSent && match.Round.Phase == RoundPhase.Countdown) || now - started > 20)
            {
                bool pressureValid = scenario == "forward" || scenario == "backward" || maxPressure > 0.05f;
                bool pass = countdownMask == 7 && pressureValid && sawFall && sawLanding && settled && resultStable
                    && !sawPresentationTimeout
                    && settledAt - resultAt >= 2.95f && stepDistance > 0.12f
                    && restartSent && match.Round.Phase == RoundPhase.Countdown && errors == 0;
                File.WriteAllText(Path.Combine(output, "smoke.txt"), $"通过={pass}\n情景={scenario}\n帧数={frames}\n倒计时完整={countdownMask == 7}\n实际压力反馈={maxPressure:F3}\n音频输出峰值RMS={maximumRms:F5}\n实际失足={sawFall}\n实际摔落接地={sawLanding}\n实际下方地面接触={sawLowerGround}\n动作超时={sawPresentationTimeout}\n足部位移={stepDistance:F3}\n展示时长={settledAt - resultAt:F3}\n提前重开被阻止={resultStable}\n重开进入倒计时={restartSent && match.Round.Phase == RoundPhase.Countdown}\n运行错误={errors}");
                Application.Quit(pass ? 0 : 1); enabled = false;
            }
        }
        private void CaptureViews(string name)
        {
            // 隐藏窗口的屏幕捕获会是黑帧；直接渲染实际双视口，不包含 ScreenSpaceOverlay HUD。
            var target = new RenderTexture(1600, 900, 24); var active = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                foreach (var fighter in match.Fighters)
                {
                    var camera = fighter.ViewCamera; var previous = camera.targetTexture;
                    try { camera.targetTexture = target; camera.Render(); }
                    finally { camera.targetTexture = previous; }
                }
                RenderTexture.active = target; texture = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); texture.Apply();
                File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = active;
                if (texture != null) Destroy(texture); target.Release(); Destroy(target);
            }
        }
        private void OnDestroy()
        {
            Application.logMessageReceived -= CountErrors;
            if (first != null && first.added) InputSystem.RemoveDevice(first);
            if (second != null && second.added) InputSystem.RemoveDevice(second);
        }
    }
}
#endif
