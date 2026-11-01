using UnityEngine;

namespace OnlineActionRpg.Client.Combat
{
    // “这里可以被锁定”。
    // 与 IDamageable 分开：能被打和能被锁定是两个独立属性。
    // 木桩两者都是；可破坏道具只“能被打”；友方 NPC 可能“能被锁定但不能打”。
    public sealed class CombatTarget : MonoBehaviour
    {
        public enum Faction
        {
            Player,
            Enemy,
            Neutral
        }

        [Header("Identity")]
        [SerializeField] private Faction faction = Faction.Enemy;
        [SerializeField] private bool targetable = true;

        [Header("Lock Point")]
        [Tooltip("锁定与吸附的参考点；留空时取自身位置加高度偏移。")]
        [SerializeField] private Transform lockPoint;
        [SerializeField] private float fallbackLockHeight = 1.2f;

        private IDamageable _damageable;
        private bool _resolved;// 是否已解析过 IDamageable，避免重复 GetComponent

        public Faction FactionType => faction;

        // 没有 IDamageable 的对象视为“可锁定但不可打”，例如友方 NPC。
        public bool IsTargetable
        {
            get
            {
                if (!targetable)
                {
                    return false;
                }

                ResolveDamageable();
                return _damageable == null || _damageable.IsAlive;
            }
        }

        public Vector3 LockPosition =>
            lockPoint != null
                ? lockPoint.position
                : transform.position + Vector3.up * fallbackLockHeight;

        private void Awake()
        {
            ResolveDamageable();
        }

        private void ResolveDamageable()
        {
            if (_resolved)
            {
                return;
            }

            _damageable = GetComponentInParent<IDamageable>();
            _resolved = true;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = IsTargetable
                ? new Color(1f, 0.6f, 0.1f, 0.9f)// 黄色表示可锁定
                : new Color(0.5f, 0.5f, 0.5f, 0.5f);// 灰色表示不可锁定

            Gizmos.DrawWireSphere(LockPosition, 0.18f);// 绘制锁定点的线框球体
        }
    }
}
