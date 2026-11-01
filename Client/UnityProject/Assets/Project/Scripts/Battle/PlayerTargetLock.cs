using OnlineActionRpg.Client.Combat;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnlineActionRpg.Client.Battle
{
    // “按下中键 -> 选一个目标 -> 打印结果”。
    // 锁定粘住、取消条件、事件广播留到 04F-2。
    public sealed class PlayerTargetLock : MonoBehaviour
    {
        [Header("Selection")]
        [SerializeField] private float maxDistance = 12f;
        [SerializeField] private LayerMask targetMask = ~0;
        [SerializeField] private CombatTarget.Faction targetFaction = CombatTarget.Faction.Enemy;
        [SerializeField] private float anglePenaltyDistance = TargetSelector.DefaultAnglePenaltyDistance;

        private Transform _cameraTransform;

        public CombatTarget CurrentTarget { get; private set; }

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
            Mouse mouse = Mouse.current;//获取当前鼠标设备，Input System 的鼠标类

            // 项目是 Input System only；04F-1 先直读鼠标，暂不改 inputactions 资产。
            if (mouse == null || !mouse.middleButton.wasPressedThisFrame)
            {
                return;
            }

            CurrentTarget = SelectTarget();
            LogSelection();//打印选中目标的调试信息
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

        private void LogSelection()
        {
            if (CurrentTarget == null)
            {
                Debug.Log("[TargetLock] Selected nothing.");
                return;
            }

            Vector3 toTarget = CurrentTarget.LockPosition - transform.position;
            Vector3 flatToTarget = new Vector3(toTarget.x, 0f, toTarget.z);

            float distance = toTarget.magnitude;
            float angle = Vector3.Angle(ResolveSelectionForward(), flatToTarget);
            float score = distance + angle / 180f * anglePenaltyDistance;


            Debug.Log($"[TargetLock] Selected {CurrentTarget.name}  " +
                      $"dist={distance:0.0}  angle={angle:0}°  score={score:0.00}");
        }

        //方向解析
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
