using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace PushHands
{
    // 仅开发包显式传入参数时运行；用虚拟设备检查完整输入到裁决链路，不替代实机验收。
    public sealed class DevelopmentSmoke : MonoBehaviour
    {
        private PrototypeController controller;
        private Gamepad first, second;
        private float started;
        private string output;
        private bool contactCapture, resultCapture;
        private int errors;
        private int lastDiagnostic = -1;
        private bool initialized;
        private int stage;
        private float stageStarted;
        private int renderedFrames;

        private void Start()
        {
            controller = GetComponent<PrototypeController>();
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-smokeOutput");
            output = index >= 0 && index + 1 < args.Length ? args[index + 1] : "Smoke";
            Directory.CreateDirectory(output);
            first = InputSystem.AddDevice<Gamepad>(); second = InputSystem.AddDevice<Gamepad>();
            started = Time.unscaledTime;
            int fpsIndex = Array.IndexOf(args, "-smokeFPS");
            if (fpsIndex >= 0 && fpsIndex + 1 < args.Length && int.TryParse(args[fpsIndex + 1], out int fps))
            { QualitySettings.vSyncCount = 0; Application.targetFrameRate = Mathf.Clamp(fps, 15, 240); }
            Application.logMessageReceived += CountErrors;
        }
        private void CountErrors(string message, string trace, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++; }
        private void Update()
        {
            // 首帧可能因着色器初始化停顿数秒，按实际状态推进，不能错过加入/开始的时间窗口。
            if (!initialized) { initialized = true; started = stageStarted = Time.unscaledTime; }
            renderedFrames++;
            float elapsed = Time.unscaledTime - started;
            int secondMark = Mathf.FloorToInt(elapsed);
            if (secondMark != lastDiagnostic)
            {
                lastDiagnostic = secondMark;
                Debug.Log($"自动试玩 {elapsed:F1}s：阶段={controller.Simulation.Phase}，设备={controller.Roster.Count}，已添加={first.added}/{second.added}，启用={first.enabled}/{second.enabled}，菜单={first.startButton.ReadValue()}，后台={Application.isFocused}，输入更新={InputSystem.settings.updateMode}");
            }
            var a = new GamepadState(); var b = new GamepadState();
            if (stage == 0)
            {
                a = a.WithButton(GamepadButton.Start); b = b.WithButton(GamepadButton.Start);
                if (controller.Roster.Complete) { stage = 1; stageStarted = Time.unscaledTime; }
            }
            else if (stage == 1 && Time.unscaledTime - stageStarted > 0.25f) stage = 2;
            else if (stage == 2)
            {
                a = a.WithButton(GamepadButton.Start);
                if (controller.Simulation.Phase == RoundPhase.Fighting) { stage = 3; stageStarted = Time.unscaledTime; }
            }
            else if (stage == 3)
            {
                if (Time.unscaledTime - stageStarted > 0.25f) { a.leftTrigger = 1; a.rightTrigger = 1; }
                if (controller.Simulation.Result != RoundResult.None) { stage = 4; stageStarted = Time.unscaledTime; }
            }
            InputSystem.QueueStateEvent(first, a); InputSystem.QueueStateEvent(second, b);
            if (!contactCapture && controller.Simulation.Fighters[0].Left.Contact)
            { ScreenCapture.CaptureScreenshot(Path.Combine(output, "contact.png")); contactCapture = true; }
            if (!resultCapture && stage == 4 && Time.unscaledTime - stageStarted > 0.4f)
            { ScreenCapture.CaptureScreenshot(Path.Combine(output, "result.png")); resultCapture = true; }
            if ((stage == 4 && Time.unscaledTime - stageStarted > 0.8f) || elapsed > 12)
            {
                bool pass = controller.Simulation.Result != RoundResult.None && controller.Simulation.ResolutionCount == 1
                    && controller.Roster.Complete && controller.Simulation.Tick > 0 && errors == 0;
                File.WriteAllText(Path.Combine(output, "smoke.txt"), $"通过={pass}\n结果={controller.Simulation.Result}\n模拟步={controller.Simulation.Tick}\n裁决次数={controller.Simulation.ResolutionCount}\n运行错误={errors}\n目标帧率={Application.targetFrameRate}\n实测平均帧率={renderedFrames / Mathf.Max(elapsed, 0.01f):F1}");
                Application.Quit(pass ? 0 : 1); enabled = false;
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
