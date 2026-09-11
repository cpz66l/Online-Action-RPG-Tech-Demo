using UnityEngine;

namespace OnlineActionRpg.Client.Combat
{
    // 一次命中的事实数据。
    // 只描述“谁、用什么攻击、打在哪、造成多少伤害”，不含任何表现逻辑。
    public readonly struct HitInfo
    {
        public readonly float Damage;
        public readonly string AttackerId;
        public readonly Transform AttackerRoot;
        public readonly string AttackId;
        public readonly Vector3 HitPoint;
        public readonly Vector3 HitDirection;

        public bool IsValid => Damage > 0f;

        public HitInfo(
            float damage,
            string attackerId,
            Transform attackerRoot,
            string attackId,
            Vector3 hitPoint,
            Vector3 hitDirection)
        {
            Damage = damage;
            AttackerId = attackerId ?? string.Empty;
            AttackerRoot = attackerRoot;
            AttackId = attackId ?? string.Empty;
            HitPoint = hitPoint;
            HitDirection = hitDirection;
        }

        //重写 ToString 方法，方便调试输出
        public override string ToString()
        {
            return $"Damage={Damage:0.##}, Attacker={AttackerId}, Attack={AttackId}, " +
                   $"HitPoint=({HitPoint.x:F2}, {HitPoint.y:F2}, {HitPoint.z:F2})";
        }
    }
}