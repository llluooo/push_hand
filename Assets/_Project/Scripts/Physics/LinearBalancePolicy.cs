using System;
using UnityEngine;

namespace PushHands.Physical
{
    [Serializable]
    public sealed class LinearBalancePolicy
    {
        public const int ObservationSize = 10;
        public string version = PhysicalConfig.Version;
        public bool validated;
        public float[] weights = new float[ObservationSize * 2];
        public float[] bias = new float[2];
        public static LinearBalancePolicy LoadValidated(string json)
        {
            try
            {
                var policy = JsonUtility.FromJson<LinearBalancePolicy>(json);
                return policy != null && policy.Valid && policy.validated ? policy : null;
            }
            catch (ArgumentException) { return null; }
        }
        public bool Valid
        {
            get
            {
                if (version != PhysicalConfig.Version || weights == null || bias == null || weights.Length != ObservationSize * 2 || bias.Length != 2) return false;
                foreach (float value in weights) if (float.IsNaN(value) || float.IsInfinity(value) || Mathf.Abs(value) > 20) return false;
                foreach (float value in bias) if (float.IsNaN(value) || float.IsInfinity(value) || Mathf.Abs(value) > 20) return false;
                return true;
            }
        }
        public Vector2 Evaluate(float[] observation)
        {
            if (!Valid || observation == null || observation.Length != ObservationSize) return Vector2.zero;
            Vector2 result = Vector2.zero;
            for (int output = 0; output < 2; output++)
            {
                float value = bias[output];
                for (int i = 0; i < ObservationSize; i++)
                { if (float.IsNaN(observation[i]) || float.IsInfinity(observation[i])) return Vector2.zero; value += weights[output * ObservationSize + i] * Mathf.Clamp(observation[i], -3, 3); }
                result[output] = (float)Math.Tanh(value);
            }
            return Vector2.ClampMagnitude(result, 1);
        }
        public static float[] Observe(PhysicalFighter fighter, PlayerIntent intent)
        {
            var state = fighter.CaptureBalance();
            return new[] { state.Center.x / 0.23f, state.Center.y / 0.23f, state.Velocity.x, state.Velocity.y,
                intent.Lean.x, intent.Lean.y, intent.LeftForce, intent.RightForce,
                fighter.Technique.WithdrawWeight, fighter.Technique.FeintWeight };
        }
    }
}
