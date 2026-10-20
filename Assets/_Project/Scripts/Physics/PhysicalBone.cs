using System;
using UnityEngine;

namespace PushHands.Physical
{
    [Serializable]
    public sealed class PhysicalBone
    {
        public string Name;
        public Rigidbody Body;
        public Collider Collider;
        public ConfigurableJoint Joint;
        public ContactRegion Region;
        public Vector3 StartPosition;
        public Quaternion StartRotation;
        public Quaternion StartLocalRotation;
        [NonSerialized] public Vector3 SavedVelocity, SavedAngularVelocity;
        [NonSerialized] public PhysicalBone Parent;
        [NonSerialized] public Quaternion GoalRotation;
    }

    [Serializable]
    public struct BalanceSample
    {
        public Vector2 Center, Velocity, Predicted, RequiredSupport;
        public float Height, FootDisplacement;
        public bool Finite;
    }

    [Serializable]
    public struct PoseSample
    {
        public Vector3 Head, LeftPalm, RightPalm;
        public float MeanAngularSpeedSquared;
    }
}
