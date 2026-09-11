using System.Collections.Generic;
using OnlineActionRpg.Client.Battle;
using UnityEngine;

namespace OnlineActionRpg.Client.Combat
{
    // HitBox 表达“本次攻击能不能命中”。
    // 它只在 LocalPlayerController 确认的命中窗口内做检测，
    // 且同一次挥击对同一个 HurtBox 只结算一次，避免一次按键打出多段伤害。
    [RequireComponent(typeof(BoxCollider))]
    public sealed class HitBox : MonoBehaviour
    {
        [Header("Owner")]
        [SerializeField] private LocalPlayerController playerController;
        [SerializeField] private string attackerId = string.Empty;

        [Header("Hit")]
        [SerializeField] private float damage = 12f;
        [SerializeField] private LayerMask queryMask = ~0;
        [SerializeField] private QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.Collide;
        [SerializeField] private int maxTargetsPerSwing = 4;

        [Header("Debug")]
        [SerializeField] private bool drawDebug = true;
        [SerializeField] private Color debugColor = new Color(1f, 0.35f, 0.2f, 0.85f);

        private BoxCollider _boxCollider;
        private readonly HashSet<HurtBox> _hitThisSwing = new();//用于一次攻击窗口的去重

        public bool IsHitWindowOpen => playerController != null && playerController.IsInAttackHitWindow;

        private void Awake()
        {
            _boxCollider = GetComponent<BoxCollider>();

            if (_boxCollider != null && !_boxCollider.isTrigger)
            {
                _boxCollider.isTrigger = true;
            }

            if (playerController == null)
            {
                playerController = GetComponentInParent<LocalPlayerController>();
            }
        }

        private void OnEnable()
        {
            if (playerController != null)
            {
                playerController.AttackHitWindowOpened += HandleHitWindowOpened;
                playerController.AttackHitWindowClosed += HandleHitWindowClosed;
            }
        }

        private void OnDisable()
        {
            if (playerController != null)
            {
                playerController.AttackHitWindowOpened -= HandleHitWindowOpened;
                playerController.AttackHitWindowClosed -= HandleHitWindowClosed;
            }

            _hitThisSwing.Clear();
        }

        private void HandleHitWindowOpened(LocalPlayerController.AttackVariant variant)
        {
            // 每次窗口开启都当成一次全新的挥击，清空“本次已命中”集合。
            _hitThisSwing.Clear();
        }

        private void HandleHitWindowClosed()
        {
            _hitThisSwing.Clear();
        }

        private void FixedUpdate()
        {
            if (!IsHitWindowOpen)
            {
                return;
            }

            ResolveHits();
        }

        private void ResolveHits()
        {
            if (_boxCollider == null)
            {
                return;
            }

            Vector3 center = transform.TransformPoint(_boxCollider.center);
            //确定碰撞盒的半尺寸，考虑到物体的缩放
            Vector3 halfExtents = Vector3.Scale(_boxCollider.size, transform.lossyScale) * 0.5f;

            //OverlapBox 检测所有与碰撞盒重叠的 Collider
            Collider[] overlaps = Physics.OverlapBox(
                center,
                halfExtents,
                transform.rotation,
                queryMask,
                queryTriggerInteraction);

            int hitCount = 0;

            for (int i = 0; i < overlaps.Length; i++)
            {
                if (hitCount >= maxTargetsPerSwing)
                {
                    break;
                }

                //从检测到的 Collider 中获取 HurtBox 组件
                HurtBox hurtBox = overlaps[i].GetComponentInParent<HurtBox>();

                //如果没有HurtBox组件，或者这个HurtBox已经在本次挥击中被命中过，则跳过
                if (hurtBox == null || _hitThisSwing.Contains(hurtBox))
                {
                    continue;
                }
                //尝试命中这个 HurtBox，如果命中失败，则跳过
                if (!TryHit(hurtBox, center))
                {
                    continue;
                }

                _hitThisSwing.Add(hurtBox);
                hitCount++;
            }
        }

        private bool TryHit(HurtBox hurtBox, Vector3 hitBoxCenter)
        {
            Vector3 targetPoint = hurtBox.transform.position;
            Vector3 direction = targetPoint - hitBoxCenter;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;

            HitInfo hit = new HitInfo(
                damage,
                ResolveAttackerId(),
                playerController != null ? playerController.transform : transform,
                playerController != null ? playerController.CurrentAttackVariant.ToString() : string.Empty,
                targetPoint,
                direction);

            if (!hurtBox.TryReceiveHit(hit))
            {
                return false;
            }

            Debug.Log($"[Combat] Hit {hurtBox.name} for {damage:0.##}. " +
                      $"Attack={hit.AttackId}, Attacker={hit.AttackerId}");

            return true;
        }

        private string ResolveAttackerId()
        {
            if (!string.IsNullOrWhiteSpace(attackerId))
            {
                return attackerId;
            }

            return playerController != null ? playerController.name : name;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawDebug)
            {
                return;
            }

            BoxCollider box = _boxCollider != null ? _boxCollider : GetComponent<BoxCollider>();

            if (box == null)
            {
                return;
            }

            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
            Gizmos.color = IsHitWindowOpen ? Color.red : debugColor;
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}
