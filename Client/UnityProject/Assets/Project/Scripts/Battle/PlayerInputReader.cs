using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnlineActionRpg.Client.Battle
{
    // PlayerInputReader 是 04C 前置输入层。
    // 它统一监听 Input System，并把输入状态广播给移动、动画、攻击等模块。
    // 当前只做本地事件广播，不发送网络消息。
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [Header("Input Actions")]
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference lookAction;
        [SerializeField] private InputActionReference attackAction;
        [SerializeField] private InputActionReference dodgeAction;
        [SerializeField] private InputActionReference jumpAction;
        [SerializeField] private InputActionReference walkModeAction;

        public event Action<Vector2> MoveChanged;
        public event Action<Vector2> LookChanged;
        public event Action AttackPressed;
        public event Action DodgePressed;
        public event Action JumpPressed;
        public event Action<bool> WalkModeChanged;

        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool IsWalkModeHeld { get; private set; }

        public bool HasMoveInput => Move.sqrMagnitude > 0.0001f;

        private InputAction _walkModeRuntimeAction;

        private void OnEnable()
        {
            EnableAction(moveAction);
            EnableAction(lookAction);
            EnableAction(attackAction);
            EnableAction(dodgeAction);
            EnableAction(jumpAction);

            _walkModeRuntimeAction = ResolveAction(walkModeAction, "WalkMode");
            EnableAction(_walkModeRuntimeAction);

            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();

            DisableAction(moveAction);
            DisableAction(lookAction);
            DisableAction(attackAction);
            DisableAction(dodgeAction);
            DisableAction(jumpAction);
            DisableAction(_walkModeRuntimeAction);

            Move = Vector2.zero;
            Look = Vector2.zero;
            IsWalkModeHeld = false;
            _walkModeRuntimeAction = null;
        }

        private void Subscribe()
        {
            if (moveAction != null && moveAction.action != null)
            {
                moveAction.action.performed += HandleMovePerformed;
                moveAction.action.canceled += HandleMoveCanceled;
            }

            if (lookAction != null && lookAction.action != null)
            {
                lookAction.action.performed += HandleLookPerformed;
                lookAction.action.canceled += HandleLookCanceled;
            }

            if (attackAction != null && attackAction.action != null)
            {
                attackAction.action.performed += HandleAttackPerformed;
            }

            if (dodgeAction != null && dodgeAction.action != null)
            {
                dodgeAction.action.performed += HandleDodgePerformed;
            }

            if (jumpAction != null && jumpAction.action != null)
            {
                jumpAction.action.performed += HandleJumpPerformed;
            }

            if (_walkModeRuntimeAction != null)
            {
                _walkModeRuntimeAction.performed += HandleWalkModePerformed;
                _walkModeRuntimeAction.canceled += HandleWalkModeCanceled;
            }
        }

        private void Unsubscribe()
        {
            if (moveAction != null && moveAction.action != null)
            {
                moveAction.action.performed -= HandleMovePerformed;
                moveAction.action.canceled -= HandleMoveCanceled;
            }

            if (lookAction != null && lookAction.action != null)
            {
                lookAction.action.performed -= HandleLookPerformed;
                lookAction.action.canceled -= HandleLookCanceled;
            }

            if (attackAction != null && attackAction.action != null)
            {
                attackAction.action.performed -= HandleAttackPerformed;
            }

            if (dodgeAction != null && dodgeAction.action != null)
            {
                dodgeAction.action.performed -= HandleDodgePerformed;
            }

            if (jumpAction != null && jumpAction.action != null)
            {
                jumpAction.action.performed -= HandleJumpPerformed;
            }

            if (_walkModeRuntimeAction != null)
            {
                _walkModeRuntimeAction.performed -= HandleWalkModePerformed;
                _walkModeRuntimeAction.canceled -= HandleWalkModeCanceled;
            }
        }

        private void HandleMovePerformed(InputAction.CallbackContext context)
        {
            Move = context.ReadValue<Vector2>();
            Move = Move.sqrMagnitude > 1f ? Move.normalized : Move;
            MoveChanged?.Invoke(Move);
        }

        private void HandleMoveCanceled(InputAction.CallbackContext context)
        {
            Move = Vector2.zero;
            MoveChanged?.Invoke(Move);
        }

        private void HandleLookPerformed(InputAction.CallbackContext context)
        {
            Look = context.ReadValue<Vector2>();
            LookChanged?.Invoke(Look);
        }

        private void HandleLookCanceled(InputAction.CallbackContext context)
        {
            Look = Vector2.zero;
            LookChanged?.Invoke(Look);
        }

        private void HandleAttackPerformed(InputAction.CallbackContext context)
        {
            AttackPressed?.Invoke();
        }

        private void HandleDodgePerformed(InputAction.CallbackContext context)
        {
            DodgePressed?.Invoke();
        }

        private void HandleJumpPerformed(InputAction.CallbackContext context)
        {
            JumpPressed?.Invoke();
        }

        private void HandleWalkModePerformed(InputAction.CallbackContext context)
        {
            SetWalkMode(true);
        }

        private void HandleWalkModeCanceled(InputAction.CallbackContext context)
        {
            SetWalkMode(false);
        }

        private void SetWalkMode(bool isHeld)
        {
            if (IsWalkModeHeld == isHeld)
            {
                return;
            }

            IsWalkModeHeld = isHeld;
            WalkModeChanged?.Invoke(IsWalkModeHeld);
        }

        private InputAction ResolveAction(InputActionReference actionReference, string actionName)
        {
            if (actionReference != null && actionReference.action != null)
            {
                return actionReference.action;
            }

            return moveAction != null && moveAction.action != null
                ? moveAction.action.actionMap.FindAction(actionName, false)
                : null;
        }

        private static void EnableAction(InputActionReference actionReference)
        {
            if (actionReference != null && actionReference.action != null)
            {
                actionReference.action.Enable();
            }
        }

        private static void DisableAction(InputActionReference actionReference)
        {
            if (actionReference != null && actionReference.action != null)
            {
                actionReference.action.Disable();
            }
        }

        private static void EnableAction(InputAction action)
        {
            if (action != null)
            {
                action.Enable();
            }
        }

        private static void DisableAction(InputAction action)
        {
            if (action != null)
            {
                action.Disable();
            }
        }
    }
}