using System;
using UnityEngine;

namespace OnlineActionRpg.Client.Battle
{
    // 04B-2 本地玩家控制器：负责本地移动、加减速、重力和朝向。
    [RequireComponent(typeof(CharacterController))]
    public sealed class LocalPlayerController : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private PlayerInputReader inputReader;

        [Header("Movement")]
        [SerializeField] private float walkSpeed = 2.2f;
        [SerializeField] private float runSpeed = 5.2f;
        [SerializeField] private LocomotionMode defaultLocomotionMode = LocomotionMode.Run;
        [SerializeField] private float acceleration = 32f;
        [SerializeField] private float deceleration = 40f;
        [SerializeField] private float gravity = -24f;
        [SerializeField] private float groundedStickForce = -3f;
        [SerializeField] private float jumpHeight = 1.2f;
        [SerializeField] private float jumpGroundedGraceTime = 0.08f;

        [Header("Rotation")]
        [SerializeField] private float turnSmoothTime = 0.08f;
        [SerializeField] private float minRotationSpeed = 0.15f;

        [Header("View")]
        [SerializeField] private Transform cameraTransform;

        [Header("Dodge")]
        [SerializeField] private float dodgeSpeed = 9.5f;
        [SerializeField] private float dodgeDuration = 0.22f;
        [SerializeField] private float dodgeCooldown = 0.45f;
        [SerializeField] private bool dodgeRequiresGrounded = true;

        [Header("Attack")]
        [SerializeField] private float attackDuration = 0.45f;
        [SerializeField] private float attackCooldown = 0.2f;
        [SerializeField] private bool attackRequiresGrounded = true;
        [SerializeField] private bool attackBlockedByDodge = true;
        [SerializeField] private bool dodgeBlockedByAttack = true;
        [SerializeField] private bool attackLocksMovement = true;
        [SerializeField] private float attackImpulseSpeed = 3.5f;
        [SerializeField] private float attackImpulseDuration = 0.12f;
        [SerializeField] private float attackImpulseBrake = 30f;

        private CharacterController _characterController;
        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;
        private float _turnSmoothVelocity;

        //跳跃
        private float _lastGroundedTime;
        private bool _jumpQueued;
        public float VerticalSpeed => _verticalVelocity;
        public bool IsGrounded { get; private set; }

        //翻滚
        private bool _dodgeQueued;
        private bool _isDodging;
        private Vector3 _dodgeDirection;
        private float _dodgeTimer;
        private float _dodgeCooldownTimer;
        public event Action DodgeStarted;
        public event Action DodgeEnded;
        public bool IsDodging => _isDodging;
        public float DodgeCooldownRemaining => _dodgeCooldownTimer;
        //攻击
        private bool _attackQueued;
        private bool _isAttacking;
        private float _attackTimer;
        private float _attackCooldownTimer;
        public event Action AttackStarted;
        public event Action AttackEnded;
        public bool IsAttacking => _isAttacking;
        public float AttackCooldownRemaining => _attackCooldownTimer;
        private Vector3 _attackImpulseDirection;
        private float _attackImpulseTimer;
        //移动
        private enum LocomotionMode
        {
            Walk,
            Run
        }
        private LocomotionMode _currentLocomotionMode;

        public bool IsWalking => _currentLocomotionMode == LocomotionMode.Walk && CurrentMoveSpeed01 > 0.05f;
        public bool IsRunning => _currentLocomotionMode == LocomotionMode.Run && CurrentMoveSpeed01 > 0.05f;

        public Vector3 CurrentMoveDirection { get; private set; }
        public Vector3 CurrentHorizontalVelocity => _horizontalVelocity;
        public float CurrentMoveSpeed01 { get; private set; }

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();

            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }

            if (inputReader == null)
            {
                inputReader = GetComponent<PlayerInputReader>();
            }

            _currentLocomotionMode = defaultLocomotionMode;
        }

        private void OnEnable()
        {
            if (inputReader != null)
            {
                inputReader.JumpPressed += HandleJumpPressed;
                inputReader.DodgePressed += HandleDodgePressed;
                inputReader.AttackPressed += HandleAttackPressed;
                inputReader.WalkModeChanged += HandleWalkModeChanged;

                HandleWalkModeChanged(inputReader.IsWalkModeHeld);
            }
        }

        private void OnDisable()
        {
            if (inputReader != null)
            {
                inputReader.JumpPressed -= HandleJumpPressed;
                inputReader.DodgePressed -= HandleDodgePressed;
                inputReader.AttackPressed -= HandleAttackPressed;
                inputReader.WalkModeChanged -= HandleWalkModeChanged;
            }
        }

        public void SetCameraTransform(Transform targetCamera)
        {
            cameraTransform = targetCamera;
        }

        private void HandleWalkModeChanged(bool isWalkModeHeld)
        {
            _currentLocomotionMode = isWalkModeHeld
                ? LocomotionMode.Walk
                : defaultLocomotionMode;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            //处理移动输入，计算期望的移动方向
            Vector2 moveInput = inputReader != null ? inputReader.Move : Vector2.zero;
            Vector3 desiredDirection = BuildCameraRelativeMoveDirection(moveInput);

            //翻滚和攻击的冷却计时
            TickDodgeCooldown(deltaTime);
            TickAttackState(deltaTime);

            UpdateGroundedState();

            //处理翻滚和攻击输入
            TryStartDodge(desiredDirection);
            TryStartAttack();

            if (_isDodging)
            {
                UpdateDodge(deltaTime);
            }
            else if (_isAttacking && attackLocksMovement)
            {
                UpdateAttackMovement(deltaTime);
            }
            else
            {
                UpdateHorizontalVelocity(desiredDirection, moveInput.magnitude);
            }

            //处理旋转、跳跃、重力和移动
            ApplyRotation();
            ApplyJump();
            ApplyGravity();
            ApplyMovement();
            RefreshGroundedAfterMove();
            FinishDodgeIfNeeded();
            UpdateDebugState();
        }

        private Vector3 BuildCameraRelativeMoveDirection(Vector2 input)
        {
            if (input.sqrMagnitude <= 0.0001f)
            {
                return Vector3.zero;
            }

            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;

            if (cameraTransform != null)
            {
                forward = cameraTransform.forward;
                right = cameraTransform.right;

                forward.y = 0f;
                right.y = 0f;

                forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
                right = right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
            }

            Vector3 direction = forward * input.y + right * input.x;
            return direction.sqrMagnitude > 1f ? direction.normalized : direction;
        }

        private void UpdateHorizontalVelocity(Vector3 desiredDirection, float inputMagnitude)
        {
            float clampedInput = Mathf.Clamp01(inputMagnitude);

            float selectedSpeed = _currentLocomotionMode == LocomotionMode.Walk ? walkSpeed : runSpeed;

            Vector3 targetVelocity = desiredDirection * (selectedSpeed * clampedInput);

            bool hasInput = desiredDirection.sqrMagnitude > 0.0001f;
            float rate = hasInput ? acceleration : deceleration;

            _horizontalVelocity = Vector3.MoveTowards(
                _horizontalVelocity,
                targetVelocity,
                rate * Time.deltaTime);
        }

        private void ApplyRotation()
        {
            Vector3 flatVelocity = _horizontalVelocity;
            flatVelocity.y = 0f;

            if (flatVelocity.magnitude < minRotationSpeed)
            {
                return;
            }

            float targetAngle = Mathf.Atan2(flatVelocity.x, flatVelocity.z) * Mathf.Rad2Deg;

            float smoothedAngle = Mathf.SmoothDampAngle(
                transform.eulerAngles.y,
                targetAngle,
                ref _turnSmoothVelocity,
                turnSmoothTime);

            transform.rotation = Quaternion.Euler(0f, smoothedAngle, 0f);
        }

        private void ApplyGravity()
        {
            if (IsGrounded && _verticalVelocity < 0f && !_jumpQueued)
            {
                _verticalVelocity = groundedStickForce;
            }

            _verticalVelocity += gravity * Time.deltaTime;
        }

        private void ApplyMovement()
        {
            Vector3 velocity = _horizontalVelocity + Vector3.up * _verticalVelocity;
            _characterController.Move(velocity * Time.deltaTime);
        }

        private void UpdateDebugState()
        {
            Vector3 flatVelocity = _horizontalVelocity;
            flatVelocity.y = 0f;

            float speed = flatVelocity.magnitude;
            CurrentMoveSpeed01 = runSpeed > 0f ? Mathf.Clamp01(speed / runSpeed) : 0f;
            CurrentMoveDirection = speed > 0.0001f ? flatVelocity.normalized : Vector3.zero;
        }

        //跳跃
        private void ApplyJump()
        {
            if (!_jumpQueued)
            {
                return;
            }

            //如果玩家正在翻滚，则不允许跳跃。
            if (_isDodging)
            {
                _jumpQueued = false;
                return;
            }

            //如果玩家正在攻击，则不允许跳跃。
            if (_isAttacking)
            {
                _jumpQueued = false;
                return;
            }

            //如果玩家在跳跃按键按下后，仍然在允许的跳跃宽限时间内，则允许跳跃。
            bool canJump = Time.time - _lastGroundedTime <= jumpGroundedGraceTime;

            if (!canJump)
            {
                _jumpQueued = false;
                return;
            }

            _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            _jumpQueued = false;
        }

        private void UpdateGroundedState()
        {
            IsGrounded = _characterController.isGrounded;

            if (_characterController.isGrounded)
            {
                _lastGroundedTime = Time.time;
            }
        }

        private void RefreshGroundedAfterMove()
        {
            IsGrounded = _characterController.isGrounded;

            if (IsGrounded)
            {
                _lastGroundedTime = Time.time;
            }
        }

        private void HandleJumpPressed()
        {
            _jumpQueued = true;
        }

        //翻滚
        private void TickDodgeCooldown(float deltaTime)
        {
            if (_dodgeCooldownTimer > 0f)
            {
                _dodgeCooldownTimer = Mathf.Max(0f, _dodgeCooldownTimer - deltaTime);
            }
        }

        private void TryStartDodge(Vector3 desiredDirection)
        {
            if (!_dodgeQueued)
            {
                return;
            }

            _dodgeQueued = false;

            if (_isDodging || _dodgeCooldownTimer > 0f)
            {
                return;
            }

            //如果玩家正在攻击，则不允许翻滚。
            if (dodgeBlockedByAttack && _isAttacking)
            {
                return;
            }

            //如果玩家当前不在地面上，则不允许翻滚。
            if (dodgeRequiresGrounded && !IsGrounded)
            {
                return;
            }

            Vector3 direction = desiredDirection.sqrMagnitude > 0.0001f
                ? desiredDirection.normalized
                : transform.forward;

            direction.y = 0f;

            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector3.forward;
            }

            _dodgeDirection = direction.normalized;
            _isDodging = true;
            _dodgeTimer = dodgeDuration;
            _dodgeCooldownTimer = dodgeCooldown;
            _horizontalVelocity = _dodgeDirection * dodgeSpeed;

            DodgeStarted?.Invoke();
        }

        private void UpdateDodge(float deltaTime)
        {
            _dodgeTimer -= deltaTime;
            _horizontalVelocity = _dodgeDirection * dodgeSpeed;
        }

        private void FinishDodgeIfNeeded()
        {
            if (!_isDodging || _dodgeTimer > 0f)
            {
                return;
            }

            _isDodging = false;
            DodgeEnded?.Invoke();
        }

        private void HandleDodgePressed()
        {
            _dodgeQueued = true;
        }

        //攻击
        private void TickAttackState(float deltaTime)
        {
            if (_attackCooldownTimer > 0f)
            {
                _attackCooldownTimer = Mathf.Max(0f, _attackCooldownTimer - deltaTime);
            }

            if (!_isAttacking)
            {
                return;
            }

            _attackTimer -= deltaTime;

            if (_attackTimer > 0f)
            {
                return;
            }

            _isAttacking = false;
            _attackImpulseTimer = 0f;
            _horizontalVelocity = Vector3.MoveTowards(
                _horizontalVelocity,
                Vector3.zero,
                attackImpulseBrake * deltaTime);

            AttackEnded?.Invoke();
        }

        private void TryStartAttack()
        {
            if (!_attackQueued)
            {
                return;
            }

            _attackQueued = false;

            if (_isAttacking || _attackCooldownTimer > 0f)
            {
                return;
            }

            if (attackRequiresGrounded && !IsGrounded)
            {
                return;
            }

            if (attackBlockedByDodge && _isDodging)
            {
                return;
            }

            _isAttacking = true;
            _attackTimer = attackDuration;
            _attackCooldownTimer = attackDuration + attackCooldown;

            StartAttackImpulse();

            AttackStarted?.Invoke();
        }

        private void StartAttackImpulse()
        {
            Vector3 direction = transform.forward;
            direction.y = 0f;

            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector3.forward;
            }

            _attackImpulseDirection = direction.normalized;
            _attackImpulseTimer = attackImpulseDuration;
            _horizontalVelocity = _attackImpulseDirection * attackImpulseSpeed;
        }

        private void UpdateAttackMovement(float deltaTime)
        {
            if (_attackImpulseTimer > 0f)
            {
                _attackImpulseTimer = Mathf.Max(0f, _attackImpulseTimer - deltaTime);

                float remaining01 = attackImpulseDuration > 0f
                    ? _attackImpulseTimer / attackImpulseDuration
                    : 0f;

                _horizontalVelocity = _attackImpulseDirection * (attackImpulseSpeed * remaining01);
                return;
            }

            _horizontalVelocity = Vector3.MoveTowards(
                _horizontalVelocity,
                Vector3.zero,
                attackImpulseBrake * deltaTime);
        }

        private void HandleAttackPressed()
        {
            _attackQueued = true;
        }

    }
}
