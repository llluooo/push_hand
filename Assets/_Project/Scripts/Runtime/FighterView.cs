using UnityEngine;

namespace PushHands
{
    public sealed class FighterView : MonoBehaviour
    {
        public Transform Torso, Head;
        public Transform[] Feet, UpperLegs, LowerLegs, UpperArms, LowerArms, Palms;
        public Camera ViewCamera;
        private Vector2 look;
        public void ResetLook() { look = Vector2.zero; }
        public void Observe(Vector2 delta, PrototypeConfig config)
        {
            look.x = Mathf.Clamp(look.x + delta.x, -config.YawLimit, config.YawLimit);
            look.y = Mathf.Clamp(look.y - delta.y, -config.PitchLimit, config.PitchLimit);
        }
        public void Render(FighterState state, FighterState opponent, FighterView other, PrototypeConfig config)
        {
            Vector3 displacement = new Vector3(state.Center.x, 0, state.Center.y);
            float step = state.Stepping ? Mathf.SmoothStep(0, 1, state.StepProgress) : 0;
            Vector3 stepOffset = new Vector3(state.StepDirection.x, 0, state.StepDirection.y) * step * 0.40f;
            Vector3 hips = new Vector3(0, 0.91f, 0) + displacement + stepOffset * 0.65f;
            Torso.localPosition = hips + new Vector3(0, 0.35f, 0);
            Torso.localRotation = Quaternion.Euler(Mathf.Clamp(state.Center.y * 75, -25, 25), 0, Mathf.Clamp(-state.Center.x * 70, -20, 20));
            Head.localPosition = new Vector3(0, 1.67f, 0) + displacement * 0.85f + stepOffset * 0.65f;
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1 : 1;
                Vector3 foot = new Vector3(sign * 0.14f, 0.06f, side == 0 ? 0.06f : -0.06f);
                // 迈向失衡方向的一只脚抬起，落点变化只服从已提交的迈步状态。
                int steppingFoot = state.StepDirection.x < 0 ? 0 : 1;
                if (side == steppingFoot) foot += stepOffset + Vector3.up * Mathf.Sin(step * Mathf.PI) * 0.12f;
                Feet[side].localPosition = foot;
                Vector3 hip = hips + Vector3.right * sign * 0.13f;
                Vector3 knee = (hip + foot) * 0.5f + new Vector3(0, -0.03f, 0.12f);
                Segment(UpperLegs[side], hip, knee, 0.105f);
                Segment(LowerLegs[side], knee, foot + Vector3.up * 0.06f, 0.085f);
                HandState hand = side == 0 ? state.Left : state.Right;
                Vector3 shoulder = new Vector3(sign * 0.24f, 1.43f, 0.04f) + displacement * 0.85f + stepOffset * 0.65f;
                Vector3 rest = new Vector3(sign * 0.22f, 1.10f, 0.24f) + displacement * 0.7f + stepOffset * 0.65f;
                float otherStep = opponent.Stepping ? Mathf.SmoothStep(0, 1, opponent.StepProgress) : 0;
                Vector3 otherOffset = new Vector3(opponent.StepDirection.x, 0, opponent.StepDirection.y) * otherStep * 0.26f;
                Vector3 targetWorld = other.transform.TransformPoint(new Vector3(-sign * 0.16f + opponent.Center.x, 1.36f,
                    config.TorsoRadius + opponent.Center.y) + otherOffset);
                Vector3 target = transform.InverseTransformPoint(targetWorld);
                Vector3 direction = (target - shoulder).normalized;
                // Extension 表示最大伸手距离的比例；与模拟层采用同一接触距离，不再直接当作到目标的插值比例。
                float contactDistance = config.PlayerSpacing - config.TorsoRadius - state.Center.y - opponent.Center.y;
                float progress = Mathf.Clamp01(config.Reach * hand.Extension / Mathf.Max(0.01f, contactDistance));
                Vector3 palm = Vector3.Lerp(rest, target, progress);
                Vector3 elbow = Vector3.Lerp(shoulder, palm, 0.5f) + new Vector3(sign * 0.10f, -0.13f, -0.04f) * (1 - hand.Extension * 0.65f);
                Segment(UpperArms[side], shoulder, elbow, 0.067f);
                Segment(LowerArms[side], elbow, palm, 0.058f);
                Palms[side].localPosition = palm;
                Palms[side].localRotation = Quaternion.LookRotation(direction);
            }
            ViewCamera.transform.localPosition = new Vector3(0, 1.61f, 0.08f) + displacement * 0.65f + stepOffset * 0.65f;
            ViewCamera.transform.localRotation = Quaternion.Euler(look.y + Mathf.Clamp(state.Center.y * 8, -3, 3), look.x,
                Mathf.Clamp(-state.Center.x * 9, -3, 3));
        }
        private static void Segment(Transform part, Vector3 a, Vector3 b, float radius)
        {
            part.localPosition = (a + b) * 0.5f;
            part.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
            part.localScale = new Vector3(radius * 2, Vector3.Distance(a, b) * 0.5f, radius * 2);
        }
    }
}
