using System;
using UnityEngine;

namespace OnlineActionRpg.Client.Battle
{
    // 负责本地移动、加减速、重力和朝向。
    // 输入只表达意图，Controller 判断动作是否成立。
    [RequireComponent(typeof(CharacterController))]
    public sealed class LocalPlayerController : MonoBehaviour
    {
        // ==================== Types ====================
        public enum AttackVariant
        {
            RightPunch,
            LeftPunch
        }

        private enum LocomotionMode
        {
            Walk,
            Run
        }

        // ==================== Inspector Config ====================
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
        [SerializeField] private float attackInputBufferTime = 0.12f;
        [SerializeField] private float comboResetWindow = 0.55f;

        [Header("Attack Hit Window")]
        [SerializeField, Range(0f, 1f)] private float rightPunchHitStart01 = 0.30f;
        [SerializeField, Range(0f, 1f)] private float rightPunchHitEnd01 = 0.55f;
        [SerializeField, Range(0f, 1f)] private float leftPunchHitStart01 = 0.28f;
        [SerializeField, Range(0f, 1f)] private float leftPunchHitEnd01 = 0.52f;

        // 命中窗口用归一化进度表达：0 = 攻击起手，1 = 攻击结束。
        // 用代码时间窗而不是 Animation Event，是为了让“这一击什么时候能命中”
        // 留在玩法层；动画替换、重定向或将来做服务端校验时都不受影响。

        // ==================== Runtime State ====================
        private CharacterController _characterController;

        // Movement state.
        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;
        private float _turnSmoothVelocity;
        private LocomotionMode _currentLocomotionMode;

        // Jump state.
        private float _lastGroundedTime;
        private bool _jumpQueued;

        // Dodge state.
        private bool _dodgeQueued;
        private bool _isDodging;
        private Vector3 _dodgeDirection;
        private float _dodgeTimer;
        private float _dodgeCooldownTimer;

        // Attack state.
        private float _attackInputBufferTimer;
        private bool _isAttacking;
        private float _attackTimer;
        private float _attackCooldownTimer;
        private float _comboWindowTimer;
        private Vector3 _attackImpulseDirection;
        private float _attackImpulseTimer;
        private bool _isInAttackHitWindow;

        // ==================== Public State / Events ====================
        public float VerticalSpeed => _verticalVelocity;
        public bool IsGrounded { get; private set; }

        public bool IsDodging => _isDodging;
        public float DodgeCooldownRemaining => _dodgeCooldownTimer;

        public bool IsAttacking => _isAttacking;
        public float AttackCooldownRemaining => _attackCooldownTimer;
        public float ComboWindowRemaining => _comboWindowTimer;
        public AttackVariant CurrentAttackVariant { get; private set; } = AttackVariant.RightPunch;
        public bool IsInAttackHitWindow => _isInAttackHitWindow;
        public float AttackProgress01 { get; private set; }

        public bool IsWalking => _currentLocomotionMode == LocomotionMode.Walk && CurrentMoveSpeed01 > 0.05f;
        public bool IsRunning => _currentLocomotionMode == LocomotionMode.Run && CurrentMoveSpeed01 > 0.05f;

        public Vector3 CurrentMoveDirection { get; private set; }
        public Vector3 CurrentHorizontalVelocity => _horizontalVelocity;
        public float CurrentMoveSpeed01 { get; private set; }

        public event Action JumpStarted;
        public event Action DodgeStarted;
        public event Action DodgeEnded;
        public event Action AttackStarted;
        public event Action AttackEnded;
        public event Action<AttackVariant> AttackHitWindowOpened;
        public event Action AttackHitWindowClosed;

        // ==================== Unity Lifecycle ====================
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

        // ==================== Frame Pipeline ====================
        private void Update()
        {
            float deltaTime = Time.deltaTime;

            Vector2 moveInput = inputReader != null ? inputReader.Move : Vector2.zero;
            Vector3 desiredDirection = BuildCameraRelativeMoveDirection(moveInput);

            TickDodgeCooldown(deltaTime);
            TickAttackInputBuffer(deltaTime);
            TickAttackState(deltaTime);
            TickAttackComboWindow(deltaTime);

            UpdateGroundedState();

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

            ApplyRotation();
            ApplyJump();
            ApplyGravity();
            ApplyMovement();
            RefreshGroundedAfterMove();
            FinishDodgeIfNeeded();
            UpdateDebugState();
        }

        // ==================== Input Handlers ====================
        private void HandleWalkModeChanged(bool isWalkModeHeld)
        {
            _currentLocomotionMode = isWalkModeHeld
                ? LocomotionMode.Walk
                : defaultLocomotionMode;
        }

        private void HandleJumpPressed()
        {
            _jumpQueued = true;
        }

        private void HandleDodgePressed()
        {
            _dodgeQueued = true;
        }

        private void HandleAttackPressed()
        {
            _attackInputBufferTimer = attackInputBufferTime;
        }

        // ==================== Locomotion ====================
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

        // ==================== Grounding / Jump ====================
        private void UpdateGroundedState()
        {
            IsGrounded = _characterController.isGrounded;

            if (_characterController.isGrounded)
            {
                _lastGroundedTime = Time.time;
            }
        }

        private void ApplyJump()
        {
            if (!_jumpQueued)
            {
                return;
            }

            if (_isDodging)
            {
                _jumpQueued = false;
                return;
            }

            if (_isAttacking)
            {
                _jumpQueued = false;
                return;
            }

            bool canJump = Time.time - _lastGroundedTime <= jumpGroundedGraceTime;

            if (!canJump)
            {
                _jumpQueued = false;
                return;
            }

            _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            _jumpQueued = false;
            ResetAttackCombo();

            JumpStarted?.Invoke();
        }

        private void ApplyGravity()
        {
            if (IsGrounded && _verticalVelocity < 0f && !_jumpQueued)
            {
                _verticalVelocity = groundedStickForce;
            }

            _verticalVelocity += gravity * Time.deltaTime;
        }

        private void RefreshGroundedAfterMove()
        {
            IsGrounded = _characterController.isGrounded;

            if (IsGrounded)
            {
                _lastGroundedTime = Time.time;
            }
        }

        // ==================== Dodge ====================
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

            if (dodgeBlockedByAttack && _isAttacking)
            {
                return;
            }

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

            ResetAttackCombo();
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

        // ==================== Attack / Combo ====================
        private void TickAttackInputBuffer(float deltaTime)
        {
            if (_attackInputBufferTimer > 0f)
            {
                _attackInputBufferTimer = Mathf.Max(0f, _attackInputBufferTimer - deltaTime);
            }
        }

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
                UpdateAttackHitWindow();
                return;
            }

            //攻击结束
            _isAttacking = false;
            _attackImpulseTimer = 0f;
            _comboWindowTimer = comboResetWindow;

            CloseAttackHitWindow();

            _horizontalVelocity = Vector3.MoveTowards(
                _horizontalVelocity,
                Vector3.zero,
                attackImpulseBrake * deltaTime);

            AttackEnded?.Invoke();
        }

        private void TickAttackComboWindow(float deltaTime)
        {
            if (_isAttacking || _comboWindowTimer <= 0f)
            {
                return;
            }

            _comboWindowTimer = Mathf.Max(0f, _comboWindowTimer - deltaTime);
        }

        private void TryStartAttack()
        {
            if (_attackInputBufferTimer <= 0f)
            {
                return;
            }

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

            _attackInputBufferTimer = 0f;
            CurrentAttackVariant = ResolveNextAttackVariant();

            _isAttacking = true;
            _attackTimer = attackDuration;
            _attackCooldownTimer = attackDuration + attackCooldown;
            _comboWindowTimer = 0f;
            AttackProgress01 = 0f;

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

        private AttackVariant ResolveNextAttackVariant()
        {
            if (_comboWindowTimer <= 0f)
            {
                return AttackVariant.RightPunch;
            }

            return CurrentAttackVariant == AttackVariant.RightPunch
                ? AttackVariant.LeftPunch
                : AttackVariant.RightPunch;
        }

        // 把“这次攻击进行到哪、现在能不能命中”算清楚，并用事件把窗口开合广播出去。
        // HitBox 只订阅事件和读 IsInAttackHitWindow，不需要自己复算攻击计时。
        private void UpdateAttackHitWindow()
        {
            float duration = attackDuration > 0f ? attackDuration : 0.0001f;
            //AttackProgress用于将attackDuration归一化攻击进度，0 = 攻击起手，1 = 攻击结束。
            AttackProgress01 = Mathf.Clamp01(1f - (_attackTimer / duration));

            float start = CurrentAttackVariant == AttackVariant.LeftPunch
                ? leftPunchHitStart01
                : rightPunchHitStart01;

            float end = CurrentAttackVariant == AttackVariant.LeftPunch
                ? leftPunchHitEnd01
                : rightPunchHitEnd01;

            //避免调试时出现 start > end 的情况，导致命中窗口永远不会开。
            if (start > end)
            {
                (start, end) = (end, start);
            }

            bool inWindow = AttackProgress01 >= start && AttackProgress01 <= end;

            if (inWindow == _isInAttackHitWindow)
            {
                return;
            }

            if (inWindow)
            {
                _isInAttackHitWindow = true;
                AttackHitWindowOpened?.Invoke(CurrentAttackVariant);
            }
            else
            {
                CloseAttackHitWindow();
            }
        }

        private void CloseAttackHitWindow()
        {
            if (!_isInAttackHitWindow)
            {
                return;
            }

            _isInAttackHitWindow = false;
            AttackHitWindowClosed?.Invoke();
        }
        private void ResetAttackCombo()
        {
            _comboWindowTimer = 0f;
            CurrentAttackVariant = AttackVariant.RightPunch;
        }
    }
}
