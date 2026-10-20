using UnityEngine;

namespace PushHands
{
    [System.Serializable]
    public struct PlayerIntent
    {
        public float LeftForce, RightForce;
        public Vector2 Lean, Look;
    }

    public enum HandPhase { Rest, Extending, Contact, Retracting, Recovering }
    public enum RoundPhase { Waiting, Ready, Fighting, Frozen, Finished, Countdown }
    public enum RoundResult { None, PlayerOne, PlayerTwo, Draw }

    public sealed class HandState
    {
        public HandPhase Phase;
        public float Extension, Strength, RecoveryRemaining;
        public bool Contact;
    }

    public sealed class FighterState
    {
        // 坐标以各自站姿为准：x 为右，y 为朝向对手的前方。
        public Vector2 Center, Velocity, PredictedCenter;
        public float UnstableSeconds, StepProgress;
        public bool Stepping;
        public Vector2 StepDirection;
        public readonly HandState Left = new HandState();
        public readonly HandState Right = new HandState();
    }

    public sealed class MatchSimulation
    {
        public readonly FighterState[] Fighters = { new FighterState(), new FighterState() };
        public RoundPhase Phase { get; private set; } = RoundPhase.Waiting;
        public RoundResult Result { get; private set; }
        public int ResolutionCount { get; private set; }
        public int Tick { get; private set; }
        private double countdownRemaining;
        public int CountdownNumber => Phase == RoundPhase.Countdown
            ? Mathf.CeilToInt((float)System.Math.Max(0, countdownRemaining - 0.000001)) : 0;
        private readonly PrototypeConfig config;

        public MatchSimulation(PrototypeConfig config) { this.config = config; }
        public void Ready() { if (Phase == RoundPhase.Waiting) Phase = RoundPhase.Ready; }
        public void WaitForPlayers() { if (Phase == RoundPhase.Ready) Phase = RoundPhase.Waiting; }
        public void Start()
        {
            if (Phase != RoundPhase.Ready && Phase != RoundPhase.Frozen) return;
            countdownRemaining = 3;
            Phase = RoundPhase.Countdown;
        }
        public void Freeze()
        {
            if (Phase != RoundPhase.Fighting && Phase != RoundPhase.Countdown) return;
            countdownRemaining = 0;
            Phase = RoundPhase.Frozen;
        }
        public void Reset()
        {
            Fighters[0] = new FighterState(); Fighters[1] = new FighterState();
            Result = RoundResult.None; ResolutionCount = 0; Tick = 0; countdownRemaining = 0; Phase = RoundPhase.Ready;
        }

        public void Step(PlayerIntent first, PlayerIntent second, float dt)
        {
            if (dt <= 0 || Phase == RoundPhase.Frozen) return;
            if (Phase == RoundPhase.Countdown)
            {
                countdownRemaining -= dt;
                if (countdownRemaining <= 0.000001)
                { countdownRemaining = 0; Phase = RoundPhase.Fighting; }
                // 倒计时结束这一模拟步不消费预先输入，也不补算对抗时间。
                return;
            }
            if (Phase == RoundPhase.Finished)
            {
                foreach (var f in Fighters)
                {
                    if (f.Stepping) f.StepProgress = Mathf.Min(1, f.StepProgress + dt / config.StepAnimationSeconds);
                    UpdateHand(f.Left, 0, 0, false, dt);
                    UpdateHand(f.Right, 0, 0, false, dt);
                }
                return;
            }
            if (Phase != RoundPhase.Fighting) return;
            Tick++;
            first = Sanitize(first); second = Sanitize(second);
            UpdateHands(Fighters[0], Fighters[1], first, dt);
            UpdateHands(Fighters[1], Fighters[0], second, dt);
            // 两人状态均取本步开始时的快照；先算接触再积分，避免玩家遍历顺序影响裁决。
            float forceOne = Force(Fighters[0]);
            float forceTwo = Force(Fighters[1]);
            float compression = forceOne + forceTwo;
            Integrate(Fighters[0], first, compression, dt);
            Integrate(Fighters[1], second, compression, dt);
            bool one = Fighters[0].Stepping, two = Fighters[1].Stepping;
            if (one || two)
            {
                Result = one && two ? RoundResult.Draw : one ? RoundResult.PlayerTwo : RoundResult.PlayerOne;
                ResolutionCount++; Phase = RoundPhase.Finished;
                ClearContact(Fighters[0]); ClearContact(Fighters[1]);
            }
        }

        private static PlayerIntent Sanitize(PlayerIntent intent)
        {
            intent.LeftForce = Mathf.Clamp01(intent.LeftForce);
            intent.RightForce = Mathf.Clamp01(intent.RightForce);
            intent.Lean = Vector2.ClampMagnitude(intent.Lean, 1);
            return intent;
        }

        private void UpdateHands(FighterState self, FighterState other, PlayerIntent input, float dt)
        {
            float forwardDistance = config.PlayerSpacing - config.TorsoRadius - self.Center.y - other.Center.y;
            float sidewaysDistance = Mathf.Abs(self.Center.x + other.Center.x);
            bool aligned = forwardDistance > 0 && sidewaysDistance < config.ContactHalfWidth;
            UpdateHand(self.Left, input.LeftForce, forwardDistance, aligned, dt);
            UpdateHand(self.Right, input.RightForce, forwardDistance, aligned, dt);
        }

        private void UpdateHand(HandState hand, float input, float distance, bool aligned, float dt)
        {
            hand.Contact = false;
            if (input <= 0.03f && (hand.Phase == HandPhase.Contact || hand.Phase == HandPhase.Extending))
                hand.Phase = HandPhase.Retracting;
            if (hand.Phase == HandPhase.Retracting)
            {
                hand.Strength = 0;
                hand.Extension = Mathf.MoveTowards(hand.Extension, 0, dt / config.RetractSeconds);
                if (hand.Extension <= 0) { hand.Phase = HandPhase.Recovering; hand.RecoveryRemaining = config.RecoverSeconds; }
                return;
            }
            if (hand.Phase == HandPhase.Recovering)
            {
                hand.RecoveryRemaining = Mathf.Max(0, hand.RecoveryRemaining - dt);
                if (hand.RecoveryRemaining <= 0) hand.Phase = HandPhase.Rest;
                return;
            }
            if (input <= 0.03f) { hand.Strength = 0; return; }
            hand.Extension = Mathf.MoveTowards(hand.Extension, 1, dt / config.ExtendSeconds);
            hand.Strength = Mathf.MoveTowards(hand.Strength, input, dt * config.ForceRisePerSecond);
            hand.Contact = aligned && distance <= config.Reach * hand.Extension;
            if (hand.Contact) hand.Extension = Mathf.Clamp01(distance / config.Reach);
            hand.Phase = hand.Contact ? HandPhase.Contact : HandPhase.Extending;
        }

        private float Force(FighterState fighter)
        {
            float amount = (fighter.Left.Contact ? fighter.Left.Strength : 0) + (fighter.Right.Contact ? fighter.Right.Strength : 0);
            return Mathf.Min(config.SharedForce, amount * config.HandForce);
        }

        private void Integrate(FighterState fighter, PlayerIntent input, float compression, float dt)
        {
            float effort = Mathf.Max(fighter.Left.Strength, fighter.Right.Strength)
                + 0.5f * Mathf.Min(fighter.Left.Strength, fighter.Right.Strength);
            Vector2 target = input.Lean * config.LeanRange + Vector2.up * effort * config.EffortLean;
            Vector2 recovery = Vector2.ClampMagnitude((target - fighter.Center) * config.RecoveryStiffness
                - fighter.Velocity * config.RecoveryDamping, config.MaxRecoveryAcceleration);
            // 双方前向相反；同一接触力在各自局部坐标内均指向后方，世界坐标严格作用/反作用。
            Vector2 acceleration = fighter.Center * (config.Gravity / config.CenterHeight) + recovery
                - Vector2.up * (compression / config.Mass);
            fighter.Velocity += acceleration * dt;
            fighter.Center += fighter.Velocity * dt;
            fighter.PredictedCenter = fighter.Center + fighter.Velocity / Mathf.Sqrt(config.Gravity / config.CenterHeight);
            Vector2 risk = fighter.PredictedCenter;
            bool outside = Mathf.Abs(risk.x) > config.SupportHalfSize.x || Mathf.Abs(risk.y) > config.SupportHalfSize.y;
            fighter.UnstableSeconds = outside ? fighter.UnstableSeconds + dt : 0;
            if (fighter.UnstableSeconds + 0.00001f >= config.StepSeconds)
            {
                fighter.Stepping = true;
                fighter.StepDirection = risk.sqrMagnitude > 0.0001f ? risk.normalized : Vector2.down;
            }
        }

        private static void ClearContact(FighterState f)
        { f.Left.Contact = f.Right.Contact = false; f.Left.Strength = f.Right.Strength = 0; }
    }
}
