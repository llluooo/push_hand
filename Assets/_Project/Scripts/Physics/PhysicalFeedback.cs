using UnityEngine;
using UnityEngine.InputSystem;

namespace PushHands.Physical
{
    // 对局拥有表现资源及设备输出；固定步解释接触，画面步只平滑镜头和输出。
    public sealed class PhysicalFeedback : MonoBehaviour
    {
        private PhysicalMatch match;
        private readonly ContactFeedbackState[,] hands = { { new ContactFeedbackState(), new ContactFeedbackState() },
            { new ContactFeedbackState(), new ContactFeedbackState() } };
        private readonly AudioSource[] pressureAudio = new AudioSource[2], transientAudio = new AudioSource[2];
        private readonly Gamepad[] drivenPads = new Gamepad[2];
        private readonly bool[] landed = new bool[2];
        private readonly bool[] grounded = new bool[2];
        private readonly float[] landingImpact = new float[2];
        private readonly Vector3[] viewOffset = new Vector3[2], viewAngles = new Vector3[2];
        private AudioClip contactClip, pressureClip, footClip, fallClip;
        public ContactFeedbackState Hand(int player, int side) => hands[player, side];
        public float LowMotor(int player) { return Motor(player, false); }
        public float HighMotor(int player) { return Motor(player, true); }
        public void Initialize(PhysicalMatch owner)
        {
            if (match != null) return;
            match = owner;
            contactClip = CreateClip("掌臂接触", 0.12f, 0);
            pressureClip = CreateClip("衣物与掌臂持续摩擦", 0.8f, 1);
            footClip = CreateClip("保护步落脚", 0.2f, 2);
            fallClip = CreateClip("失足摔落接地", 0.28f, 3);
            for (int i = 0; i < 2; i++)
            {
                var obj = new GameObject("Player" + (i + 1) + "Feedback"); obj.transform.SetParent(transform, false);
                pressureAudio[i] = obj.AddComponent<AudioSource>(); pressureAudio[i].playOnAwake = false;
                pressureAudio[i].loop = true; pressureAudio[i].clip = pressureClip;
                transientAudio[i] = obj.AddComponent<AudioSource>(); transientAudio[i].playOnAwake = false;
                // 同机共用音频设备，以轻微声像区分两个视口；每次接触的双方声音合计限制音量。
                pressureAudio[i].panStereo = transientAudio[i].panStereo = i == 0 ? -0.35f : 0.35f;
                pressureAudio[i].spatialBlend = transientAudio[i].spatialBlend = 0;
            }
        }
        public void Sample(float dt, bool fighting)
        {
            if (match == null || !isActiveAndEnabled) return;
            for (int player = 0; player < 2; player++)
            {
                float pulse = 0;
                for (int side = 0; side < 2; side++)
                {
                    match.Fighters[player].ReadHandLoad(side, dt, out bool touching, out float force);
                    hands[player, side].Tick(fighting && touching, force, dt, match.Config);
                    if (hands[player, side].ContactPulse) pulse = Mathf.Max(pulse, hands[player, side].Impact);
                }
                if (pulse > 0) transientAudio[player].PlayOneShot(contactClip, pulse * match.Config.FeedbackVolume * 0.45f);
                bool fell = match.Fighters[player].GroundLanded;
                bool footDown = match.Fighters[player].StepLanded;
                // 搭沿落脚后仍可能滑落，真实摔落接地是独立事件。
                if ((fell && !grounded[player]) || (footDown && !landed[player]))
                {
                    transientAudio[player].PlayOneShot(fell ? fallClip : footClip, match.Config.FeedbackVolume * (fell ? 0.8f : 0.65f));
                    landingImpact[player] = fell ? 0.7f : 0.45f;
                }
                landed[player] = footDown;
                grounded[player] = fell;
                landingImpact[player] *= Mathf.Exp(-12 * dt);
            }
        }
        public void Render(float dt)
        {
            if (match == null || !isActiveAndEnabled) return;
            float blend = 1 - Mathf.Exp(-24 * Mathf.Max(0, dt));
            for (int player = 0; player < 2; player++)
            {
                float left = hands[player, 0].Pressure, right = hands[player, 1].Pressure;
                float impact = Mathf.Clamp01(Mathf.Max(hands[player, 0].Impact, hands[player, 1].Impact) + landingImpact[player]);
                float meters = Mathf.Clamp(match.Config.CameraKickMeters, 0, 0.025f);
                float degrees = Mathf.Clamp(match.Config.CameraKickDegrees, 0, 1.5f);
                Vector3 position = new Vector3((right - left) * meters * 0.2f, -landingImpact[player] * meters, -impact * meters);
                Vector3 angles = new Vector3(impact * degrees, 0, (left - right) * degrees * 0.3f);
                viewOffset[player] = Vector3.Lerp(viewOffset[player], position, blend);
                viewAngles[player] = Vector3.Lerp(viewAngles[player], angles, blend);
                match.Fighters[player].SetFeedbackView(viewOffset[player], viewAngles[player]);
                float pressure = Mathf.Clamp01((left + right) * 0.5f);
                pressureAudio[player].volume = pressure * match.Config.FeedbackVolume * 0.22f;
                pressureAudio[player].pitch = 0.85f + pressure * 0.25f;
                if (pressure > 0.012f) { if (!pressureAudio[player].isPlaying) pressureAudio[player].Play(); }
                else pressureAudio[player].Stop();
                var pad = match.Input.Roster.KeyboardDebug ? null : match.Input.Roster.Device(player);
                if (drivenPads[player] != pad) { StopPad(drivenPads[player]); drivenPads[player] = pad; }
                if (pad != null && pad.added && pad.enabled) pad.SetMotorSpeeds(LowMotor(player), HighMotor(player));
            }
        }
        private float Motor(int player, bool high)
        {
            if (match == null) return 0;
            float pressure = (hands[player, 0].Pressure + hands[player, 1].Pressure) * 0.5f;
            float impact = Mathf.Clamp01(Mathf.Max(hands[player, 0].Impact, hands[player, 1].Impact) + landingImpact[player]);
            return Mathf.Clamp01((high ? impact * 0.8f + pressure * 0.08f : pressure * 0.55f + impact * 0.3f)
                * Mathf.Clamp01(match.Config.HapticStrength));
        }
        private static void StopPad(Gamepad pad)
        { if (pad != null && pad.added) pad.SetMotorSpeeds(0, 0); }
        public void Clear()
        {
            for (int player = 0; player < 2; player++)
            {
                hands[player, 0].Clear(); hands[player, 1].Clear();
                StopPad(drivenPads[player]); drivenPads[player] = null;
                if (pressureAudio[player] != null) pressureAudio[player].Stop();
                if (transientAudio[player] != null) transientAudio[player].Stop();
                viewOffset[player] = viewAngles[player] = Vector3.zero; landingImpact[player] = 0;
                landed[player] = match != null && match.Fighters[player] != null && match.Fighters[player].StepLanded;
                grounded[player] = match != null && match.Fighters[player] != null && match.Fighters[player].GroundLanded;
                if (match != null && match.Fighters[player] != null) match.Fighters[player].SetFeedbackView(Vector3.zero, Vector3.zero);
            }
        }
        private void OnDisable() => Clear();
        private void OnDestroy()
        {
            Clear();
            if (contactClip != null) Destroy(contactClip);
            if (pressureClip != null) Destroy(pressureClip);
            if (footClip != null) Destroy(footClip);
            if (fallClip != null) Destroy(fallClip);
        }
        private static AudioClip CreateClip(string name, float seconds, int kind)
        {
            const int rate = 22050; int size = Mathf.RoundToInt(rate * seconds);
            var samples = new float[size]; uint random = 7391; float filtered = 0;
            for (int i = 0; i < size; i++)
            {
                random = random * 1664525 + 1013904223;
                float noise = (random / (float)uint.MaxValue) * 2 - 1;
                filtered = Mathf.Lerp(filtered, noise, kind == 1 ? 0.12f : 0.25f);
                float t = i / (float)rate;
                float envelope = kind == 1 ? 0.5f : Mathf.Sin(Mathf.Clamp01(t / 0.006f) * Mathf.PI * 0.5f) * Mathf.Exp(-t * (kind == 0 ? 38 : 24));
                // 循环声两端收敛至零，避免接缝爆音；原型音色可确定性复现。
                if (kind == 1) envelope *= Mathf.Sin(Mathf.PI * i / (size - 1));
                float body = kind == 1 ? 0 : Mathf.Sin(2 * Mathf.PI * (kind == 0 ? 180 : kind == 3 ? 58 : 85) * t) * 0.45f;
                samples[i] = Mathf.Clamp((filtered * 0.7f + body) * envelope, -1, 1);
            }
            var clip = AudioClip.Create(name, size, 1, rate, false); clip.SetData(samples, 0); return clip;
        }
    }
}
