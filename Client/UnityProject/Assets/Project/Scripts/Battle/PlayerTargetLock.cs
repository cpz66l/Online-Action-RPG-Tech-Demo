using System;
using OnlineActionRpg.Client.Combat;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnlineActionRpg.Client.Battle
{
    // 中键切换锁定：没有目标时选一个，已有目标时取消。
    // 锁定后目标会“粘住”，直到手动取消、死亡、超距或被销毁。
    public sealed class PlayerTargetLock : MonoBehaviour
    {
        [Header("Selection")]
        [SerializeField] private float maxDistance = 20f;
        [SerializeField] private LayerMask targetMask = ~0;
        [SerializeField] private CombatTarget.Faction targetFaction = CombatTarget.Faction.Enemy;
        [SerializeField] private float anglePenaltyDistance = TargetSelector.DefaultAnglePenaltyDistance;

        [Header("Release")]
        [Tooltip("解除锁定的距离比锁定距离更远，避免在边界反复锁上又解开。")]
        [SerializeField] private float releaseDistanceBuffer = 4f;

        private Transform _cameraTransform;

        public CombatTarget CurrentTarget { get; private set; }

        // 吸附和锁定相机会订阅它。
        public event Action<CombatTarget> TargetChanged;

        private void Awake()
        {
            if (_cameraTransform == null && Camera.main != null)
            {
                _cameraTransform = Camera.main.transform;
            }
        }

        public void SetCameraTransform(Transform cameraTransform)
        {
            _cameraTransform = cameraTransform;
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;

            if (mouse != null && mouse.middleButton.wasPressedThisFrame)
            {
                ToggleTarget();
            }

            ValidateCurrentTarget();
        }

        private void ToggleTarget()
        {
            if (CurrentTarget != null)
            {
                ClearTarget("manual");
                return;
            }

            CombatTarget selected = SelectTarget();

            if (selected == null)
            {
                Debug.Log("[TargetLock] Selected nothing.");
                return;
            }

            SetTarget(selected);
        }

        private void ValidateCurrentTarget()
        {
            // Unity 重载了 ==，已销毁的对象会被判定为 null。
            // 这里用 ReferenceEquals 区分“本来就是空”和“刚被销毁”，
            // 否则目标销毁时不会广播取消事件。
            bool destroyed = !ReferenceEquals(CurrentTarget, null) && CurrentTarget == null;

            if (destroyed)
            {
                ClearTarget("destroyed");
                return;
            }

            if (CurrentTarget == null)
            {
                return;
            }

            if (!CurrentTarget.IsTargetable)
            {
                ClearTarget("not targetable");
                return;
            }

            float distance = (CurrentTarget.LockPosition - transform.position).magnitude;

            if (distance > maxDistance + releaseDistanceBuffer)
            {
                ClearTarget($"out of range ({distance:0.0}m)");
            }
        }

        private void SetTarget(CombatTarget target)
        {
            if (ReferenceEquals(CurrentTarget, target))
            {
                return;
            }

            CurrentTarget = target;
            TargetChanged?.Invoke(target);

            if (target == null)
            {
                return;
            }

            Vector3 toTarget = target.LockPosition - transform.position;
            Vector3 flatToTarget = new Vector3(toTarget.x, 0f, toTarget.z);

            float distance = toTarget.magnitude;
            float angle = Vector3.Angle(ResolveSelectionForward(), flatToTarget);
            float score = distance + angle / 180f * anglePenaltyDistance;

            Debug.Log($"[TargetLock] Locked {target.name}  " +
                      $"dist={distance:0.0}  angle={angle:0}°  score={score:0.00}");
        }

        private void ClearTarget(string reason)
        {
            if (CurrentTarget == null)
            {
                return;
            }

            CurrentTarget = null;
            TargetChanged?.Invoke(null);
            Debug.Log($"[TargetLock] Unlocked ({reason}).");
        }

        private CombatTarget SelectTarget()
        {
            return TargetSelector.SelectBest(
                transform.position,
                ResolveSelectionForward(),
                maxDistance,
                targetMask,
                targetFaction,
                transform,
                anglePenaltyDistance);
        }

        //前方向解析
        private Vector3 ResolveSelectionForward()
        {
            Vector3 forward = _cameraTransform != null ? _cameraTransform.forward : transform.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude <= 0.0001f)
            {
                forward = transform.forward;
                forward.y = 0f;
            }

            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }
    }
}
