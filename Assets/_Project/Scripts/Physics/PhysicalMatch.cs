using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PushHands.Physical
{
    [DefaultExecutionOrder(-50)]
    public sealed class PhysicalMatch : MonoBehaviour
    {
        public PhysicalConfig Config;
        public PhysicalFighter[] Fighters;
        public TMP_Text[] Hud, CountdownHud;
        public PhysicalRecorder Recorder;
        public TextAsset ValidatedPolicy;
        public PhysicalRound Round { get; private set; } = new PhysicalRound();
        public PhysicalInput Input { get; private set; }
        public bool ExternalSimulation;
        public bool DebugVisible;
        public bool DebugOpponentPush { get; private set; }
        public float FightSeconds { get; private set; }
        private bool focused = true;
        private bool initialized;
        private string freezeReason = "已暂停：按菜单键继续";
        private float startAnnouncement;
        private LinearBalancePolicy policy;

        private void Start() => Initialize();
        public void Initialize()
        {
            if (initialized) return;
            if (Config == null) throw new System.InvalidOperationException("物理原型缺少参数资源，请运行推手菜单生成物理原型。");
            if (Fighters == null || Fighters.Length != 2)
                Fighters = new[] { PhysicalFighter.Create(0, Config, null), PhysicalFighter.Create(1, Config, null) };
            PhysicalFighter.ConfigureInteraction(Fighters[0], Fighters[1]);
            foreach (var fighter in Fighters) fighter.ResetPose();
            Input = new PhysicalInput(); Input.Roster.DeviceLost += () => Freeze("手柄断开：补位后按菜单键继续");
            Time.fixedDeltaTime = Config.FixedStep; initialized = true;
            if (Recorder == null) Recorder = GetComponent<PhysicalRecorder>();
            if (ValidatedPolicy != null)
            {
                policy = LinearBalancePolicy.LoadValidated(ValidatedPolicy.text);
                if (policy == null)
                { policy = null; Debug.LogWarning("学习模型未通过版本／验证检查，保留基础控制器。"); }
            }
            var args = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(args, "-physical-experiment") >= 0 && GetComponent<PhysicalExperiment>() == null)
                gameObject.AddComponent<PhysicalExperiment>();
        }
        private void OnDestroy() { Input?.Dispose(); Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        private void OnApplicationFocus(bool value)
        {
            if (ExternalSimulation) return;
            focused = value; if (!value && initialized) Freeze("失去焦点：按菜单键继续");
        }
        private void Update()
        {
            if (!initialized || ExternalSimulation || !focused) return;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame) Freeze("已暂停：按菜单键继续");
                if (keyboard.f3Key.wasPressedThisFrame) DebugVisible = !DebugVisible;
                if (keyboard.f2Key.wasPressedThisFrame && Input.Roster.KeyboardDebug) DebugOpponentPush = !DebugOpponentPush;
                if (keyboard.f1Key.wasPressedThisFrame && Round.Phase != RoundPhase.Fighting && Round.Phase != RoundPhase.Countdown)
                { Input.Roster.ToggleKeyboardDebug(); ResetRound(); }
                if (keyboard.f4Key.wasPressedThisFrame && Recorder != null)
                {
                    if (Recorder.Recording) Recorder.Stop(); else Recorder.Begin(this, "manual");
                }
            }
            bool joined = false;
            foreach (var pad in Gamepad.all) if (pad.startButton.wasPressedThisFrame) joined |= Input.Roster.Join(pad);
            if (Input.Roster.Complete) Round.Ready();
            if (!joined && Input.Roster.Complete && Input.Roster.StartPressed())
            {
                if (Round.Phase == RoundPhase.Finished) ResetRound();
                if (Round.Phase == RoundPhase.Ready || Round.Phase == RoundPhase.Frozen)
                { Input.Disarm(); Round.Start(); }
                else Freeze("已暂停：按菜单键继续");
            }
            if (Round.Phase == RoundPhase.Fighting)
                for (int i = 0; i < 2; i++)
                {
                    Input.Sample(i);
                    float scale = Input.Roster.KeyboardDebug ? Config.MouseSensitivity : Config.PadLookSpeed * Time.unscaledDeltaTime;
                    Fighters[i].Observe(Input.Intents[i].Look * scale);
                }
            Cursor.lockState = Input.Roster.KeyboardDebug && Round.Phase == RoundPhase.Fighting ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
        }
        private void FixedUpdate()
        {
            if (!initialized || ExternalSimulation || !focused) return;
            Input.ConsumeTechniques(0, out bool w0, out bool f0); Input.ConsumeTechniques(1, out bool w1, out bool f1);
            Tick(Input.Intents[0], Input.Intents[1], w0, f0, w1, f1, Time.fixedDeltaTime);
        }
        public void Tick(PlayerIntent a, PlayerIntent b, bool w0, bool f0, bool w1, bool f1, float dt)
        {
            if (dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            bool countdown = Round.Phase == RoundPhase.Countdown;
            Round.Advance(dt);
            if (countdown)
            {
                if (Round.Phase == RoundPhase.Fighting)
                {
                    Input.Disarm(); startAnnouncement = 0.6f;
                    foreach (var fighter in Fighters) fighter.SetFrozen(false);
                }
                return;
            }
            if (Round.Phase == RoundPhase.Finished)
            { foreach (var fighter in Fighters) fighter.PresentStep(dt); return; }
            if (Round.Phase != RoundPhase.Fighting) return;
            // 单人开发靶显式维持轻推，便于测试接触；正常双人松键仍完整收手。
            if (Input.Roster.KeyboardDebug && !ExternalSimulation)
                b = DebugOpponentPush ? new PlayerIntent { LeftForce = 0.6f, RightForce = 0.6f } : default;
            foreach (var fighter in Fighters)
                if (!fighter.CaptureBalance().Finite) { Freeze("物理状态异常：请重开并检查 Console"); Debug.LogError("物理人偶出现非有限值"); return; }
            // 先读取同一个已完成物理步的双方平衡，再统一裁决，避免玩家遍历顺序决定输赢。
            bool one = Fighters[0].AssessStep(dt), two = Fighters[1].AssessStep(dt);
            Round.Resolve(one, two);
            if (Round.Phase == RoundPhase.Finished)
            { Recorder?.Capture(this, a, b); return; }
            if (policy != null && !ExternalSimulation)
            {
                Fighters[0].LearningCorrection = policy.Evaluate(LinearBalancePolicy.Observe(Fighters[0], a));
                Fighters[1].LearningCorrection = policy.Evaluate(LinearBalancePolicy.Observe(Fighters[1], b));
            }
            Fighters[0].Drive(a, w0, f0, Fighters[1], dt); Fighters[1].Drive(b, w1, f1, Fighters[0], dt);
            FightSeconds += dt; Recorder?.Capture(this, a, b);
        }
        public void Freeze(string reason)
        {
            if (!initialized) return;
            // 已结束的保护步也应能冻结；不改变已经裁决的结果。
            Round.Freeze(); freezeReason = reason; Input.Disarm(); startAnnouncement = 0;
            foreach (var fighter in Fighters) fighter.SetFrozen(true);
        }
        public void ResetRound()
        {
            if (!initialized) return;
            foreach (var fighter in Fighters) fighter.ResetPose();
            Round.Reset(Input.Roster.Complete); Input.Disarm(); FightSeconds = 0; startAnnouncement = 0;
            Recorder?.MarkReset(this);
        }
        private void LateUpdate()
        {
            if (!initialized) return;
            foreach (var fighter in Fighters) fighter.RenderView(Time.unscaledDeltaTime);
            startAnnouncement = Mathf.Max(0, startAnnouncement - Time.unscaledDeltaTime);
            if (Hud == null || CountdownHud == null) return;
            string status;
            switch (Round.Phase)
            {
                case RoundPhase.Waiting: status = $"分别按菜单键加入 · {Input.Roster.Count}/2"; break;
                case RoundPhase.Ready: status = "已就绪 · 再按菜单键开始"; break;
                case RoundPhase.Countdown: status = "准备中 · 请松开操控"; break;
                case RoundPhase.Frozen: status = freezeReason; break;
                case RoundPhase.Fighting: status = "对抗中 · 先迈步者负"; break;
                default: status = Round.Result == RoundResult.Draw ? "双方迈步 · 平局" : Round.Result == RoundResult.PlayerOne ? "玩家一获胜" : "玩家二获胜"; status += " · 菜单键重开"; break;
            }
            if (Input.Roster.KeyboardDebug) status = "键鼠验证 · 对手" + (DebugOpponentPush ? "自动轻推" : "准备") + "\nF2：自动轻推\n" + status.Replace("菜单键", "Enter");
            for (int i = 0; i < 2; i++)
            {
                bool keyboard = Input.Roster.KeyboardDebug;
                var balance = Fighters[i].CaptureBalance();
                Hud[i].text = $"玩家{(i == 0 ? "一" : "二")}\n{status}\n"
                    + (keyboard ? "鼠标左右键：双手  WASD：重心\nQ：收力后撤  E：假推\n" : "左右扳机：双手  左摇杆：重心\nLB：收力后撤  RB：假推\n")
                    + "F1：键鼠验证  F3：调试  F4：本地记录";
                if (DebugVisible) Hud[i].text += $"\n重心 {balance.Center.x:F2}, {balance.Center.y:F2}\n趋势 {balance.Predicted.x:F2}, {balance.Predicted.y:F2}\n支撑 {balance.RequiredSupport.x:F2}, {balance.RequiredSupport.y:F2}\n推力 {Fighters[i].ActivePushForce.magnitude:F0}N\n合法接触 {Fighters[i].ContactCount} · 技巧 {Fighters[i].Technique.Active}";
                CountdownHud[i].text = Round.Phase == RoundPhase.Countdown ? Round.CountdownNumber.ToString()
                    : Round.Phase == RoundPhase.Fighting && startAnnouncement > 0 ? "开始" : "";
            }
        }
        private void OnDrawGizmos()
        {
            if (!DebugVisible || Fighters == null) return;
            foreach (var fighter in Fighters)
            {
                if (fighter == null || fighter.Bones == null) continue;
                var balance = fighter.CaptureBalance(); Gizmos.color = fighter.Stepping ? Color.red : Color.green;
                Gizmos.matrix = fighter.transform.localToWorldMatrix;
                Vector2 center = (fighter.SupportMinimum + fighter.SupportMaximum) * 0.5f;
                Vector2 size = fighter.SupportMaximum - fighter.SupportMinimum;
                Gizmos.DrawWireCube(new Vector3(center.x, 0.02f, center.y), new Vector3(size.x, 0.01f, size.y));
                Gizmos.DrawSphere(new Vector3(balance.Center.x, 0.03f, balance.Center.y), 0.02f);
                Gizmos.color = Color.yellow; Gizmos.DrawLine(new Vector3(balance.Center.x, 0.03f, balance.Center.y), new Vector3(balance.Predicted.x, 0.03f, balance.Predicted.y));
                Gizmos.color = new Color(1, 0.45f, 0); Gizmos.DrawSphere(new Vector3(balance.RequiredSupport.x, 0.035f, balance.RequiredSupport.y), 0.018f);
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
