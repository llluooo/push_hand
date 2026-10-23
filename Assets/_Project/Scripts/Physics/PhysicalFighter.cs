using System.Collections.Generic;
using UnityEngine;

namespace PushHands.Physical
{
    public sealed class PhysicalFighter : MonoBehaviour
    {
        public PhysicalConfig Config;
        public PhysicalArena Arena;
        public int PlayerIndex;
        public PhysicalBone[] Bones;
        public Camera ViewCamera;
        public readonly TechniqueState Technique = new TechniqueState();
        public Vector3[] PalmTargets { get; private set; } = new Vector3[2];
        public Rigidbody Pelvis => Bones[0].Body;
        public bool Frozen { get; private set; }
        public bool Stepping { get; private set; }
        public bool StepLanded { get; private set; }
        public bool StepPresentationComplete { get; private set; }
        public bool Falling { get; private set; }
        public bool GroundLanded { get; private set; }
        public bool PresentationTimedOut { get; private set; }
        public Vector2 LearningCorrection { get; set; }
        public float[] Effort { get; } = new float[2];
        public Vector3 ActivePushForce { get; private set; }
        public Vector2 SupportMinimum { get { Cache(); return supportMin; } }
        public Vector2 SupportMaximum { get { Cache(); return supportMax; } }
        private PhysicalBone[] palms, forearms, upperArms, thighs, shins, feet, clavicles;
        private PhysicalBone[][] supportChains;
        private JointDrive[] standingDrives;
        private Quaternion[] stepPose;
        private int[] standingLayers;
        private Vector2 look;
        private Vector2 controlLean, bodyTilt, restCenter;
        private Vector3 restGravityTorque;
        private Vector3 activePressureMoment;
        private Vector2 filteredSupportShift, supportMin, supportMax;
        private PhysicalFighter opponent;
        private bool cached;
        private float unstable, stepTime, landingTime;
        private float fallTime, groundLandingTime;
        private int stepSide = -1;
        private Vector3 stepFrom, stepTo;
        private Vector3 feedbackPosition, feedbackAngles, lastFeedbackWorld;
        private PhysicalContact[] palmContacts, forearmContacts;
        private readonly float[] handPushForce = new float[2];
        private readonly Dictionary<string, PhysicalBone> named = new Dictionary<string, PhysicalBone>();
        public int ContactCount
        {
            get
            {
                int count = 0;
                foreach (var bone in Bones)
                { var sensor = bone.Body.GetComponent<PhysicalContact>(); if (sensor != null) count += sensor.Count; }
                return count / 2 + count % 2;
            }
        }

        public static PhysicalFighter Create(int index, PhysicalConfig config, Material material)
        {
            var root = new GameObject("PhysicalFighter" + (index + 1));
            root.transform.SetPositionAndRotation(new Vector3(0, 0, (index == 0 ? -1 : 1) * config.Spacing * 0.5f), Quaternion.Euler(0, index * 180, 0));
            var fighter = root.AddComponent<PhysicalFighter>(); fighter.Config = config; fighter.PlayerIndex = index;
            var bones = new List<PhysicalBone>();
            var pelvis = fighter.AddBone(bones, "Pelvis", new Vector3(0, 0.94f, 0), new Vector3(0.30f, 0.18f, 0.22f), 12, ContactRegion.Torso, null, new Vector3(0, 0.94f, 0), material, 25);
            var waist = fighter.AddBone(bones, "Lumbar", new Vector3(0, 1.09f, 0), new Vector3(0.25f, 0.16f, 0.20f), 5, ContactRegion.Torso, pelvis, new Vector3(0, 1.025f, 0), material, 25);
            var lower = fighter.AddBone(bones, "LowerThorax", new Vector3(0, 1.23f, 0), new Vector3(0.34f, 0.16f, 0.23f), 7, ContactRegion.Torso, waist, new Vector3(0, 1.16f, 0), material, 18);
            var chest = fighter.AddBone(bones, "UpperThorax", new Vector3(0, 1.39f, 0), new Vector3(0.39f, 0.17f, 0.24f), 8, ContactRegion.Torso, lower, new Vector3(0, 1.31f, 0), material, 18);
            var neck = fighter.AddBone(bones, "Neck", new Vector3(0, 1.54f, 0), new Vector3(0.10f, 0.12f, 0.10f), 1, ContactRegion.Head, chest, new Vector3(0, 1.48f, 0), material, 25);
            fighter.AddBone(bones, "Head", new Vector3(0, 1.68f, 0), new Vector3(0.22f, 0.25f, 0.22f), 4, ContactRegion.Head, neck, new Vector3(0, 1.59f, 0), material, 35);
            for (int side = 0; side < 2; side++)
            {
                string prefix = side == 0 ? "Left" : "Right"; float sign = side == 0 ? -1 : 1;
                Vector3 hip = new Vector3(sign * 0.13f, 0.93f, 0), knee = new Vector3(sign * 0.15f, 0.53f, 0.065f);
                Vector3 ankle = new Vector3(sign * 0.15f, 0.13f, side == 0 ? 0.055f : -0.055f);
                var thigh = fighter.AddSegment(bones, prefix + "Thigh", hip, knee, 0.085f, 7, ContactRegion.Leg, pelvis, hip, material, 65);
                var shin = fighter.AddSegment(bones, prefix + "Shin", knee, ankle, 0.065f, 3, ContactRegion.Leg, thigh, knee, material, 75);
                fighter.ConfigureHinge(shin, -100, 12);
                var foot = fighter.AddBone(bones, prefix + "Foot", ankle + new Vector3(0, -0.065f, 0.025f), new Vector3(0.14f, 0.12f, 0.26f), 1, ContactRegion.Leg, shin, ankle, material, 35);
                // 站姿约束只有有限驱动力；不是位置锁定，足部超过位移阈值仍判迈步。
                var support = foot.Body.gameObject.AddComponent<ConfigurableJoint>();
                support.autoConfigureConnectedAnchor = false; support.anchor = Vector3.zero; support.connectedAnchor = foot.Body.position;
                support.xMotion = support.yMotion = support.zMotion = ConfigurableJointMotion.Free;
                support.angularXMotion = support.angularYMotion = support.angularZMotion = ConfigurableJointMotion.Free;
                support.configuredInWorldSpace = true;
                var supportDrive = new JointDrive { positionSpring = config.FootSpring, positionDamper = config.FootDamper, maximumForce = config.FootForce };
                support.xDrive = support.zDrive = supportDrive;
                Vector3 shoulder = new Vector3(sign * 0.24f, 1.44f, 0);
                Vector3 wrist = new Vector3(sign * config.GuardWidth, config.GuardHeight - 0.015f, config.GuardReach - 0.055f);
                Vector3 elbow = new Vector3(sign * 0.30f, 1.25f, 0.15f);
                var clavicle = fighter.AddSegment(bones, prefix + "Clavicle", new Vector3(sign * 0.08f, 1.44f, 0), shoulder, 0.06f, 1, ContactRegion.UpperArm, chest, new Vector3(sign * 0.08f, 1.44f, 0), material, 25);
                var upper = fighter.AddSegment(bones, prefix + "UpperArm", shoulder, elbow, 0.055f, 2, ContactRegion.UpperArm, clavicle, shoulder, material, 100);
                var forearm = fighter.AddSegment(bones, prefix + "Forearm", elbow, wrist, 0.047f, 1.5f, ContactRegion.Forearm, upper, elbow, material, 110);
                // 限位相对于创建时弯肘姿态，伸直所需的角度必须扣除这段初始弯曲。
                float initialBend = Vector3.Angle(elbow - shoulder, wrist - elbow);
                fighter.ConfigureHinge(forearm, initialBend - 155, initialBend - 5);
                fighter.AddBone(bones, prefix + "Palm", wrist + new Vector3(0, 0.015f, 0.055f), new Vector3(0.105f, 0.145f, 0.06f), 1, ContactRegion.Palm, forearm, wrist, material, 45);
            }
            fighter.Bones = bones.ToArray(); fighter.Cache();
            var cameraObject = new GameObject("FirstPersonCamera"); cameraObject.transform.SetParent(root.transform, false);
            fighter.ViewCamera = cameraObject.AddComponent<Camera>();
            fighter.ViewCamera.rect = new Rect(index * 0.5f, 0, 0.5f, 1); fighter.ViewCamera.nearClipPlane = 0.025f;
            fighter.ViewCamera.fieldOfView = config.ViewFieldOfView; fighter.ViewCamera.farClipPlane = 50;
            // 只排除自己的头与颈；身体、手臂仍可见。沿用原工程的头部专用层。
            fighter.named["Head"].Body.gameObject.layer = 8 + index;
            fighter.named["Neck"].Body.gameObject.layer = 8 + index;
            for (int i = 0; i < fighter.Bones.Length; i++) fighter.standingLayers[i] = fighter.Bones[i].Body.gameObject.layer;
            fighter.ViewCamera.cullingMask &= ~(1 << (8 + index));
            if (index == 0) cameraObject.AddComponent<AudioListener>();
            fighter.ResetPose(); fighter.RenderView(0);
            return fighter;
        }

        public static void ConfigureInteraction(PhysicalFighter a, PhysicalFighter b)
        {
            foreach (var x in a.Bones) foreach (var y in b.Bones)
                Physics.IgnoreCollision(x.Collider, y.Collider, !ContactRules.IsLegal(x.Region, y.Region));
        }
        private PhysicalBone AddSegment(List<PhysicalBone> list, string name, Vector3 a, Vector3 b, float radius, float mass, ContactRegion region, PhysicalBone parent, Vector3 anchor, Material material, float limit)
        {
            return AddBone(list, name, (a + b) * 0.5f, new Vector3(radius * 2, Vector3.Distance(a, b), radius * 2), mass, region,
                parent, anchor, material, limit, Quaternion.FromToRotation(Vector3.up, b - a));
        }
        private PhysicalBone AddBone(List<PhysicalBone> list, string name, Vector3 position, Vector3 size, float mass, ContactRegion region, PhysicalBone parent, Vector3 anchor, Material material, float limit, Quaternion? localRotation = null)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name = name; obj.transform.SetParent(transform, false);
            obj.transform.localPosition = position; obj.transform.localScale = size; obj.transform.localRotation = localRotation ?? Quaternion.identity;
            if (material != null) obj.GetComponent<Renderer>().sharedMaterial = material;
            var body = obj.AddComponent<Rigidbody>(); body.mass = mass; body.drag = 0.03f; body.angularDrag = 0.12f;
            body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.solverIterations = 16; body.solverVelocityIterations = 8; body.maxAngularVelocity = 12;
            var joint = obj.AddComponent<ConfigurableJoint>(); joint.connectedBody = parent?.Body;
            joint.autoConfigureConnectedAnchor = false; Vector3 worldAnchor = transform.TransformPoint(anchor);
            joint.anchor = obj.transform.InverseTransformPoint(worldAnchor);
            joint.connectedAnchor = parent != null ? parent.Body.transform.InverseTransformPoint(worldAnchor) : worldAnchor;
            joint.xMotion = joint.yMotion = joint.zMotion = parent != null ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Free;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Limited;
            if (parent == null) joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
            joint.lowAngularXLimit = new SoftJointLimit { limit = -limit }; joint.highAngularXLimit = new SoftJointLimit { limit = limit };
            joint.angularYLimit = new SoftJointLimit { limit = limit }; joint.angularZLimit = new SoftJointLimit { limit = limit };
            joint.rotationDriveMode = RotationDriveMode.Slerp; joint.configuredInWorldSpace = false;
            bool arm = region == ContactRegion.Palm || region == ContactRegion.Forearm || region == ContactRegion.UpperArm;
            joint.slerpDrive = new JointDrive { positionSpring = arm ? Config.ArmSpring : Config.BodySpring,
                positionDamper = (arm ? Config.ArmDamper : Config.BodyDamper) * Config.JointDampingScale, maximumForce = name.EndsWith("Foot") ? Config.FootTorque : arm ? Config.ArmTorque : Config.BodyTorque };
            joint.projectionMode = JointProjectionMode.None; joint.enableCollision = false;
            var bone = new PhysicalBone { Name = name, Body = body, Collider = obj.GetComponent<Collider>(), Joint = joint,
                Region = region, StartPosition = body.position, StartRotation = body.rotation,
                StartLocalRotation = parent != null ? Quaternion.Inverse(parent.Body.rotation) * body.rotation : body.rotation };
            var sensor = obj.AddComponent<PhysicalContact>(); sensor.Owner = this; sensor.Region = region;
            list.Add(bone); return bone;
        }
        private void Cache()
        {
            if (cached) return; named.Clear(); foreach (var bone in Bones) named.Add(bone.Name, bone);
            foreach (var bone in Bones)
            {
                // 这些原生物理属性不会随2022版场景/预制体保存，加载后须恢复创建时的精度。
                bone.Body.solverIterations = 16; bone.Body.solverVelocityIterations = 8; bone.Body.maxAngularVelocity = 12;
                bone.GoalRotation = bone.StartRotation;
                foreach (var candidate in Bones)
                    if (candidate.Body == bone.Joint.connectedBody) { bone.Parent = candidate; break; }
            }
            supportChains = new PhysicalBone[Bones.Length][];
            standingDrives = new JointDrive[Bones.Length];
            stepPose = new Quaternion[Bones.Length];
            standingLayers = new int[Bones.Length];
            for (int i = 0; i < Bones.Length; i++)
            {
                standingDrives[i] = Bones[i].Joint.slerpDrive;
                standingLayers[i] = Bones[i].Body.gameObject.layer;
                var chain = new List<PhysicalBone>();
                foreach (var candidate in Bones)
                    for (var parent = candidate; parent != null; parent = parent.Parent)
                        if (parent == Bones[i]) { chain.Add(candidate); break; }
                supportChains[i] = chain.ToArray();
            }
            palms = Pair("Palm"); forearms = Pair("Forearm"); upperArms = Pair("UpperArm"); clavicles = Pair("Clavicle");
            palmContacts = new[] { palms[0].Body.GetComponent<PhysicalContact>(), palms[1].Body.GetComponent<PhysicalContact>() };
            forearmContacts = new[] { forearms[0].Body.GetComponent<PhysicalContact>(), forearms[1].Body.GetComponent<PhysicalContact>() };
            thighs = Pair("Thigh"); shins = Pair("Shin"); feet = Pair("Foot"); cached = true;
            supportMin = Vector2.one * 100; supportMax = Vector2.one * -100;
            foreach (var foot in feet)
            {
                Vector3 position = transform.InverseTransformPoint(foot.StartPosition);
                Vector2 center = new Vector2(position.x, position.z);
                Vector2 half = new Vector2(foot.Body.transform.localScale.x, foot.Body.transform.localScale.z) * 0.5f;
                supportMin = Vector2.Min(supportMin, center - half); supportMax = Vector2.Max(supportMax, center + half);
            }
            supportMin = Vector2.Max(supportMin, -Config.SupportHalfSize); supportMax = Vector2.Min(supportMax, Config.SupportHalfSize);
            // IgnoreCollision 不随场景保存。每次加载真实身体后重新建立自碰撞过滤。
            foreach (var first in Bones) foreach (var second in Bones)
                if (first != second) Physics.IgnoreCollision(first.Collider, second.Collider, true);
        }
        private PhysicalBone[] Pair(string name) => new[] { named["Left" + name], named["Right" + name] };
        public Rigidbody Palm(int side) { Cache(); return palms[side].Body; }

        public void Drive(PlayerIntent intent, bool withdraw, bool feint, PhysicalFighter other, float dt)
        {
            if (Frozen || dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            Cache(); Technique.Tick(withdraw, feint, dt);
            ActivePushForce = Vector3.zero;
            handPushForce[0] = handPushForce[1] = 0;
            activePressureMoment = Vector3.zero; opponent = other;
            foreach (var bone in Bones) bone.Body.GetComponent<PhysicalContact>().ResetImpulse();
            float retreat = Technique.WithdrawWeight;
            float desiredLeft = Mathf.Clamp01(intent.LeftForce), desiredRight = Mathf.Clamp01(intent.RightForce);
            if (Technique.Active == global::PushHands.Physical.Technique.Withdraw || Technique.Recovering) desiredLeft = desiredRight = 0;
            if (Technique.Active == global::PushHands.Physical.Technique.Feint)
                desiredLeft = desiredRight = Technique.FeintWeight * 0.42f;
            float sum = desiredLeft + desiredRight;
            if (sum > Config.SharedEffort) { desiredLeft *= Config.SharedEffort / sum; desiredRight *= Config.SharedEffort / sum; }
            Effort[0] = Mathf.MoveTowards(Effort[0], desiredLeft, dt * (desiredLeft == 0 ? 12 : 3));
            Effort[1] = Mathf.MoveTowards(Effort[1], desiredRight, dt * (desiredRight == 0 ? 12 : 3));
            BalanceSample balance = CaptureBalance();
            float response = 1 - Mathf.Exp(-Config.PoseResponse * dt);
            // 主动重心靠肌肉逐渐建立；不能把满幅按键当作瞬时全身冲击。
            controlLean = Vector2.MoveTowards(controlLean, Vector2.ClampMagnitude(intent.Lean, 1), dt * Config.LeanInputSpeed);
            Vector2 lean = controlLean;
            var rootDrive = Bones[0].Joint.slerpDrive;
            rootDrive.maximumForce = Mathf.Lerp(Config.PassiveRootTorque, Config.ActiveRootTorque, lean.magnitude);
            Bones[0].Joint.slerpDrive = rootDrive;
            SupportStance(balance, lean, retreat);
            Vector2 correction = Vector2.ClampMagnitude(LearningCorrection, 1) * 2;
            Vector2 error = balance.Center - restCenter;
            float pitch = lean.y * Config.LeanDegrees + Mathf.Max(Effort[0], Effort[1]) * Config.EffortDegrees - retreat * 13
                - OutsideDeadZone(error.y, Config.CenterDeadZone) * Config.RecoveryGain
                - OutsideDeadZone(balance.Velocity.y, Config.VelocityDeadZone) * Config.RecoveryDamping + correction.y;
            float roll = -lean.x * Config.LeanDegrees + OutsideDeadZone(error.x, Config.CenterDeadZone) * Config.RecoveryGain
                + OutsideDeadZone(balance.Velocity.x, Config.VelocityDeadZone) * Config.RecoveryDamping - correction.x;
            bodyTilt = Vector2.Lerp(bodyTilt, new Vector2(Mathf.Clamp(pitch, -22, 22), Mathf.Clamp(roll, -18, 18)), response);
            Quaternion tilt = Quaternion.Euler(bodyTilt.x, 0, bodyTilt.y);
            foreach (var bone in Bones)
                SetGoal(bone, transform.rotation * tilt * Quaternion.Inverse(transform.rotation) * bone.StartRotation);
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1 : 1;
                // 腿长不随表现插值缩放；两段 IK 只提供关节旋转目标，实际位置仍由物理求解。
                // 腿部追求可站立的目标髋位，而非把正在下沉的真实骨盆继续当作目标，避免越塌越蹲。
                Vector3 hip = transform.TransformPoint(new Vector3(sign * 0.13f + lean.x * Config.LeanTravel,
                    0.93f - retreat * 0.035f, lean.y * Config.LeanTravel - retreat * 0.04f));
                Vector3 ankle = feet[side].Body.position + transform.up * 0.065f - transform.forward * 0.025f;
                Vector3 knee = Bend(hip, ankle, thighs[side].Body.transform.localScale.y, shins[side].Body.transform.localScale.y, transform.forward);
                SetSegmentGoal(thighs[side], hip, knee); SetSegmentGoal(shins[side], knee, ankle);
                SetGoal(feet[side], feet[side].StartRotation);
                Vector3 shoulder = upperArms[side].Joint.connectedBody.transform.TransformPoint(upperArms[side].Joint.connectedAnchor);
                // 过滤空闲微振，但身体已被明显推动时，准备手位必须随身体移动，不能锚定开局世界位置。
                Vector3 chestOffset = transform.InverseTransformDirection(named["UpperThorax"].Body.position - named["UpperThorax"].StartPosition);
                float voluntaryTravel = Config.LeanTravel + Mathf.Deg2Rad * Config.LeanDegrees * 0.45f;
                chestOffset -= new Vector3(lean.x * voluntaryTravel, 0, lean.y * voluntaryTravel);
                chestOffset = new Vector3(OutsideDeadZone(chestOffset.x, 0.025f), OutsideDeadZone(chestOffset.y, 0.025f), OutsideDeadZone(chestOffset.z, 0.025f));
                Vector3 guard = transform.TransformPoint(new Vector3(sign * Config.GuardWidth + lean.x * 0.03f,
                    Config.GuardHeight - retreat * 0.02f, Config.GuardReach + lean.y * 0.04f - retreat * Config.WithdrawDistance) + chestOffset);
                Vector3 target = guard;
                if (Effort[side] > 0.01f && other != null)
                {
                    Vector3 legalTarget = other.Palm(1 - side).position;
                    // 中线与可达范围由当前身体位置计算，不能用开局位置阻止有效的重心前移。
                    Vector3 local = transform.InverseTransformPoint(legalTarget);
                    local.z = Mathf.Clamp(local.z + Config.HandAdvance * Effort[side], 0.20f,
                        transform.InverseTransformPoint(shoulder).z + Config.MaximumHandReach);
                    // 在胸肩之间的稳定通道接触，不能将对手受碰撞后的抬手误差逐帧追高。
                    local.y = Config.GuardHeight;
                    local.x = Mathf.Clamp(local.x, sign * Config.GuardWidth - 0.035f, sign * Config.GuardWidth + 0.035f);
                    float torsoPlane = transform.InverseTransformPoint(other.named["UpperThorax"].Body.position).z - 0.22f;
                    local.z = Mathf.Min(local.z, torsoPlane);
                    target = Vector3.Lerp(guard, transform.TransformPoint(local), Mathf.Clamp01(Effort[side] * 2));
                }
                target = shoulder + Vector3.ClampMagnitude(target - shoulder, Config.MaximumHandReach);
                PalmTargets[side] = Vector3.MoveTowards(PalmTargets[side], target, dt * Config.AimSpeed * (retreat > 0 ? 1.8f : 1));
                Vector3 wrist = PalmTargets[side] - transform.forward * 0.055f - transform.up * 0.015f;
                Vector3 elbow = Bend(shoulder, wrist, upperArms[side].Body.transform.localScale.y, forearms[side].Body.transform.localScale.y,
                    transform.TransformDirection(new Vector3(sign * 0.6f, -1, -0.2f)));
                SetSegmentGoal(upperArms[side], shoulder, elbow); SetSegmentGoal(forearms[side], elbow, wrist);
                SetGoal(palms[side], transform.rotation);
                SupportHandPlane(side);
                ApplyContactPush(side, lean, other);
            }
            SupportPose();
        }
        private static float OutsideDeadZone(float value, float zone)
        { return Mathf.Sign(value) * Mathf.Max(0, Mathf.Abs(value) - zone); }
        private void ApplyContactPush(int side, Vector2 lean, PhysicalFighter other)
        {
            if (Effort[side] <= 0 || other == null) return;
            var source = palms[side].Body;
            if (!source.GetComponent<PhysicalContact>().TryGetPushContact(other, transform.forward, out var recipient, out var point, out var direction))
            {
                source = forearms[side].Body;
                if (!source.GetComponent<PhysicalContact>().TryGetPushContact(other, transform.forward, out recipient, out point, out direction)) return;
            }
            // 主动接触压力在真实接触点成对施加，避免被伸手位置伺服抵消；双方总力与总力矩保持平衡。
            float pressure = Config.PushForcePerHand * Effort[side] * (1 + Mathf.Max(0, lean.y) * Config.ForwardCommitmentGain);
            Vector3 force = direction * (pressure * Mathf.Max(0, Vector3.Dot(direction, transform.forward)));
            // 有限身体链传力：接触载荷不能全耗在轻质手掌及姿态伺服上。
            float share = Mathf.Clamp01(Config.ContactBodyShare);
            recipient.AddForceAtPosition(force * (1 - share), point, ForceMode.Force);
            other.named["UpperThorax"].Body.AddForceAtPosition(force * share, point, ForceMode.Force);
            // 肌肉经手臂传来的接触反作用由上身承担，避免把自己的手掌弹离接触点。
            named["UpperThorax"].Body.AddForceAtPosition(-force, point, ForceMode.Force);
            ActivePushForce += force;
            handPushForce[side] = force.magnitude;
            activePressureMoment += Vector3.Cross(point, force);
        }
        private void SupportHandPlane(int side)
        {
            var palm = palms[side].Body; var chest = named["UpperThorax"].Body;
            Vector3 error = transform.InverseTransformDirection(PalmTargets[side] - palm.position);
            Vector3 velocity = transform.InverseTransformDirection(palm.velocity - chest.velocity);
            // 手臂在横向和高度上维持有限接触通道；不在前后方向添加推力或吸力。
            Vector3 force = new Vector3(error.x * Config.HandPlaneSpring - velocity.x * Config.HandPlaneDamper,
                error.y * Config.HandPlaneSpring - velocity.y * Config.HandPlaneDamper, 0);
            force = transform.TransformDirection(Vector3.ClampMagnitude(force, Config.MaximumHandPlaneForce));
            palm.AddForce(force, ForceMode.Force); chest.AddForce(-force, ForceMode.Force);
        }
        private void SupportPose()
        {
            // 身体链共同承担自重，避免只抬起手臂，却把反作用全部压给未补偿的脊柱。
            for (int i = 0; i < Bones.Length; i++)
            {
                var root = Bones[i];
                Vector3 anchor = root.Body.transform.TransformPoint(root.Joint.anchor), torque = Vector3.zero;
                if (root.Parent == null) torque = restGravityTorque;
                else foreach (var part in supportChains[i]) torque += GravityMoment(part, anchor);
                bool arm = root.Region == ContactRegion.Palm || root.Region == ContactRegion.Forearm || root.Region == ContactRegion.UpperArm;
                torque = Vector3.ClampMagnitude(torque * Config.GravityCompensation, (arm ? Config.ArmTorque : Config.BodyTorque) * 0.5f);
                root.Body.AddTorque(torque, ForceMode.Force);
                if (root.Parent != null) root.Parent.Body.AddTorque(-torque, ForceMode.Force);
                else foreach (var foot in feet) foot.Body.AddTorque(-torque * 0.5f, ForceMode.Force);
            }
        }
        private static Vector3 GravityMoment(PhysicalBone bone, Vector3 anchor)
        { return bone == null ? Vector3.zero : Vector3.Cross(bone.Body.worldCenterOfMass - anchor, -Physics.gravity * bone.Body.mass); }
        private static Vector3 Bend(Vector3 root, Vector3 tip, float first, float second, Vector3 hint)
        {
            Vector3 delta = tip - root; float distance = Mathf.Clamp(delta.magnitude, 0.025f, first + second - 0.002f);
            Vector3 direction = delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector3.down;
            float along = (first * first - second * second + distance * distance) / (2 * distance);
            Vector3 bend = Vector3.ProjectOnPlane(hint, direction).normalized;
            return root + direction * along + bend * Mathf.Sqrt(Mathf.Max(0, first * first - along * along));
        }
        private void SupportStance(BalanceSample balance, Vector2 lean, float retreat, int liftedSide = -1)
        {
            float mass = 0; foreach (var bone in Bones) mass += bone.Body.mass;
            float desiredHeight = transform.position.y + (Stepping ? 0.89f : 0.94f) - retreat * 0.035f;
            float vertical = Mathf.Clamp(mass * (9.81f + (desiredHeight - Pelvis.position.y) * Config.StandingStiffness
                - Pelvis.velocity.y * Config.StandingDamping), 0, Config.MaximumStandForce);
            Vector2 desiredCenter = restCenter + lean * Config.LeanTravel + Vector2.up * (Mathf.Max(Effort[0], Effort[1]) * 0.045f - retreat * 0.06f);
            float extreme = Mathf.InverseLerp(0.85f, 1, lean.magnitude);
            desiredCenter += lean.normalized * (extreme * extreme * Config.ExtremeLeanTravel);
            // 空闲恢复只消除小扰动；主要防守能力来自玩家主动调整重心。
            float balanceLimit = Mathf.Lerp(Config.PassiveBalanceForce, Config.MaximumBalanceForce, lean.magnitude);
            Vector2 horizontal = Vector2.ClampMagnitude(((desiredCenter - balance.Center) * 18 - balance.Velocity * 6) * mass
                + lean * Config.ActiveBalancePush, balanceLimit);
            // 已经提交的保护步应保留失衡动量，不能靠旧站姿辅助把身体拉回开局重心。
            if (Arena != null && Stepping && !StepLanded) horizontal = Vector2.zero;
            Vector3 force = transform.TransformDirection(new Vector3(horizontal.x, 0, horizontal.y)) + Vector3.up * vertical;
            // 任务空间的站姿肌肉：骨盆与足部之间施加等大反向作用，依赖地面支撑而非悬空抬升。
            // 力量有上限，脚被推动或恢复不及时仍会越界、触发保护步。
            int supportingFeet = 0;
            for (int i = 0; i < feet.Length; i++)
                if (i != liftedSide && (Arena == null || Arena.HasPlatformSupport(feet[i].Body.position))) supportingFeet++;
            if (supportingFeet == 0) return;
            Pelvis.AddForce(force, ForceMode.Force);
            for (int i = 0; i < feet.Length; i++)
                if (i != liftedSide && (Arena == null || Arena.HasPlatformSupport(feet[i].Body.position)))
                    feet[i].Body.AddForce(-force / supportingFeet, ForceMode.Force);
        }
        private void ConfigureHinge(PhysicalBone bone, float lower, float upper)
        {
            bone.Joint.axis = bone.Body.transform.InverseTransformDirection(transform.right);
            bone.Joint.secondaryAxis = bone.Body.transform.InverseTransformDirection(transform.up);
            bone.Joint.lowAngularXLimit = new SoftJointLimit { limit = lower };
            bone.Joint.highAngularXLimit = new SoftJointLimit { limit = upper };
            bone.Joint.angularYLimit = new SoftJointLimit { limit = 8 };
            bone.Joint.angularZLimit = new SoftJointLimit { limit = 8 };
        }
        private void SetSegmentGoal(PhysicalBone bone, Vector3 a, Vector3 b)
        { SetGoal(bone, Quaternion.FromToRotation(bone.StartRotation * Vector3.up, b - a) * bone.StartRotation); }
        private static void SetGoal(PhysicalBone bone, Quaternion worldRotation)
        {
            var joint = bone.Joint;
            Quaternion basis = Quaternion.LookRotation(Vector3.Cross(joint.axis, joint.secondaryAxis), joint.secondaryAxis);
            bone.GoalRotation = worldRotation;
            Quaternion desiredLocal = bone.Parent != null ? Quaternion.Inverse(bone.Parent.GoalRotation) * worldRotation : worldRotation;
            // 用父段的目标姿态转换局部目标，不能把父段实时抖动再次输入子段的驱动。
            joint.targetRotation = Quaternion.Inverse(basis) * Quaternion.Inverse(desiredLocal) * bone.StartLocalRotation * basis;
        }

        public BalanceSample CaptureBalance()
        {
            Cache(); float mass = 0; Vector3 center = Vector3.zero, velocity = Vector3.zero;
            foreach (var bone in Bones) { mass += bone.Body.mass; center += bone.Body.worldCenterOfMass * bone.Body.mass; velocity += bone.Body.velocity * bone.Body.mass; }
            center /= mass; velocity /= mass;
            Vector3 local = transform.InverseTransformPoint(center), speed = transform.InverseTransformDirection(velocity);
            var sample = new BalanceSample { Center = new Vector2(local.x, local.z), Velocity = new Vector2(speed.x, speed.z), Height = local.y };
            sample.Predicted = sample.Center + sample.Velocity / Mathf.Sqrt(9.81f / Mathf.Max(0.4f, local.y));
            sample.RequiredSupport = sample.Center + filteredSupportShift;
            foreach (var foot in feet)
            {
                Vector3 difference = foot.Body.position - foot.StartPosition; difference.y = 0;
                sample.FootDisplacement = Mathf.Max(sample.FootDisplacement, difference.magnitude);
            }
            sample.Finite = !(float.IsNaN(center.sqrMagnitude) || float.IsInfinity(center.sqrMagnitude)
                || float.IsNaN(velocity.sqrMagnitude) || float.IsInfinity(velocity.sqrMagnitude));
            return sample;
        }
        public PoseSample CapturePose()
        {
            Cache(); float angular = 0, mass = 0;
            foreach (var bone in Bones) { angular += bone.Body.angularVelocity.sqrMagnitude * bone.Body.mass; mass += bone.Body.mass; }
            return new PoseSample { Head = named["Head"].Body.position, LeftPalm = palms[0].Body.position,
                RightPalm = palms[1].Body.position, MeanAngularSpeedSquared = angular / mass };
        }
        public bool AssessStep(float dt)
        {
            if (Stepping) return true;
            UpdateContactSupport(dt);
            var sample = CaptureBalance();
            bool outside = sample.RequiredSupport.x < supportMin.x || sample.RequiredSupport.x > supportMax.x
                || sample.RequiredSupport.y < supportMin.y || sample.RequiredSupport.y > supportMax.y
                || sample.Predicted.x < supportMin.x || sample.Predicted.x > supportMax.x
                || sample.Predicted.y < supportMin.y || sample.Predicted.y > supportMax.y
                || sample.FootDisplacement > Config.FootStepDistance || Pelvis.position.y - transform.position.y < 0.62f;
            unstable = outside ? unstable + dt : Mathf.Max(0, unstable - dt * 2);
            if (unstable >= Config.StepDelay) Stepping = true;
            return Stepping;
        }
        private void UpdateContactSupport(float dt)
        {
            if (dt <= 0) return;
            Vector3 force = (opponent != null ? opponent.ActivePushForce : Vector3.zero) - ActivePushForce;
            Vector3 moment = (opponent != null ? opponent.activePressureMoment : Vector3.zero) - activePressureMoment;
            float mass = 0;
            foreach (var bone in Bones)
            {
                mass += bone.Body.mass;
                var sensor = bone.Body.GetComponent<PhysicalContact>();
                force += sensor.Impulse / dt; moment += sensor.ImpulseMoment / dt;
            }
            moment = transform.InverseTransformDirection(moment - Vector3.Cross(transform.position, force));
            float normal = Mathf.Max(mass * 9.81f * 0.5f, mass * 9.81f - force.y);
            // 接触力矩要求脚底压力中心相应移动；不能让世界空间站姿驱动提供无限地面支撑。
            Vector2 shift = new Vector2(-moment.z, moment.x) / normal;
            filteredSupportShift = Vector2.Lerp(filteredSupportShift, shift, 1 - Mathf.Exp(-dt / 0.08f));
        }
        public void PresentStep(float dt)
        {
            if (Frozen) return;
            if (!Stepping) { Drive(default, false, false, null, dt); return; }
            stepTime += dt;
            Cache();
            if (Falling) { PresentFall(dt); return; }
            if (stepSide < 0)
            {
                for (int i = 0; i < Bones.Length; i++) stepPose[i] = Bones[i].GoalRotation;
                var sample = CaptureBalance();
                var risk = sample.RequiredSupport - restCenter;
                if ((sample.Predicted - restCenter).sqrMagnitude > risk.sqrMagnitude) risk = sample.Predicted - restCenter;
                stepSide = Mathf.Abs(risk.x) > Mathf.Abs(risk.y) ? (risk.x < 0 ? 0 : 1) : (risk.y < 0 ? 1 : 0);
                Vector3 direction = transform.TransformDirection(new Vector3(risk.x, 0, risk.y).normalized);
                float distance = Config.ProtectiveStepDistance;
                if (Arena != null)
                {
                    // 落脚点预留迈步期间身体按已有速度继续前进的距离，不因平台边缘额外放大步幅。
                    Vector3 velocity = transform.TransformDirection(new Vector3(sample.Velocity.x, 0, sample.Velocity.y));
                    distance += Mathf.Min(Config.ProtectiveStepDistance, Mathf.Max(0, Vector3.Dot(velocity, direction)) * Config.StepSeconds);
                }
                stepFrom = feet[stepSide].Body.position; stepTo = stepFrom + direction * distance;
                foreach (var support in feet[stepSide].Body.GetComponents<ConfigurableJoint>())
                    support.xDrive = support.yDrive = support.zDrive = default;
            }
            for (int i = 0; i < Bones.Length; i++)
            {
                if (Arena == null) { SetGoal(Bones[i], Bones[i].StartRotation); continue; }
                // 保留提交保护步时的身体投入；只有真实落脚后才逐渐恢复直立。
                if (StepLanded) stepPose[i] = Quaternion.Slerp(stepPose[i], Bones[i].StartRotation, 1 - Mathf.Exp(-4 * dt));
                SetGoal(Bones[i], stepPose[i]);
            }
            float progress = Mathf.Clamp01(stepTime / Mathf.Max(0.1f, Config.StepSeconds));
            if (Arena != null && progress >= 1 && !Arena.HasPlatformBelow(feet[stepSide].Collider))
            { BeginFall(); PresentFall(dt); return; }
            Vector3 target = Vector3.Lerp(stepFrom, stepTo, Mathf.SmoothStep(0, 1, progress));
            target.y += Mathf.Sin(progress * Mathf.PI) * Config.StepLiftHeight;
            for (int side = 0; side < 2; side++)
            {
                Vector3 hip = thighs[side].Joint.connectedBody.transform.TransformPoint(thighs[side].Joint.connectedAnchor);
                Vector3 ankle = (side == stepSide ? target : feet[side].Body.position) + transform.up * 0.065f - transform.forward * 0.025f;
                Vector3 knee = Bend(hip, ankle, thighs[side].Body.transform.localScale.y, shins[side].Body.transform.localScale.y, transform.forward);
                SetSegmentGoal(thighs[side], hip, knee); SetSegmentGoal(shins[side], knee, ankle);
                SetGoal(feet[side], feet[side].StartRotation);
            }
            SupportStance(CaptureBalance(), Vector2.zero, 0, StepLanded ? -1 : stepSide); SupportPose();
            var lifted = feet[stepSide].Body;
            if (!StepLanded)
            {
                Vector3 liftForce = (target - lifted.position) * Config.FootSpring - lifted.velocity * Config.FootDamper
                    + Vector3.up * (progress < 1 ? 110 : 0);
                lifted.AddForce(Vector3.ClampMagnitude(liftForce, Config.MaximumStepForce), ForceMode.Force);
                Vector3 error = lifted.position - stepTo; error.y = 0;
                if (progress >= 1 && error.magnitude < 0.06f && lifted.position.y <= stepFrom.y + 0.035f
                    && (Arena == null || Arena.HasPlatformContact(lifted.GetComponent<PhysicalContact>())))
                {
                    StepLanded = true;
                    landingTime = stepTime;
                    var support = lifted.GetComponents<ConfigurableJoint>()[1];
                    support.connectedAnchor = lifted.position;
                    var drive = new JointDrive { positionSpring = Config.FootSpring, positionDamper = Config.FootDamper, maximumForce = Config.FootForce };
                    support.xDrive = support.zDrive = drive;
                    Vector3 travel = transform.InverseTransformDirection(lifted.position - stepFrom);
                    restCenter += new Vector2(travel.x, travel.z) * 0.5f;
                }
            }
            // 落脚后继续恢复身体，不能仅因原来的短计时结束就截断动作。
            if (StepLanded && stepTime - landingTime >= Mathf.Max(0.3f, Config.StepRecoverySeconds))
            { StepPresentationComplete = true; SetFrozen(true); }
            CheckPresentationTimeout();
        }
        private void BeginFall()
        {
            Falling = true; fallTime = groundLandingTime = 0;
            foreach (var foot in feet)
            {
                var support = foot.Body.GetComponents<ConfigurableJoint>()[1];
                support.xDrive = support.yDrive = support.zDrive = default;
            }
            // 离台后撤去世界姿态驱动与站姿辅助；身体保留实际速度及关节限位。
            int hiddenLayer = named["Head"].Body.gameObject.layer;
            for (int i = 0; i < Bones.Length; i++)
            {
                var bone = Bones[i]; standingLayers[i] = bone.Body.gameObject.layer;
                var drive = bone.Joint.slerpDrive;
                drive.positionSpring *= 0.08f; drive.positionDamper = 6; drive.maximumForce = bone.Parent == null ? 0 : 12;
                bone.Joint.slerpDrive = drive;
                // 倒地后视线保持水平，排除自己的肩胸遮挡；另一玩家仍看见完整身体。
                bone.Body.gameObject.layer = hiddenLayer;
            }
        }
        private void PresentFall(float dt)
        {
            fallTime += dt;
            if (!GroundLanded && Arena.HasLanded(this)) { GroundLanded = true; groundLandingTime = fallTime; }
            if (GroundLanded && fallTime - groundLandingTime >= Config.FallLandingSeconds)
            { StepPresentationComplete = true; SetFrozen(true); }
            CheckPresentationTimeout();
        }
        private void CheckPresentationTimeout()
        {
            if (StepPresentationComplete || stepTime < Config.ResultTimeoutSeconds) return;
            PresentationTimedOut = StepPresentationComplete = true; SetFrozen(true);
            Debug.LogWarning($"玩家{PlayerIndex + 1}结束动作超时，已开放重开；未将其记为真实落地。");
        }
        public void EndControl()
        {
            Effort[0] = Effort[1] = 0; Technique.Reset(); LearningCorrection = Vector2.zero;
            ActivePushForce = activePressureMoment = Vector3.zero; opponent = null;
            handPushForce[0] = handPushForce[1] = 0;
        }
        public void ReadHandLoad(int side, float dt, out bool touching, out float force)
        {
            Cache(); var palm = palmContacts[side]; var arm = forearmContacts[side];
            touching = palm.Count > 0 || arm.Count > 0;
            force = touching ? Mathf.Max(handPushForce[side], (palm.Impulse + arm.Impulse).magnitude / Mathf.Max(0.001f, dt)) : 0;
        }
        public void SetFeedbackView(Vector3 position, Vector3 angles)
        { feedbackPosition = position; feedbackAngles = angles; }
        public void SetFrozen(bool frozen)
        {
            if (Frozen == frozen) return;
            foreach (var bone in Bones)
            {
                if (bone.Body == null) continue; // 场景退出时身体段可能先于对局控制器销毁。
                if (frozen) { bone.SavedVelocity = bone.Body.velocity; bone.SavedAngularVelocity = bone.Body.angularVelocity; bone.Body.isKinematic = true; }
                else { bone.Body.isKinematic = false; bone.Body.velocity = bone.SavedVelocity; bone.Body.angularVelocity = bone.SavedAngularVelocity; bone.Body.WakeUp(); }
            }
            Frozen = frozen;
        }
        public void ResetPose()
        {
            Cache(); SetFrozen(true);
            for (int i = 0; i < Bones.Length; i++)
            {
                var bone = Bones[i]; bone.Joint.slerpDrive = standingDrives[i];
                bone.Body.gameObject.layer = standingLayers[i];
                bone.Body.position = bone.StartPosition; bone.Body.rotation = bone.StartRotation;
                bone.SavedVelocity = bone.SavedAngularVelocity = Vector3.zero; bone.Joint.targetRotation = Quaternion.identity;
                bone.GoalRotation = bone.StartRotation;
                bone.Body.GetComponent<PhysicalContact>().Clear();
            }
            foreach (var foot in feet)
            {
                var support = foot.Body.GetComponents<ConfigurableJoint>()[1];
                support.connectedAnchor = foot.StartPosition;
                var drive = new JointDrive { positionSpring = Config.FootSpring, positionDamper = Config.FootDamper, maximumForce = Config.FootForce };
                support.xDrive = support.zDrive = drive;
            }
            for (int i = 0; i < 2; i++) { PalmTargets[i] = palms[i].Body.position; Effort[i] = 0; }
            Technique.Reset(); Stepping = StepLanded = StepPresentationComplete = false; unstable = stepTime = landingTime = 0; stepSide = -1; look = Vector2.zero; LearningCorrection = Vector2.zero;
            Falling = GroundLanded = PresentationTimedOut = false; fallTime = groundLandingTime = 0;
            controlLean = bodyTilt = Vector2.zero; restCenter = CaptureBalance().Center;
            ActivePushForce = Vector3.zero;
            handPushForce[0] = handPushForce[1] = 0; feedbackPosition = feedbackAngles = lastFeedbackWorld = Vector3.zero;
            activePressureMoment = Vector3.zero; filteredSupportShift = Vector2.zero; opponent = null;
            restGravityTorque = Vector3.zero;
            foreach (var bone in Bones) restGravityTorque += GravityMoment(bone, Pelvis.position);
        }
        public void RelocateSpawn(Vector3 position)
        {
            Cache(); SetFrozen(true);
            Vector3 offset = position - transform.position; transform.position = position;
            foreach (var bone in Bones)
            {
                bone.StartPosition += offset;
                if (bone.Parent == null) bone.Joint.connectedAnchor += offset;
            }
            ResetPose(); RenderView(0);
        }
        public void Observe(Vector2 delta)
        { look.x = Mathf.Clamp(look.x + delta.x, -Config.YawLimit, Config.YawLimit); look.y = Mathf.Clamp(look.y - delta.y, -Config.PitchLimit, Config.PitchLimit); }
        public void RenderView(float dt)
        {
            Cache(); if (ViewCamera == null) return;
            Vector3 eye = named["Head"].Body.position + transform.forward * 0.095f - Vector3.up * 0.045f;
            if (Falling && Arena != null && Arena.LandingGround != null)
                eye.y = Mathf.Max(eye.y, Arena.LandingGround.bounds.max.y + 0.12f);
            ViewCamera.transform.position = dt <= 0 ? eye : Vector3.Lerp(ViewCamera.transform.position - lastFeedbackWorld, eye, 1 - Mathf.Exp(-18 * dt));
            var balance = CaptureBalance();
            lastFeedbackWorld = transform.TransformDirection(feedbackPosition);
            ViewCamera.transform.position += lastFeedbackWorld;
            ViewCamera.transform.rotation = transform.rotation * Quaternion.Euler(new Vector3(Config.ViewDownDegrees + look.y + Mathf.Clamp((balance.Center.y - restCenter.y) * 5, -2, 2), look.x, Mathf.Clamp(-balance.Center.x * 5, -2, 2)) + feedbackAngles);
        }
    }
}
