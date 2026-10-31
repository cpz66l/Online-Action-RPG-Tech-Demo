using UnityEngine;

namespace OnlineActionRpg.Client.Combat
{
    // HurtBox 表达“这个位置可以被打中”。
    // 它只把命中转发给受击方，不决定伤害数值，也不做死亡判定，
    // 保持受击规则集中在受击方自己身上。
    public sealed class HurtBox : MonoBehaviour
    {
        // 留一个显式引用位，方便在 Inspector 里直接指定受击箱体对应的受击方；
        // 不指定时按“本物体 -> 父节点”顺序查找 IDamageable。
        [SerializeField] private MonoBehaviour damageableSource;

        private IDamageable _damageable;

        //外部访问接口，获取受击方的 IDamageable 实例
        public IDamageable Damageable
        {
            get
            {
                if (_damageable == null)
                {
                    ResolveDamageable();
                }

                return _damageable;
            }
        }

        // HurtBox 自身通常挂在子物体上，name 只能得到 "HurtBox" 这类子对象名。
        // 这里返回真正受击方（IDamageable 实现者）的对象名，用于日志与调试显示。
        public string OwnerName
        {
            get
            {
                IDamageable target = Damageable;
                return target is Component component ? component.name : name;
            }
        }

        private void Awake()
        {
            ResolveDamageable();
        }

        private void ResolveDamageable()
        {
            //如果没有指定damageableSource，则尝试在父节点中查找IDamageable组件
            _damageable = damageableSource != null
                ? damageableSource as IDamageable
                : GetComponentInParent<IDamageable>();

            if (_damageable == null)
            {
                Debug.LogWarning(
                    $"HurtBox on {name} cannot resolve an IDamageable. " +
                    "Assign damageableSource, or add a damageable component on this object or its parents.");
            }
        }

        // 返回 true 表示这次命中被受击方接收，HitBox 用它作为“是否打中”的判定。
        public bool TryReceiveHit(in HitInfo hit)
        {
            IDamageable target = Damageable;

            if (target == null || !target.IsAlive)
            {
                return false;
            }

            target.ApplyDamage(hit);
            return true;
        }

        //调试可视化，可在 Inspector 中关闭
        [SerializeField] private bool drawDebug = true;
        private static readonly Color HurtBoxColor = new Color(0.2f, 0.75f, 1f, 0.9f);//淡蓝色

        private void OnDrawGizmos()
        {
            if (!drawDebug)
            {
                return;
            }

            Collider target = GetComponent<Collider>();

            if (target == null)
            {
                target = GetComponentInChildren<Collider>();
            }

            if (target == null)
            {
                return;
            }

            Bounds bounds = target.bounds;

            if (bounds.size.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Color fill = HurtBoxColor;
            fill.a = 0.10f;
            Gizmos.color = fill;
            Gizmos.DrawCube(bounds.center, bounds.size);//画出半透明立方体

            Gizmos.color = HurtBoxColor;
            Gizmos.DrawWireCube(bounds.center, bounds.size);//画出线框立方体
        }

    }
}
