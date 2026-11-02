using System;
using OnlineActionRpg.Client.Battle;
using UnityEngine;

namespace OnlineActionRpg.Client.Combat
{
    // 攻击起手时把冲量与朝向吸附到锁定目标。
    // 只提供“方向建议”，是否采纳由 LocalPlayerController 决定。
    public sealed class AttackHoming : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerTargetLock targetLock;
        [SerializeField] private LocalPlayerController playerController;

        [Header("Homing")]
        [Tooltip("目标超出角色正前方这个角度就不吸附，避免变成自动瞄准。")]
        [SerializeField] private float maxHomingAngle = 60f;

        private Func<Vector3?> _resolver;

        private void Awake()
        {
            if (playerController == null)
            {
                playerController = GetComponent<LocalPlayerController>();
            }

            if (targetLock == null)
            {
                targetLock = GetComponent<PlayerTargetLock>();
            }
        }

        private void OnEnable()
        {
            _resolver = ResolveFacingDirection;

            if (playerController != null)
            {
                playerController.AttackFacingResolver = _resolver;
                //用委托的方式将方法传递，避免直接引用本组件，导致 OnDisable 时无法清空。
            }
        }

        private void OnDisable()
        {
            // 只摘自己挂上去的那个委托，避免误清掉别人的。
            if (playerController != null && playerController.AttackFacingResolver == _resolver)
            {
                playerController.AttackFacingResolver = null;
            }
        }

        //方向吸附逻辑：如果锁定目标在角色正前方 maxHomingAngle 以内，则返回指向目标的方向，否则返回 null。
        private Vector3? ResolveFacingDirection()
        {
            CombatTarget target = targetLock != null ? targetLock.CurrentTarget : null;

            if (target == null)
            {
                return null;
            }

            Vector3 toTarget = target.LockPosition - transform.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude <= 0.0001f)
            {
                return null;
            }

            Vector3 direction = toTarget.normalized;
            float angle = Vector3.Angle(transform.forward, direction);

            if (angle > maxHomingAngle)
            {
                return null;
            }

            return direction;
        }
    }
}
