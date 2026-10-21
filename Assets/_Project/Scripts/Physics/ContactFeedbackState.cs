using UnityEngine;

namespace PushHands.Physical
{
    // 只解释实际接触，不修改身体、推力或胜负。每只手独立保存防抖状态。
    public sealed class ContactFeedbackState
    {
        public float Pressure { get; private set; }
        public float Impact { get; private set; }
        public bool ContactPulse { get; private set; }
        public int PulseCount { get; private set; }
        private float gap = 1, cooldown, lastContactLoad;
        public void Tick(bool touching, float newtons, float dt, PhysicalConfig config)
        {
            ContactPulse = false;
            if (dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            float load = touching && !float.IsNaN(newtons) && !float.IsInfinity(newtons)
                ? Mathf.Clamp01(newtons / Mathf.Max(1, config.FeedbackForceScale)) : 0;
            cooldown = Mathf.Max(0, cooldown - dt);
            Impact *= Mathf.Exp(-18 * dt);
            if (touching)
            {
                bool arrival = gap >= 0.08f;
                // 压力表现可在断触时衰减，但加力基准必须保留短断触前的真实载荷。
                float increase = load - lastContactLoad;
                if (cooldown <= 0 && (arrival || increase > 0.35f))
                {
                    ContactPulse = true; PulseCount++;
                    Impact = Mathf.Clamp01(0.2f + load * 0.8f);
                    cooldown = Mathf.Max(0.08f, config.FeedbackCooldown);
                }
                gap = 0;
                lastContactLoad = load;
            }
            else { gap += dt; if (gap >= 0.08f) lastContactLoad = 0; }
            Pressure = Mathf.Lerp(Pressure, load, 1 - Mathf.Exp(-Mathf.Max(1, config.FeedbackResponse) * dt));
        }
        public void Clear()
        { Pressure = Impact = cooldown = lastContactLoad = 0; gap = 1; ContactPulse = false; PulseCount = 0; }
    }
}
