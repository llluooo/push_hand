using UnityEngine;

namespace PushHands
{
    [CreateAssetMenu(menuName = "推手/灰盒参数")]
    public sealed class PrototypeConfig : ScriptableObject
    {
        [Header("平衡与支撑（米、秒、牛顿）")]
        public float StepSeconds = 0.25f;
        public Vector2 SupportHalfSize = new Vector2(0.19f, 0.20f);
        public float Mass = 70f;
        public float CenterHeight = 1.0f;
        public float Gravity = 9.81f;
        public float RecoveryStiffness = 24f;
        public float RecoveryDamping = 5f;
        public float MaxRecoveryAcceleration = 8f;
        public float LeanRange = 0.13f;
        public float EffortLean = 0.085f;
        [Header("双手")]
        public float PlayerSpacing = 0.85f;
        public float TorsoRadius = 0.19f;
        public float Reach = 1.0f;
        public float ContactHalfWidth = 0.32f;
        public float ExtendSeconds = 0.32f;
        public float RetractSeconds = 0.24f;
        public float RecoverSeconds = 0.16f;
        public float ForceRisePerSecond = 2.4f;
        public float HandForce = 220f;
        public float SharedForce = 280f;
        [Header("表现与观察")]
        public float YawLimit = 35f;
        public float PitchLimit = 20f;
        public float GamepadLookSpeed = 85f;
        public float MouseSensitivity = 0.10f;
        public float StepAnimationSeconds = 0.30f;
    }
}
