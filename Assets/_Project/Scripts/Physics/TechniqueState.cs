namespace PushHands.Physical
{
    public enum Technique { None, Withdraw, Feint }
    public enum ContactRegion { Palm, Forearm, Torso, Head, UpperArm, Leg }

    public static class ContactRules
    {
        public static bool IsLegal(ContactRegion first, ContactRegion second) =>
            (first == ContactRegion.Palm || first == ContactRegion.Forearm)
            && (second == ContactRegion.Palm || second == ContactRegion.Forearm);
    }

    public sealed class TechniqueState
    {
        public Technique Active { get; private set; }
        public float Elapsed { get; private set; }
        public int TriggerCount { get; private set; }
        public const float WithdrawSeconds = 0.38f, FeintSeconds = 0.26f, RecoverySeconds = 0.42f;
        private bool lastWithdraw, lastFeint;
        private float actionSeconds;
        public float WithdrawWeight => Active == Technique.Withdraw ? Envelope(Elapsed, actionSeconds) : 0;
        public float FeintWeight => Active == Technique.Feint ? Envelope(Elapsed, actionSeconds) : 0;
        public bool Recovering => Active != Technique.None && Elapsed >= actionSeconds;
        public void Tick(bool withdraw, bool feint, float dt)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0) return;
            bool withdrawEdge = withdraw && !lastWithdraw, feintEdge = feint && !lastFeint;
            lastWithdraw = withdraw; lastFeint = feint;
            if (Active == Technique.None && (withdrawEdge || feintEdge))
            {
                Active = withdrawEdge ? Technique.Withdraw : Technique.Feint;
                actionSeconds = Active == Technique.Withdraw ? WithdrawSeconds : FeintSeconds;
                Elapsed = 0; TriggerCount++;
            }
            if (Active == Technique.None) return;
            Elapsed += dt;
            if (Elapsed >= actionSeconds + RecoverySeconds) { Active = Technique.None; Elapsed = 0; }
        }
        private static float Envelope(float time, float duration)
        {
            if (time >= duration) return 0;
            float t = time / duration;
            // 快速卸力，短暂保持后逐渐返回，避免一帧跳变。
            return t < 0.25f ? t / 0.25f : t < 0.65f ? 1 : (1 - t) / 0.35f;
        }
        public void Reset()
        { Active = Technique.None; Elapsed = 0; TriggerCount = 0; lastWithdraw = lastFeint = false; }
    }
}
