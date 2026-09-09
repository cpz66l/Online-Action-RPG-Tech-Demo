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

        [Header("Animator Parameters")]
        [SerializeField] private string moveSpeedParameter = "MoveSpeed";
        [SerializeField] private string isMovingParameter = "IsMoving";
        [SerializeField] private string attackTriggerParameter = "Attack";
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

        private int _moveSpeedHash;
        private int _isMovingHash;
        private int _attackHash;
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

            if (inputReader != null)
            {
                inputReader.JumpPressed += HandleJumpPressed;
            }

            if (playerController != null)
            {
                playerController.AttackStarted += HandleAttackStarted;
                playerController.DodgeStarted += HandleDodgeStarted;
            }
        }

        private void OnDisable()
        {
            if (inputReader != null)
            {
                inputReader.JumpPressed -= HandleJumpPressed;
            }

            if (playerController != null)
            {
                playerController.AttackStarted -= HandleAttackStarted;
                playerController.DodgeStarted -= HandleDodgeStarted;
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
        }

        //缓存Animator参数的哈希值，以提高性能。
        private void CacheParameterHashes()
        {
            _moveSpeedHash = Animator.StringToHash(moveSpeedParameter);
            _isMovingHash = Animator.StringToHash(isMovingParameter);
            _attackHash = Animator.StringToHash(attackTriggerParameter);
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
            if (animator == null)
            {
                return;
            }

            animator.SetTrigger(_attackHash);
        }

        private void HandleDodgeStarted()
        {
            if (animator == null)
            {
                return;
            }

            animator.SetTrigger(_dodgeHash);
        }

        private void HandleJumpPressed()
        {
            if(animator == null || !playerController.IsGrounded)
            {
                return;
            }

            animator.SetTrigger(_jumpHash);
        }
    }
}

