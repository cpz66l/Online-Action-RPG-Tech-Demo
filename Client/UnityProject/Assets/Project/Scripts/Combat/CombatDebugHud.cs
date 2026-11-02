using System.Text;
using OnlineActionRpg.Client.Battle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OnlineActionRpg.Client.Combat
{
    // 战斗调试 HUD：把动作状态、命中窗口和本次挥击命中数画在屏幕上。
    // 只读状态，不参与任何裁决；F1 显示 / 隐藏。
    public sealed class CombatDebugHud : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private LocalPlayerController playerController;
        [SerializeField] private HitBox hitBox;
        [SerializeField] private PlayerTargetLock targetLock;


        [Header("Display")]
        // 默认关闭：HUD 只在对状态有疑问时按 F1 打开，平时不占用画面。
        [SerializeField] private bool visible = false;
        [SerializeField] private Key toggleKey = Key.F1;
        [SerializeField] private Vector2 screenOffset = new Vector2(12f, 12f);

        [Header("Hit Log")]
        [SerializeField, Range(1, 16)] private int maxLogEntries = 8;

        private HitLogEntry[] _entries;
        private int _entryCount;
        private int _nextIndex;

        private struct HitLogEntry
        {
            public float Time;
            public string AttackId;
            public string TargetName;
            public float Damage;
        }

        private readonly StringBuilder _builder = new StringBuilder(256);
        private readonly GUIContent _content = new GUIContent();

        private string _cachedText = string.Empty;
        private GUIStyle _style;
        private Texture2D _background;

        private void Awake()
        {
            if (playerController == null)
            {
                playerController = GetComponent<LocalPlayerController>();
            }

            if (hitBox == null)
            {
                hitBox = GetComponentInChildren<HitBox>();
            }

            if (targetLock == null)
            {
                targetLock = GetComponent<PlayerTargetLock>();
            }


            _entries = new HitLogEntry[Mathf.Clamp(maxLogEntries, 1, 16)];

        }
        private void OnEnable()
        {
            if (hitBox == null)
            {
                hitBox = GetComponentInChildren<HitBox>();
            }

            if (hitBox == null)
            {
                return;
            }

            // 先退订再订阅，避免重绑或重生时重复挂载回调。
            hitBox.HitLanded -= HandleHitLanded;
            hitBox.HitLanded += HandleHitLanded;
        }

        private void OnDestroy()
        {
            if (_background != null)
            {
                Destroy(_background);
            }

            if (hitBox != null)
            {
                hitBox.HitLanded -= HandleHitLanded;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;//获得当前键盘设备

            // 项目是 Input System only，不能用 UnityEngine.Input 读键。
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
            {
                visible = !visible;
            }

            if (!visible || playerController == null)
            {
                _cachedText = string.Empty;
                return;
            }

            BuildText();
        }

        private void BuildText()
        {
            _builder.Clear();

            //ACTION 状态：Idle / Walk / Run / Dodge / Attack / Air
            _builder.Append("ACTION     ").AppendLine(ResolveActionState());

            //
            if (playerController.IsAttacking)
            {
                _builder.Append("ATTACK     ")
                    .Append(playerController.CurrentAttackVariant.ToString())
                    .Append("  ")
                    .Append(playerController.AttackProgress01.ToString("0.00"))
                    .AppendLine();

                _builder.Append("HIT WINDOW ")
                    .Append(playerController.IsInAttackHitWindow
                        ? "<color=#FFD24D>OPEN</color>"
                        : "closed")
                    .AppendLine();

                _builder.Append("COMBO      ")
                    .Append(playerController.ComboWindowRemaining.ToString("0.00"))
                    .AppendLine();
            }
            else
            {
                _builder.AppendLine("ATTACK     -");
                _builder.AppendLine("HIT WINDOW -");
                _builder.AppendLine("COMBO      -");
            }

            //攻击命中数：本次挥击命中数，0 表示未命中。
            int hitCount = hitBox != null ? hitBox.HitCountThisSwing : 0;
            _builder.Append("HIT SWING  ");

            if (hitCount > 0)
            {
                _builder.Append("<color=#FF4D4D>").Append(hitCount).Append("</color>");
            }
            else
            {
                _builder.Append('0');
            }

            _builder.AppendLine();

            //锁定目标：名字 + 距离，若无锁定目标则显示 "-"
            CombatTarget lockedTarget = targetLock != null ? targetLock.CurrentTarget : null;

            if (lockedTarget != null)
            {
                float targetDistance = (lockedTarget.LockPosition - transform.position).magnitude;

                _builder.Append("TARGET     ")
                    .Append(lockedTarget.name)
                    .Append("  ")
                    .Append(targetDistance.ToString("0.0"))
                    .AppendLine();
            }
            else
            {
                _builder.AppendLine("TARGET     -");
            }

            _builder.AppendLine();

            //速度
            _builder.Append("SPEED      ")
                .Append(playerController.CurrentMoveSpeed01.ToString("0.00"))
                .Append("  ")
                .Append(playerController.CurrentHorizontalVelocity.magnitude.ToString("0.0"))
                .AppendLine();

            //空间状态，以及垂直速度
            _builder.Append("GROUNDED   ")
                .Append(playerController.IsGrounded ? "yes" : "no")
                .Append("   Vy ")
                .Append(playerController.VerticalSpeed.ToString("0.0"));

            _builder.AppendLine();

            //命中日志
            AppendHitLog();

            _cachedText = _builder.ToString();
        }

        private string ResolveActionState()
        {
            if (playerController.IsDodging)
            {
                return "Dodge";
            }

            if (playerController.IsAttacking)
            {
                return "Attack";
            }

            if (!playerController.IsGrounded)
            {
                return "Air";
            }

            if (playerController.CurrentMoveSpeed01 > 0.05f)
            {
                return playerController.IsWalking ? "Walk" : "Run";
            }

            return "Idle";
        }

        private void OnGUI()
        {
            if (!visible || string.IsNullOrEmpty(_cachedText))
            {
                return;
            }

            EnsureStyle();

            _content.text = _cachedText;
            Vector2 size = _style.CalcSize(_content);

            Rect panel = new Rect(
                screenOffset.x,
                screenOffset.y,
                size.x + 20f,
                size.y + 16f);

            GUI.DrawTexture(panel, _background);
            GUI.Label(
                new Rect(panel.x + 10f, panel.y + 8f, size.x, size.y),
                _cachedText,
                _style);
        }

        // 确保 GUIStyle 和背景贴图已创建
        private void EnsureStyle()
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    richText = true,
                    alignment = TextAnchor.UpperLeft
                };
                _style.normal.textColor = new Color(0.92f, 0.94f, 0.96f);
            }

            if (_background == null)
            {
                _background = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                _background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.62f));
                _background.Apply();
            }
        }

        // 处理命中事件：记录命中日志
        private void HandleHitLanded(HurtBox target, HitInfo hit)
        {
            if (_entries == null || _entries.Length == 0)
            {
                return;
            }

            _entries[_nextIndex] = new HitLogEntry
            {
                Time = Time.time,
                AttackId = hit.AttackId,
                TargetName = target != null ? target.OwnerName : "(destroyed)",
                Damage = hit.Damage
            };

            _nextIndex = (_nextIndex + 1) % _entries.Length;// 环形缓冲

            if (_entryCount < _entries.Length)
            {
                _entryCount++;
            }
        }

        // 把命中日志写入文本
        private void AppendHitLog()
        {
            _builder.AppendLine("--- HIT LOG ---");

            if (_entryCount == 0)
            {
                _builder.Append("(no hit)");
                return;
            }

            // 从最新一条往回读，环形缓冲里最新的位置是 _nextIndex - 1。
            for (int i = 0; i < _entryCount; i++)
            {
                int index = (_nextIndex - 1 - i + _entries.Length) % _entries.Length;
                HitLogEntry entry = _entries[index];

                _builder.Append(entry.Time.ToString("0.0"))
                    .Append("s  ")
                    .Append(entry.AttackId)
                    .Append(" -> ")
                    .Append(entry.TargetName)
                    .Append("  ")
                    .Append(entry.Damage.ToString("0.##"))
                    .AppendLine();
            }
        }
    }
}
