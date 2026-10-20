using UnityEngine;

namespace PushHands.Physical
{
    [CreateAssetMenu(menuName = "推手/物理人偶参数")]
    public sealed class PhysicalConfig : ScriptableObject
    {
        public const string Version = "physical-v3";
        [Header("固定步与站姿")]
        [Range(0.005f, 0.02f)] public float FixedStep = 0.01f;
        [Range(0.9f, 1.2f)] public float Spacing = 0.98f;
        public Vector2 SupportHalfSize = new Vector2(0.23f, 0.23f);
        public float StepDelay = 0.20f, FootStepDistance = 0.10f;
        [Header("有限肌肉驱动")]
        public float BodySpring = 750, BodyDamper = 75, BodyTorque = 220;
        public float ArmSpring = 650, ArmDamper = 50, ArmTorque = 75;
        public float FootSpring = 1600, FootDamper = 100, FootForce = 220, FootTorque = 100;
        public float MaximumStandForce = 1150, MaximumBalanceForce = 160;
        public float PassiveBalanceForce = 35, PassiveRootTorque = 25, ActiveRootTorque = 100;
        public float LeanTravel = 0.14f;
        public float ActiveBalancePush = 110;
        public float ProtectiveStepDistance = 0.22f, StepLiftHeight = 0.09f, StepSeconds = 0.42f, MaximumStepForce = 350;
        public float StandingStiffness = 80, StandingDamping = 18;
        [Header("重心与伸手")]
        public float LeanDegrees = 11, EffortDegrees = 8, RecoveryGain = 48, RecoveryDamping = 10;
        public float AimSpeed = 0.9f, HandAdvance = 0.045f, WithdrawDistance = 0.16f;
        public float MaximumHandReach = 0.59f, SharedEffort = 1.45f;
        public float PushForcePerHand = 55, ForwardCommitmentGain = 0.5f;
        [Range(0, 1)] public float ContactBodyShare = 0.8f;
        [Header("稳定架势与准备手位")]
        public float GuardHeight = 1.445f, GuardReach = 0.40f, GuardWidth = 0.16f;
        public float PoseResponse = 12, JointDampingScale = 1.6f;
        public float CenterDeadZone = 0.006f, VelocityDeadZone = 0.015f;
        public float GravityCompensation = 0.9f;
        public float HandPlaneSpring = 300, HandPlaneDamper = 25, MaximumHandPlaneForce = 55;
        [Header("视角")]
        public float YawLimit = 35, PitchLimit = 20, PadLookSpeed = 85, MouseSensitivity = 0.10f;
        public float ViewDownDegrees = 8;
        private void OnValidate()
        {
            FixedStep = Mathf.Clamp(FixedStep, 0.005f, 0.02f); Spacing = Mathf.Clamp(Spacing, 0.9f, 1.2f);
            StepDelay = Mathf.Max(0.05f, StepDelay); BodyTorque = Mathf.Clamp(BodyTorque, 1, 900);
            ArmTorque = Mathf.Clamp(ArmTorque, 1, 300); FootForce = Mathf.Clamp(FootForce, 1, 900);
            GuardHeight = Mathf.Clamp(GuardHeight, 1.34f, 1.50f); GuardReach = Mathf.Clamp(GuardReach, 0.30f, 0.42f);
            GuardWidth = Mathf.Clamp(GuardWidth, 0.12f, 0.22f); PoseResponse = Mathf.Clamp(PoseResponse, 4, 25);
            JointDampingScale = Mathf.Clamp(JointDampingScale, 0.5f, 2); GravityCompensation = Mathf.Clamp01(GravityCompensation);
            PushForcePerHand = Mathf.Clamp(PushForcePerHand, 0, 200); ForwardCommitmentGain = Mathf.Clamp(ForwardCommitmentGain, 0, 1);
            ContactBodyShare = Mathf.Clamp01(ContactBodyShare); PassiveBalanceForce = Mathf.Clamp(PassiveBalanceForce, 0, MaximumBalanceForce);
            PassiveRootTorque = Mathf.Clamp(PassiveRootTorque, 0, BodyTorque); ActiveRootTorque = Mathf.Clamp(ActiveRootTorque, PassiveRootTorque, BodyTorque);
            LeanTravel = Mathf.Clamp(LeanTravel, 0.05f, 0.18f); ActiveBalancePush = Mathf.Clamp(ActiveBalancePush, 0, 200);
            StepSeconds = Mathf.Clamp(StepSeconds, 0.25f, 0.8f); MaximumStepForce = Mathf.Clamp(MaximumStepForce, 100, 600);
        }
    }
}
