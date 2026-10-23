using UnityEngine;

namespace PushHands.Physical
{
    // 场地只提供真实几何信息；迈步判负仍由人偶和对局规则决定。
    public sealed class PhysicalArena : MonoBehaviour
    {
        public BoxCollider[] Platforms;
        public Collider LandingGround;
        // 相对平台旋转的米制出生偏移，不随台面尺寸缩放。
        public Vector3[] SpawnOffsets = new Vector3[2];

        public bool HasPlatformSupport(Vector3 footCenter)
        {
            if (Platforms == null) return false;
            var ray = new Ray(footCenter + Vector3.up * 0.10f, Vector3.down);
            foreach (var platform in Platforms)
                if (platform != null && platform.enabled && platform.gameObject.activeInHierarchy
                    && platform.Raycast(ray, out var hit, 0.24f) && hit.normal.y > 0.7f) return true;
            return false;
        }
        public bool HasLanded(PhysicalFighter fighter)
        {
            foreach (var bone in fighter.Bones)
            {
                if (bone.Region != ContactRegion.Torso && bone.Region != ContactRegion.Head) continue;
                var contact = bone.Body.GetComponent<PhysicalContact>();
                if (contact.TouchesEnvironment(LandingGround)) return true;
                // 摔倒后也可能搭在台面上；仅承认向上的真实承托，擦碰台侧不算落地。
                if (Platforms != null)
                    foreach (var platform in Platforms) if (contact.TouchesSupport(platform)) return true;
            }
            return false;
        }
        public bool HasPlatformBelow(Collider foot)
        {
            if (foot == null || Platforms == null) return false;
            var sole = foot.bounds;
            foreach (var platform in Platforms)
            {
                if (platform == null || !platform.enabled || !platform.gameObject.activeInHierarchy) continue;
                var top = platform.bounds;
                // 水平台的足底覆盖范围：脚中心越沿不等于脚掌已经完全踩空。
                if (sole.max.y >= top.max.y - 0.025f && sole.min.x < top.max.x && sole.max.x > top.min.x
                    && sole.min.z < top.max.z && sole.max.z > top.min.z) return true;
            }
            return false;
        }
        public bool HasPlatformContact(PhysicalContact foot)
        {
            if (Platforms == null) return false;
            foreach (var platform in Platforms) if (foot.TouchesSupport(platform)) return true;
            return false;
        }
        public Vector3 SpawnPosition(int player)
        {
            var platform = Platforms[player];
            Vector3 offset = SpawnOffsets != null && player < SpawnOffsets.Length ? SpawnOffsets[player] : Vector3.zero;
            return new Vector3(platform.bounds.center.x, platform.bounds.max.y + 0.01f, platform.bounds.center.z)
                + platform.transform.rotation * offset;
        }
    }
}
