using System;
using OnlineActionRpg.Client.Combat;
using UnityEngine;

namespace OnlineActionRpg.Client.Battle
{
    // 锁定态镜头：把相机 yaw 平滑拉到“玩家 -> 目标”的水平方向。
    // 只接管左右朝向，pitch 和位置仍由 ThirdPersonCameraController 自己处理。
    public sealed class LockOnCamera : MonoBehaviour
    {
        [SerializeField] private ThirdPersonCameraController cameraController;
        [SerializeField] private PlayerTargetLock targetLock;

        private Func<float?> _yawProvider;
        private CombatTarget _lockedTarget;

        private void Awake()
        {
            if (cameraController == null)
            {
                cameraController = GetComponent<ThirdPersonCameraController>();
            }
        }

        private void OnEnable()
        {
            _yawProvider = ResolveYaw;

            if (cameraController != null)
            {
                cameraController.YawOverrideProvider = _yawProvider;
            }

            if (targetLock != null)
            {
                targetLock.TargetChanged += HandleTargetChanged;
            }
        }

        private void OnDisable()
        {
            if (targetLock != null)
            {
                targetLock.TargetChanged -= HandleTargetChanged;
            }

            if (cameraController != null && cameraController.YawOverrideProvider == _yawProvider)
            {
                cameraController.YawOverrideProvider = null;
            }

            _lockedTarget = null;
        }

        // 由 BattleBootstrap 在生成玩家后调用：相机是场景对象，玩家是运行时生成的。
        public void Bind(PlayerTargetLock lockComponent)
        {
            if (targetLock != null)
            {
                targetLock.TargetChanged -= HandleTargetChanged;
            }

            targetLock = lockComponent;
            _lockedTarget = targetLock != null ? targetLock.CurrentTarget : null;

            if (isActiveAndEnabled && targetLock != null)
            {
                targetLock.TargetChanged += HandleTargetChanged;
            }
        }

        private void HandleTargetChanged(CombatTarget target)
        {
            _lockedTarget = target;
        }

        private float? ResolveYaw()
        {
            if (_lockedTarget == null || targetLock == null)
            {
                return null;
            }

            Vector3 toTarget = _lockedTarget.LockPosition - targetLock.transform.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude <= 0.0001f)
            {
                return null;
            }

            return Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;//将向量转换为角度
        }
    }
}
