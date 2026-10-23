using UnityEngine;

namespace PushHands.Physical
{
    // 独立同名脚本，保证场景和预制体中的 MonoScript 引用可以持久化。
    public sealed class PhysicalContact : MonoBehaviour
    {
        public PhysicalFighter Owner;
        public ContactRegion Region;
        public Vector3 Impulse { get; private set; }
        public Vector3 ImpulseMoment { get; private set; }
        private Collider pushCollider;
        private PhysicalFighter pushOwner;
        private Vector3 pushPoint, pushNormal;
        private readonly System.Collections.Generic.HashSet<Collider> contacts = new System.Collections.Generic.HashSet<Collider>();
        private readonly System.Collections.Generic.HashSet<Collider> environment = new System.Collections.Generic.HashSet<Collider>();
        private readonly System.Collections.Generic.HashSet<Collider> supportSurfaces = new System.Collections.Generic.HashSet<Collider>();
        public int Count { get { contacts.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy); return contacts.Count; } }
        public void ResetImpulse() { Impulse = ImpulseMoment = Vector3.zero; }
        public void Clear() { contacts.Clear(); environment.Clear(); supportSurfaces.Clear(); pushCollider = null; ResetImpulse(); }
        public bool TouchesEnvironment(Collider surface) => surface != null && surface.enabled
            && surface.gameObject.activeInHierarchy && environment.Contains(surface);
        public bool TouchesSupport(Collider surface) => TouchesEnvironment(surface) && supportSurfaces.Contains(surface);
        public bool TryGetPushContact(PhysicalFighter opponent, Vector3 forward, out Rigidbody recipient, out Vector3 point, out Vector3 direction)
        {
            recipient = null; point = pushPoint; direction = -pushNormal;
            if (pushCollider == null || !pushCollider.enabled || !pushCollider.gameObject.activeInHierarchy || !contacts.Contains(pushCollider) || pushOwner != opponent
                || Vector3.Dot(direction, forward) < 0.3f) return false;
            recipient = pushCollider.attachedRigidbody; return recipient != null;
        }
        private void OnCollisionEnter(Collision collision) => Register(collision);
        private void OnCollisionStay(Collision collision) => Register(collision);
        private void OnCollisionExit(Collision collision)
        { contacts.Remove(collision.collider); environment.Remove(collision.collider); supportSurfaces.Remove(collision.collider); if (pushCollider == collision.collider) pushCollider = null; }
        private void Register(Collision collision)
        {
            var other = collision.collider.GetComponent<PhysicalContact>();
            if (other == null)
            {
                environment.Add(collision.collider); supportSurfaces.Remove(collision.collider);
                for (int i = 0; i < collision.contactCount; i++)
                    if (collision.GetContact(i).normal.y > 0.5f) { supportSurfaces.Add(collision.collider); break; }
            }
            if (other != null && other.Owner != Owner && ContactRules.IsLegal(Region, other.Region))
            {
                contacts.Add(collision.collider); Impulse += collision.impulse;
                if (collision.contactCount > 0) ImpulseMoment += Vector3.Cross(collision.GetContact(0).point, collision.impulse);
                for (int i = 0; i < collision.contactCount; i++)
                {
                    var contact = collision.GetContact(i);
                    if (Vector3.Dot(-contact.normal, Owner.transform.forward) < 0.3f) continue;
                    pushCollider = collision.collider; pushOwner = other.Owner; pushPoint = contact.point; pushNormal = contact.normal;
                    break;
                }
            }
        }
    }
}
