using UnityEngine;

namespace OnlineActionRpg.Client.Combat
{
    // 木桩的受击表现桥。
    // 和玩家侧的 CharacterAnimatorDriver 保持同一原则：
    // TrainingDummy 决定“有没有被打中、扣了多少、死没死”，
    // Driver 只负责把已经确认的事实翻译成 Animator 参数，不反向决定伤害。
    [RequireComponent(typeof(TrainingDummy))]
    public sealed class TrainingDummyAnimatorDriver : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TrainingDummy dummy;
        [SerializeField] private Animator animator;

        [Header("Animator Parameters")]
        [SerializeField] private string hitTriggerParameter = "Hit";
        [SerializeField] private string dieTriggerParameter = "Die";
        [SerializeField] private string reviveTriggerParameter = "Revive";

        [Header("Hit Direction")]
        // 打开后必须先在 Animator 里建好 HitDirection 这个 Int 参数，
        // 否则 Unity 会打印“参数不存在”的警告。
        [SerializeField] private bool useHitDirection;
        [SerializeField] private string hitDirectionParameter = "HitDirection";

        [Header("Debug")]
        [SerializeField] private bool logStateChanges;

        private int _hitTriggerHash;
        private int _dieTriggerHash;
        private int _reviveTriggerHash;
        private int _hitDirectionHash;

        private void Awake()
        {
            ResolveReferences();
            CacheParameterHashes();

            // 木桩的摆放位置应该是权威的：受击和死亡动画不应该把它推离原位。
            // 玩家侧同样关闭了 Root Motion，两边保持一致。
            if (animator != null)
            {
                animator.applyRootMotion = false;
            }
        }

        private void OnEnable()
        {
            ResolveReferences();

            if (dummy != null)
            {
                dummy.Damaged += HandleDamaged;
                dummy.Died += HandleDied;
                dummy.Respawned += HandleRespawned;
            }
        }

        private void OnDisable()
        {
            if (dummy != null)
            {
                dummy.Damaged -= HandleDamaged;
                dummy.Died -= HandleDied;
                dummy.Respawned -= HandleRespawned;
            }
        }

        private void Reset()
        {
            ResolveReferences();
        }

        private void ResolveReferences()
        {
            if (dummy == null)
            {
                dummy = GetComponent<TrainingDummy>();
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }
        }

        // 和玩家侧一样，把高频访问的参数和 Trigger 在 Awake 里一次性转成整数 Hash，
        // 运行时不再按字符串查找。
        private void CacheParameterHashes()
        {
            _hitTriggerHash = Animator.StringToHash(hitTriggerParameter);
            _dieTriggerHash = Animator.StringToHash(dieTriggerParameter);
            _reviveTriggerHash = Animator.StringToHash(reviveTriggerParameter);
            _hitDirectionHash = Animator.StringToHash(hitDirectionParameter);
        }

        private void HandleDamaged(TrainingDummy source, HitInfo hit, float hpAfter)
        {
            if (animator == null)
            {
                return;
            }

            // 致命一击时不播受击反应，这一次命中的表现交给 Died 事件播死亡动画。
            // 否则同一帧会同时设置 Hit 和 Die 两个 Trigger，由 AnyState 的排列顺序决定谁赢，行为不稳定。
            if (!source.IsAlive)
            {
                return;
            }

            if (useHitDirection)
            {
                animator.SetInteger(_hitDirectionHash, ResolveHitDirection(source.transform, hit));
            }

            // 受击可以打断上一次受击反应；先清掉可能残留的 Die，避免它抢占表现。
            animator.ResetTrigger(_dieTriggerHash);
            animator.SetTrigger(_hitTriggerHash);

            if (logStateChanges)
            {
                Debug.Log($"[TrainingDummy] Hit reaction triggered. HP={hpAfter:0.#}, Attack={hit.AttackId}");
            }
        }

        private void HandleDied(TrainingDummy source)
        {
            if (animator == null)
            {
                return;
            }

            // 死亡优先级最高：清掉还没被消费的受击 Trigger，保证进入 Death 而不是 Hit_Chest。
            animator.ResetTrigger(_hitTriggerHash);
            animator.SetTrigger(_dieTriggerHash);

            if (logStateChanges)
            {
                Debug.Log("[TrainingDummy] Death reaction triggered.");
            }
        }

        private void HandleRespawned(TrainingDummy source)
        {
            if (animator == null)
            {
                return;
            }

            animator.ResetTrigger(_hitTriggerHash);
            animator.ResetTrigger(_dieTriggerHash);
            animator.SetTrigger(_reviveTriggerHash);

            if (logStateChanges)
            {
                Debug.Log("[TrainingDummy] Revive reaction triggered.");
            }
        }

        // 把命中来向换算成 4 个方位，供后续接不同方向的受击动作。
        // 0 = 正面, 1 = 背面, 2 = 左侧, 3 = 右侧。
        private static int ResolveHitDirection(Transform dummyTransform, in HitInfo hit)
        {
            // HitDirection 是“从攻击者指向木桩”的方向，取反才是“木桩被从哪一侧打中”。
            Vector3 localDirection = -dummyTransform.InverseTransformDirection(hit.HitDirection);
            localDirection.y = 0f;

            if (localDirection.sqrMagnitude <= 0.0001f)
            {
                return 0;
            }

            float angle = Vector3.SignedAngle(Vector3.forward, localDirection.normalized, Vector3.up);

            if (angle >= -45f && angle < 45f)
            {
                return 0;   // 正面
            }

            if (angle >= 45f && angle < 135f)
            {
                return 3;   // 右侧
            }

            if (angle >= -135f && angle < -45f)
            {
                return 2;   // 左侧
            }

            return 1;       // 背面
        }
    }
}