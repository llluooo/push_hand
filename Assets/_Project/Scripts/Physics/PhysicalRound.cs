namespace PushHands.Physical
{
    public sealed class PhysicalRound
    {
        public RoundPhase Phase { get; private set; } = RoundPhase.Waiting;
        public RoundResult Result { get; private set; }
        public int Resolutions { get; private set; }
        private double countdown;
        public int CountdownNumber => Phase == RoundPhase.Countdown ? (int)System.Math.Ceiling(System.Math.Max(0, countdown - 0.000001)) : 0;
        public void Ready() { if (Phase == RoundPhase.Waiting) Phase = RoundPhase.Ready; }
        public void Start()
        { if (Phase != RoundPhase.Ready && Phase != RoundPhase.Frozen) return; countdown = 3; Phase = RoundPhase.Countdown; }
        public void Advance(float dt)
        {
            if (Phase != RoundPhase.Countdown || dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            countdown -= dt;
            if (countdown <= 0.000001) { countdown = 0; Phase = RoundPhase.Fighting; }
        }
        public void Freeze()
        { if (Phase == RoundPhase.Fighting || Phase == RoundPhase.Countdown) { countdown = 0; Phase = RoundPhase.Frozen; } }
        public void Reset(bool complete)
        { Result = RoundResult.None; Resolutions = 0; countdown = 0; Phase = complete ? RoundPhase.Ready : RoundPhase.Waiting; }
        public void Resolve(bool one, bool two)
        {
            if (Phase != RoundPhase.Fighting || (!one && !two)) return;
            Result = one && two ? RoundResult.Draw : one ? RoundResult.PlayerTwo : RoundResult.PlayerOne;
            Resolutions++; Phase = RoundPhase.Finished;
        }
    }
}
