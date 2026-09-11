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
    }
}
