using UnityEngine;

namespace OnlineActionRpg.Client.Battle
{
    //动画驱动器：把本地控制器的移动状态翻译成 Animator 参数。
    public sealed class CharacterAnimatorDriver : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private LocalPlayerController playerController;
        [SerializeField] private Animator animator;
        [SerializeField] private PlayerInputReader inputReader;
        [SerializeField] private LocalEmoteController emoteController;

        [Header("Animator Parameters")]
        [SerializeField] private string idleStateName = "Idle_Loop";
        [SerializeField] private string moveSpeedParameter = "MoveSpeed";
        [SerializeField] private string isMovingParameter = "IsMoving";
        [SerializeField] private string rightPunchTriggerParameter = "AttackRight";
        [SerializeField] private string leftPunchTriggerParameter = "AttackLeft";
        [SerializeField] private string dodgeTriggerParameter = "Dodge";
        [SerializeField] private string jumpTriggerParameter = "Jump";
        [SerializeField] private string isGroundedParameter = "IsGrounded";
        [SerializeField] private string verticalSpeedParameter = "VerticalSpeed";
        [SerializeField] private string isAttackingParameter = "IsAttacking";
        [SerializeField] private string isWalkingParameter = "IsWalking";
        [SerializeField] private string isRunningParameter = "IsRunning";

        [Header("Movement")]
        [SerializeField] private float moveThreshold = 0.05f;
        [SerializeField] private float speedDampTime = 0.08f;
        [SerializeField] private float emoteFadeDuration = 0.12f;

        private int _moveSpeedHash;
        private int _isMovingHash;
        private int _rightPunchHash;
        private int _leftPunchHash;
        private int _dodgeHash;
        private int _jumpHash;
        private int _isGroundedHash;
        private int _verticalSpeedHash;
        private int _isAttackingHash;
        private int _isWalkingHash;
        private int _isRunningHash;

        private void Awake()
        {
            ResolveReferences();

            if (animator != null)
            {
                animator.applyRootMotion = false;
            }

            CacheParameterHashes();
        }

        private void OnEnable()
        {
            ResolveReferences();

            if (playerController != null)
            {
                playerController.AttackStarted += HandleAttackStarted;
                playerController.DodgeStarted += HandleDodgeStarted;
                playerController.JumpStarted += HandleJumpStarted;
            }

            if (emoteController != null)
            {
                emoteController.EmoteStarted += HandleEmoteStarted;
                emoteController.EmoteCanceled += HandleEmoteCanceled;
            }
        }

        private void OnDisable()
        {
            if (playerController != null)
            {
                playerController.AttackStarted -= HandleAttackStarted;
                playerController.DodgeStarted -= HandleDodgeStarted;
                playerController.JumpStarted -= HandleJumpStarted;
            }

            if (emoteController != null)
            {
                emoteController.EmoteStarted -= HandleEmoteStarted;
                emoteController.EmoteCanceled -= HandleEmoteCanceled;
            }
        }

        private void Reset()
        {
            ResolveReferences();
        }

        private void LateUpdate()
        {
            if (animator == null || playerController == null)
            {
                return;
            }

            UpdateMovementParameters();
        }

        private void ResolveReferences()
        {
            if (playerController == null)
            {
                playerController = GetComponentInParent<LocalPlayerController>();
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (inputReader == null)
            {
                inputReader = GetComponentInParent<PlayerInputReader>();
            }

            if (emoteController == null)
            {
                emoteController = GetComponentInParent<LocalEmoteController>();
            }
        }

        //缓存Animator参数的哈希值，避免每次查找字符串以提高性能。
        private void CacheParameterHashes()
        {
            _moveSpeedHash = Animator.StringToHash(moveSpeedParameter);
            _isMovingHash = Animator.StringToHash(isMovingParameter);
            _rightPunchHash = Animator.StringToHash(rightPunchTriggerParameter);
            _leftPunchHash = Animator.StringToHash(leftPunchTriggerParameter);
            _dodgeHash = Animator.StringToHash(dodgeTriggerParameter);
            _jumpHash = Animator.StringToHash(jumpTriggerParameter);
            _isGroundedHash = Animator.StringToHash(isGroundedParameter);
            _verticalSpeedHash = Animator.StringToHash(verticalSpeedParameter);
            _isAttackingHash = Animator.StringToHash(isAttackingParameter);
            _isWalkingHash = Animator.StringToHash(isWalkingParameter);
            _isRunningHash = Animator.StringToHash(isRunningParameter);
        }

        private void UpdateMovementParameters()
        {
            float moveSpeed01 = Mathf.Clamp01(playerController.CurrentMoveSpeed01);
            bool isMoving = moveSpeed01 > moveThreshold;

            animator.SetFloat(_moveSpeedHash, moveSpeed01, speedDampTime, Time.deltaTime);
            animator.SetBool(_isMovingHash, isMoving);
            animator.SetBool(_isGroundedHash, playerController.IsGrounded);
            animator.SetFloat(_verticalSpeedHash, playerController.VerticalSpeed);
            animator.SetBool(_isAttackingHash, playerController.IsAttacking);
            animator.SetBool(_isWalkingHash, playerController.IsWalking);
            animator.SetBool(_isRunningHash, playerController.IsRunning);
        }


        private void HandleAttackStarted()
        {
            if (animator == null || playerController == null)
            {
                return;
            }

            int triggerHash = playerController.CurrentAttackVariant == LocalPlayerController.AttackVariant.LeftPunch
                ? _leftPunchHash
                : _rightPunchHash;

            animator.SetTrigger(triggerHash);
        }

        private void HandleDodgeStarted()
        {
            if (animator == null)
            {
                return;
            }

            animator.ResetTrigger(_jumpHash);
            animator.SetTrigger(_dodgeHash);
        }

        private void HandleJumpStarted()
        {
            if (animator == null)
            {
                return;
            }

            animator.SetTrigger(_jumpHash);
        }

        private void HandleEmoteStarted(EmoteDefinition emote)
        {
            if (animator == null || emote == null || string.IsNullOrWhiteSpace(emote.AnimatorStateName))
            {
                return;
            }

            //当表情动作开始时，重置攻击、闪避和跳跃触发器，以确保动画状态机正确过渡到表情动画。
            animator.ResetTrigger(_rightPunchHash);
            animator.ResetTrigger(_leftPunchHash);
            animator.ResetTrigger(_dodgeHash);
            animator.ResetTrigger(_jumpHash);
            //使用 CrossFadeInFixedTime 方法平滑过渡到表情动画状态，持续时间为 emoteFadeDuration。
            animator.CrossFadeInFixedTime(emote.AnimatorStateName, emoteFadeDuration);
        }

        private void HandleEmoteCanceled()
        {
            if (animator == null || string.IsNullOrWhiteSpace(idleStateName))
            {
                return;
            }

            //当表情动作被取消时，过渡回默认的空闲状态。
            animator.CrossFadeInFixedTime(idleStateName, emoteFadeDuration);
        }
    }
}

