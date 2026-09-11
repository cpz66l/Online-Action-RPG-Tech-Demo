using UnityEngine;
using UnityEngine.InputSystem;

namespace OnlineActionRpg.Client.Battle
{
    // 04C-0 第三人称相机：从 PlayerInputReader 读取 Look 输入。
    // 当前只负责本地镜头表现，不处理锁定目标和网络同步。
    public sealed class ThirdPersonCameraController : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;
        [SerializeField] private float lookHeight = 1.45f;

        [Header("Input")]
        [SerializeField] private PlayerInputReader inputReader;

        [Header("Orbit")]
        [SerializeField] private float distance = 5.2f;
        [SerializeField] private float minDistance = 2.2f;
        [SerializeField] private float maxDistance = 7.2f;
        [SerializeField] private float shoulderOffset = 0.35f;
        [SerializeField] private float heightOffset = 1.85f;
        [SerializeField] private float mouseSensitivity = 0.12f;
        [SerializeField] private float gamepadSensitivity = 120f;
        [SerializeField] private float minPitch = -18f;
        [SerializeField] private float maxPitch = 58f;

        [Header("Zoom")]
        [SerializeField] private float zoomSensitivity = 0.02f;
        [SerializeField] private float zoomSharpness = 20f;

        [Header("Smoothing")]
        [SerializeField] private float followSharpness = 22f;
        [SerializeField] private float rotationSharpness = 24f;

        [Header("Cursor")]
        [SerializeField] private bool lockCursorOnStart = true;
        [SerializeField] private bool clickToRelockCursor = true;

        [Header("Collision")]
        [SerializeField] private bool enableCameraCollision = true;
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField] private float collisionRadius = 0.25f;
        [SerializeField] private float collisionPadding = 0.15f;

        private float _yaw;
        private float _pitch = 18f;
        private float _targetDistance;
        private float _currentDistance;

        private void Awake()
        {
            ResolveReferences();

            Vector3 euler = transform.eulerAngles;
            _yaw = euler.y;
            _pitch = Mathf.Clamp(NormalizePitch(euler.x), minPitch, maxPitch);

            _targetDistance = Mathf.Clamp(distance, minDistance, maxDistance);
            _currentDistance = _targetDistance;
        }

        private void Start()
        {
            if (lockCursorOnStart)
            {
                LockCursor();
            }
        }

        public void SetTarget(Transform followTarget)
        {
            target = followTarget;

            if (target != null)
            {
                _yaw = target.eulerAngles.y;
            }

            ResolveReferencesFromTarget();
        }

        private void LateUpdate()
        {
            HandleCursorInput();

            if (target == null)
            {
                return;
            }

            UpdateOrbitInput();
            UpdateZoomInput();
            UpdateCurrentDistance();

            Quaternion orbitRotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 focusPoint = target.position + Vector3.up * lookHeight;

            Vector3 localOffset = new Vector3(shoulderOffset, heightOffset, -_currentDistance);
            Vector3 desiredPosition = focusPoint + orbitRotation * localOffset;
            desiredPosition = ResolveCameraCollision(focusPoint, desiredPosition);

            float followT = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desiredPosition, followT);

            Quaternion desiredRotation = Quaternion.LookRotation(focusPoint - transform.position, Vector3.up);
            float rotationT = 1f - Mathf.Exp(-rotationSharpness * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationT);
        }

        private void ResolveReferences()
        {
            if (inputReader == null)
            {
                inputReader = FindFirstObjectByType<PlayerInputReader>();
            }
        }

        private void ResolveReferencesFromTarget()
        {
            if (inputReader == null && target != null)
            {
                inputReader = target.GetComponentInParent<PlayerInputReader>();
            }
        }

        private void HandleCursorInput()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                UnlockCursor();
                return;
            }

            if (!clickToRelockCursor)
            {
                return;
            }

            Mouse mouse = Mouse.current;

            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                LockCursor();
            }
        }

        private void UpdateOrbitInput()
        {
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            Vector2 lookInput = inputReader != null ? inputReader.Look : Vector2.zero;

            if (lookInput.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            bool usingMouse = Mouse.current != null && Mouse.current.delta.ReadValue().sqrMagnitude > 0.0001f;

            float sensitivity = usingMouse
                ? mouseSensitivity
                : gamepadSensitivity * Time.deltaTime;

            _yaw += lookInput.x * sensitivity;
            _pitch -= lookInput.y * sensitivity;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
        }

        private void UpdateZoomInput()
        {
            Mouse mouse = Mouse.current;

            if (mouse == null)
            {
                return;
            }

            float scrollY = mouse.scroll.ReadValue().y;

            if (Mathf.Abs(scrollY) <= 0.01f)
            {
                return;
            }

            _targetDistance -= scrollY * zoomSensitivity;
            _targetDistance = Mathf.Clamp(_targetDistance, minDistance, maxDistance);
        }

        private void UpdateCurrentDistance()
        {
            float zoomT = 1f - Mathf.Exp(-zoomSharpness * Time.deltaTime);
            _currentDistance = Mathf.Lerp(_currentDistance, _targetDistance, zoomT);
        }

        private Vector3 ResolveCameraCollision(Vector3 focusPoint, Vector3 desiredPosition)
        {
            if (!enableCameraCollision)
            {
                return desiredPosition;
            }

            Vector3 direction = desiredPosition - focusPoint;
            float desiredDistance = direction.magnitude;

            if (desiredDistance <= 0.001f)
            {
                return desiredPosition;
            }

            direction /= desiredDistance;

            if (Physics.SphereCast(
                    focusPoint,
                    collisionRadius,
                    direction,
                    out RaycastHit hit,
                    desiredDistance,
                    collisionMask,
                    QueryTriggerInteraction.Ignore))
            {
                float safeDistance = Mathf.Max(0.1f, hit.distance - collisionPadding);
                return focusPoint + direction * safeDistance;
            }

            return desiredPosition;
        }

        private static float NormalizePitch(float pitch)
        {
            return pitch > 180f ? pitch - 360f : pitch;
        }

        public static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public static void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnDisable()
        {
            UnlockCursor();
        }
    }
}