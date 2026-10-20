using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PushHands
{
    public sealed class PrototypeController : MonoBehaviour
    {
        public PrototypeConfig Config;
        public FighterView[] Views;
        public TMP_Text[] Hud;
        public MatchSimulation Simulation { get; private set; }
        public DeviceRoster Roster { get; private set; }
        public bool DebugVisible;
        private readonly PlayerIntent[] input = new PlayerIntent[2];
        private bool focused = true;
        private bool smokeMode;
        private string freezeReason;
        private readonly TMP_Text[] countdownHud = new TMP_Text[2];
        private float startAnnouncementRemaining;

        private void Awake()
        {
            Time.fixedDeltaTime = 0.02f;
            Simulation = new MatchSimulation(Config);
            Roster = new DeviceRoster();
            Roster.DeviceLost += () => Freeze("手柄已断开：重连后按菜单键继续");
            CreateCountdownHud();
            smokeMode = Debug.isDebugBuild && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-greybox-smoke") >= 0;
            if (smokeMode)
                gameObject.AddComponent<DevelopmentSmoke>();
        }
        private void OnDestroy()
        {
            Roster?.Dispose();
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        private void OnApplicationFocus(bool hasFocus)
        { if (smokeMode) return; focused = hasFocus; if (!hasFocus && Simulation != null) Freeze("窗口失去焦点：按菜单键继续"); }

        private void Update()
        {
            if (!focused) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame) DebugVisible = !DebugVisible;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Freeze("已暂停：按菜单键继续");
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame
                && Simulation.Phase != RoundPhase.Fighting && Simulation.Phase != RoundPhase.Countdown)
            {
                Roster.ToggleKeyboardDebug(); ResetRound();
                if (Roster.KeyboardDebug) Simulation.Ready();
            }
            bool joined = false;
            foreach (var pad in Gamepad.all)
                if (pad.startButton.wasPressedThisFrame) joined |= Roster.Join(pad);
            if (Roster.Complete && Simulation.Phase == RoundPhase.Waiting) Simulation.Ready();
            if (!joined && Roster.Complete && Roster.StartPressed())
            {
                if (Simulation.Phase == RoundPhase.Finished) ResetRound();
                if (Simulation.Phase == RoundPhase.Ready || Simulation.Phase == RoundPhase.Frozen)
                { ClearInput(); Simulation.Start(); }
                else if (Simulation.Phase == RoundPhase.Fighting || Simulation.Phase == RoundPhase.Countdown)
                    Freeze("已暂停：按菜单键继续");
            }
            for (int i = 0; i < 2; i++)
            {
                input[i] = Simulation.Phase == RoundPhase.Fighting ? Roster.Read(i) : default;
                float lookScale = Roster.KeyboardDebug ? Config.MouseSensitivity : Config.GamepadLookSpeed * Time.unscaledDeltaTime;
                if (Simulation.Phase == RoundPhase.Fighting) Views[i].Observe(input[i].Look * lookScale, Config);
            }
            Cursor.lockState = Roster.KeyboardDebug && Simulation.Phase == RoundPhase.Fighting ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
            UpdateHud();
            startAnnouncementRemaining = Mathf.Max(0, startAnnouncementRemaining - Time.unscaledDeltaTime);
        }

        private void FixedUpdate()
        {
            if (!focused) return;
            bool wasCountdown = Simulation.Phase == RoundPhase.Countdown;
            Simulation.Step(input[0], input[1], Time.fixedDeltaTime);
            if (wasCountdown && Simulation.Phase == RoundPhase.Fighting)
            {
                ClearInput();
                startAnnouncementRemaining = 0.6f;
            }
        }
        private void LateUpdate()
        {
            for (int i = 0; i < 2; i++) Views[i].Render(Simulation.Fighters[i], Simulation.Fighters[1 - i], Views[1 - i], Config);
        }
        public void Freeze(string reason)
        {
            Simulation.Freeze(); ClearInput(); startAnnouncementRemaining = 0; freezeReason = reason;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        public void ResetRound()
        {
            Simulation.Reset(); ClearInput(); startAnnouncementRemaining = 0;
            if (!Roster.Complete) Simulation.WaitForPlayers();
            foreach (var view in Views) view.ResetLook();
        }
        private void ClearInput() { input[0] = input[1] = default; Roster.Disarm(); }
        private void UpdateHud()
        {
            string status;
            switch (Simulation.Phase)
            {
                case RoundPhase.Waiting: status = $"等待双手柄加入（{Roster.Count}/2）\n分别按菜单键加入"; break;
                case RoundPhase.Ready: status = "已就绪 · 按菜单键开始"; break;
                case RoundPhase.Countdown: status = "准备中 · 请等待"; break;
                case RoundPhase.Fighting: status = "对抗中 · 先迈步者负"; break;
                case RoundPhase.Frozen: status = freezeReason; break;
                default:
                    status = Simulation.Result == RoundResult.Draw ? "双方同时迈步 · 平局" :
                        Simulation.Result == RoundResult.PlayerOne ? "玩家一获胜 · 玩家二迈步" : "玩家二获胜 · 玩家一迈步";
                    status += "\n按菜单键重新开始"; break;
            }
            if (Roster.KeyboardDebug) status = "键鼠开发验证 · 对手为静止测试靶\n" + status.Replace("菜单键", "Enter");
            else if (!Roster.Complete && Simulation.Phase != RoundPhase.Waiting)
                status += "\n手柄连接不完整，请重连或按菜单键补位";
            for (int i = 0; i < 2; i++)
            {
                var f = Simulation.Fighters[i];
                string device = Roster.KeyboardDebug ? (i == 0 ? "键鼠" : "测试靶") : Roster.Device(i) != null ? "手柄" + (i + 1) : "未加入";
                string controls = Roster.KeyboardDebug ? "鼠标左右键：双手  WASD：重心\n鼠标：观察  Enter：开始/暂停/重置" : "左右扳机：双手  左摇杆：重心\n右摇杆：观察  菜单键：开始/暂停/重置";
                Hud[i].text = $"玩家{(i == 0 ? "一" : "二")} · {device}\n{status}\n\n{controls}\nF1：切换开发验证  F3：调试视图";
                countdownHud[i].text = Simulation.Phase == RoundPhase.Countdown ? Simulation.CountdownNumber.ToString()
                    : Simulation.Phase == RoundPhase.Fighting && startAnnouncementRemaining > 0 ? "开始" : "";
                var panel = (RectTransform)Hud[i].transform.parent;
                panel.anchorMin = new Vector2(panel.anchorMin.x, DebugVisible ? 0.53f : 0.69f);
                if (DebugVisible)
                    Hud[i].text += $"\n\n重心 {f.Center.x:F2}, {f.Center.y:F2} 米\n趋势 {f.PredictedCenter.x:F2}, {f.PredictedCenter.y:F2}\n失衡 {f.UnstableSeconds:F2} / {Config.StepSeconds:F2} 秒\n左手 {HandName(f.Left.Phase)}  右手 {HandName(f.Right.Phase)}";
            }
        }
        private void CreateCountdownHud()
        {
            // 运行时在既有 Canvas 上创建提示，现有场景和重新生成的灰盒均自动适用。
            for (int i = 0; i < 2; i++)
            {
                var label = new GameObject("RoundCountdown", typeof(RectTransform), typeof(TextMeshProUGUI));
                label.transform.SetParent(Hud[i].canvas.transform, false);
                var rect = label.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(i * 0.5f, 0.36f);
                rect.anchorMax = new Vector2((i + 1) * 0.5f, 0.64f);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                var text = label.GetComponent<TextMeshProUGUI>();
                text.font = Hud[i].font;
                text.fontSize = 110;
                text.alignment = TextAlignmentOptions.Center;
                text.color = new Color(1, 0.9f, 0.55f);
                text.raycastTarget = false;
                text.text = "";
                countdownHud[i] = text;
            }
        }
        private static string HandName(HandPhase phase)
        {
            switch (phase)
            { case HandPhase.Contact: return "接触"; case HandPhase.Extending: return "伸出"; case HandPhase.Retracting: return "收回"; case HandPhase.Recovering: return "恢复"; default: return "待命"; }
        }
        private void OnDrawGizmos()
        {
            if (!DebugVisible || Simulation == null || Views == null) return;
            for (int i = 0; i < 2; i++)
            {
                var f = Simulation.Fighters[i]; var frame = Views[i].transform;
                Gizmos.color = f.Stepping ? Color.red : Color.green;
                Gizmos.matrix = frame.localToWorldMatrix;
                Gizmos.DrawWireCube(new Vector3(0, 0.02f, 0), new Vector3(Config.SupportHalfSize.x * 2, 0.01f, Config.SupportHalfSize.y * 2));
                Gizmos.DrawSphere(new Vector3(f.Center.x, 0.04f, f.Center.y), 0.025f);
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(new Vector3(f.Center.x, 0.04f, f.Center.y), new Vector3(f.PredictedCenter.x, 0.04f, f.PredictedCenter.y));
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
