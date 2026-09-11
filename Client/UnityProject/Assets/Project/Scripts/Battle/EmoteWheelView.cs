using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OnlineActionRpg.Client.Battle
{
    public sealed class EmoteWheelView : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerInputReader inputReader;
        [SerializeField] private LocalEmoteController emoteController;
        [SerializeField] private Transform target;
        [SerializeField] private Canvas canvas;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform wheelRoot;
        [SerializeField] private Image[] slotIcons;
        [SerializeField] private Image[] slotHighlights;
        [SerializeField] private TMP_Text selectedNameLabel;

        [Header("Selection")]
        [SerializeField] private int defaultSelectionIndex;
        [SerializeField] private float centerDeadZone = 24f;

        private bool _isOpen;
        private int _selectedIndex;

        private void Awake()
        {
            ResolveReferences();
            Hide();
        }

        private void OnDisable()
        {
            UnsubscribeInput();
            Hide();
        }

        // 统一处理轮盘输入的退订，避免 SetTarget 被多次调用或对象禁用后残留重复回调。
        private void UnsubscribeInput()
        {
            if (inputReader == null)
            {
                return;
            }

            inputReader.EmoteWheelPressed -= HandleWheelPressed;
            inputReader.EmoteWheelReleased -= HandleWheelReleased;
            inputReader.EmotePointerChanged -= HandlePointerChanged;
        }

        private void ResolveReferences()
        {
            if (canvas == null)
            {
                canvas = GetComponentInParent<Canvas>();
            }

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }

            if (wheelRoot == null)
            {
                wheelRoot = transform as RectTransform;
            }
        }

        private void HandleWheelPressed()
        {
            if (emoteController == null || !emoteController.CanStartEmote())
            {
                return;
            }

            _isOpen = true;
            _selectedIndex = Mathf.Clamp(defaultSelectionIndex, 0, GetSlotCount() - 1);

            RefreshSlots();
            Show();
            //显示光标
            ThirdPersonCameraController.UnlockCursor();
        }

        private void HandleWheelReleased()
        {
            if (!_isOpen)
            {
                return;
            }

            EmoteDefinition[] emotes = emoteController.Emotes;

            if (_selectedIndex >= 0 && _selectedIndex < emotes.Length)
            {
                //开始播放选中的表情动作
                emoteController.TryStartEmote(emotes[_selectedIndex]);
            }

            //隐藏并锁住光标
            ThirdPersonCameraController.LockCursor();
            Hide();
        }

        //处理指针位置变化，更新选中的表情动作槽位
        private void HandlePointerChanged(Vector2 screenPosition)
        {
            if (!_isOpen || wheelRoot == null)
            {
                return;
            }

            //将屏幕坐标转换为轮盘根节点的本地坐标
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            //将屏幕坐标转换为轮盘根节点的本地坐标，计算鼠标指针相对于轮盘中心的方向向量
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    wheelRoot,
                    screenPosition,
                    eventCamera,
                    out Vector2 localPoint))
            {
                return;
            }

            //根据指针位置计算选中的槽位索引，如果指针在中心死区内，
            //则使用默认选择索引，否则根据方向计算索引
            if (localPoint.magnitude < centerDeadZone)
            {
                _selectedIndex = Mathf.Clamp(defaultSelectionIndex, 0, GetSlotCount() - 1);
            }
            else
            {
                _selectedIndex = GetIndexFromDirection(localPoint);
            }

            RefreshSlots();
        }

        //根据指针方向计算选中的槽位索引
        private int GetIndexFromDirection(Vector2 direction)
        {
            int count = GetSlotCount();

            if (count <= 0)
            {
                return -1;
            }

            //将方向向量转换为角度，并计算从顶部顺时针旋转的角度
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            float fromTopClockwise = Mathf.Repeat(90f - angle, 360f);
            float step = 360f / count;

            //根据角度计算选中的槽位索引，使用四舍五入的方式，并确保索引在有效范围内
            return Mathf.FloorToInt((fromTopClockwise + step * 0.5f) / step) % count;
        }

        private int GetSlotCount()
        {
            return emoteController != null && emoteController.Emotes != null
                ? emoteController.Emotes.Length
                : 0;
        }

        //刷新槽位的显示状态和选中状态
        private void RefreshSlots()
        {
            if (emoteController == null || emoteController.Emotes == null)
            {
                return;
            }

            EmoteDefinition[] emotes = emoteController.Emotes;

            // slotIcons 和 slotHighlights 是两个独立数组，Inspector 中长度不一致时会越界。
            // 这里取两者较大长度，并对每个槽位分别做长度与空值保护。
            int iconCount = slotIcons != null ? slotIcons.Length : 0;
            int highlightCount = slotHighlights != null ? slotHighlights.Length : 0;
            int slotCount = Mathf.Max(iconCount, highlightCount);

            for (int i = 0; i < slotCount; i++)
            {
                bool hasEmote = i < emotes.Length && emotes[i] != null;

                if (i < iconCount && slotIcons[i] != null)
                {
                    slotIcons[i].enabled = hasEmote && emotes[i].Icon != null;
                    slotIcons[i].sprite = hasEmote ? emotes[i].Icon : null;
                }

                if (i < highlightCount && slotHighlights[i] != null)
                {
                    slotHighlights[i].enabled = _isOpen && i == _selectedIndex;
                }
            }

            if (selectedNameLabel != null)
            {
                selectedNameLabel.text = _selectedIndex >= 0 && _selectedIndex < emotes.Length
                    ? emotes[_selectedIndex].DisplayName
                    : string.Empty;
            }
        }

        private void Show()
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        private void Hide()
        {
            _isOpen = false;

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
        }

        public void SetTarget(Transform followTarget)
        {
            target = followTarget;

            ResolveReferencesFromTarget();
        }

        private void ResolveReferencesFromTarget()
        {
            if (inputReader == null && target != null)
            {
                inputReader = target.GetComponent<PlayerInputReader>();
            }

            if (emoteController == null && target != null)
            {
                emoteController = target.GetComponent<LocalEmoteController>();
            }

            // 先退订再订阅：SetTarget 可能在重绑流程中被再次调用，
            // 直接 += 会让同一组回调挂载多次，导致一次输入触发多轮轮盘刷新。
            UnsubscribeInput();

            if (inputReader != null)
            {
                inputReader.EmoteWheelPressed += HandleWheelPressed;
                inputReader.EmoteWheelReleased += HandleWheelReleased;
                inputReader.EmotePointerChanged += HandlePointerChanged;
            }
        }
    }
}
