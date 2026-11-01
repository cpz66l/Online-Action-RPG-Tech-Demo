using UnityEngine;

namespace OnlineActionRpg.Client.Combat
{
    // 无状态选目标：距离越近分越低，偏离正前方越多分越高。
    // 打分而不是“只取最近”，是为了避免身侧和背后的目标被锁得很别扭。
    public static class TargetSelector
    {
        // 角度惩罚折算成等效米数：正后方 180° 相当于多出这么多米。
        public const float DefaultAnglePenaltyDistance = 6f;

        public static CombatTarget SelectBest(
            Vector3 origin,
            Vector3 forward,
            float maxDistance,
            LayerMask mask,
            CombatTarget.Faction targetFaction,
            Transform ignoreRoot,
            float anglePenaltyDistance = DefaultAnglePenaltyDistance)
        {
            Collider[] overlaps = Physics.OverlapSphere(
                origin,
                maxDistance,
                mask,
                QueryTriggerInteraction.Collide);

            CombatTarget best = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < overlaps.Length; i++)
            {
                CombatTarget candidate = overlaps[i].GetComponentInParent<CombatTarget>();

                if (candidate == null || !candidate.IsTargetable)
                {
                    continue;
                }

                if (candidate.FactionType != targetFaction)
                {
                    continue;
                }

                // 同一个目标可能有多个 Collider，靠“分数更优才替换”天然去重。
                if (ignoreRoot != null && candidate.transform.IsChildOf(ignoreRoot))
                {
                    continue;
                }

                Vector3 toTarget = candidate.LockPosition - origin;
                Vector3 flatToTarget = new Vector3(toTarget.x, 0f, toTarget.z);

                float distance = toTarget.magnitude;

                if (distance > maxDistance || flatToTarget.sqrMagnitude <= 0.0001f)
                {
                    continue;
                }

                float angle = Vector3.Angle(forward, flatToTarget);
                float score = distance + angle / 180f * anglePenaltyDistance;


                if (score >= bestScore)
                {
                    continue;
                }

                bestScore = score;
                best = candidate;
            }

            return best;
        }
    }
}
