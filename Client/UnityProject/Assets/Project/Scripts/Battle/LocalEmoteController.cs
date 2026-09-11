using System;
using UnityEngine;

namespace OnlineActionRpg.Client.Battle
{
    public sealed class LocalEmoteController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerInputReader inputReader;
        [SerializeField] private LocalPlayerController playerController;

        [Header("Emotes")]
        [SerializeField] private EmoteDefinition[] emotes;
        [SerializeField] private float moveCancelThreshold = 0.1f;

        private EmoteDefinition _currentEmote;
        private float _emoteTimer;

        public event Action<EmoteDefinition> EmoteStarted;
        public event Action EmoteCanceled;

        public EmoteDefinition[] Emotes => emotes;
        public bool IsEmoting => _currentEmote != null;
        public EmoteDefinition CurrentEmote => _currentEmote;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();

            if (inputReader != null)
            {
                inputReader.MoveChanged += HandleMoveChanged;
            }

            if (playerController != null)
            {
                playerController.AttackStarted += HandleCombatActionStarted;
                playerController.DodgeStarted += HandleCombatActionStarted;
                playerController.JumpStarted += HandleCombatActionStarted;
            }

        }

        private void OnDisable()
        {
            if (inputReader != null)
            {
                inputReader.MoveChanged -= HandleMoveChanged;
            }

            if (playerController != null)
            {
                playerController.AttackStarted -= HandleCombatActionStarted;
                playerController.DodgeStarted -= HandleCombatActionStarted;
                playerController.JumpStarted -= HandleCombatActionStarted;
            }
        }

        private void Update()
        {
            if (_currentEmote == null)
            {
                return;
            }

            if (playerController != null && !playerController.IsGrounded)
            {
                CancelEmote();
                return;
            }

            //如果当前表情动作不是循环的，并且有持续时间，则更新计时器。
            if (!_currentEmote.Loop && _currentEmote.Duration > 0f)
            {
                _emoteTimer -= Time.deltaTime;

                if (_emoteTimer <= 0f)
                {
                    CancelEmote();
                }
            }
        }

        public bool CanStartEmote()
        {
            if (playerController == null)
            {
                return true;
            }

            //检查玩家是否在地面上，并且没有进行攻击或闪避动作。
            return playerController.IsGrounded
                && !playerController.IsAttacking
                && !playerController.IsDodging;
        }

        public bool TryStartEmote(EmoteDefinition emote)
        {
            if (emote == null || !CanStartEmote())
            {
                return false;
            }

            if (_currentEmote != null)
            {
                CancelEmote();
            }

            _currentEmote = emote;
            _emoteTimer = emote.Duration;
            EmoteStarted?.Invoke(_currentEmote);
            return true;
        }

        //取消当前的表情动作，用于在移动或其他条件下中断表情动作。
        public void CancelEmote()
        {
            if (_currentEmote == null)
            {
                return;
            }

            _currentEmote = null;
            _emoteTimer = 0f;
            EmoteCanceled?.Invoke();
        }

        private void ResolveReferences()
        {
            if (inputReader == null)
            {
                inputReader = GetComponent<PlayerInputReader>();
            }

            if (playerController == null)
            {
                playerController = GetComponent<LocalPlayerController>();
            }

        }

        private void HandleMoveChanged(Vector2 move)
        {
            if (_currentEmote == null || !_currentEmote.CancelOnMove)
            {
                return;
            }

            //如果移动大于取消表情的阈值，则取消当前的表情动作。
            if (move.sqrMagnitude >= moveCancelThreshold * moveCancelThreshold)
            {
                CancelEmote();
            }
        }

        private void HandleCombatActionStarted()
        {
            if (_currentEmote == null || !_currentEmote.CancelOnCombatAction)
            {
                return;
            }

            CancelEmote();
        }

    }
}