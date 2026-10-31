using System.Collections.Generic;
using OnlineActionRpg.Client.Battle;
using UnityEngine;
using System;

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

        private BoxCollider _boxCollider;
        private readonly HashSet<HurtBox> _hitThisSwing = new();//用于一次攻击窗口的去重

        // 这个属性用与桥接 LocalPlayerController 的攻击窗口状态，决定是否进行命中检测。
        public bool IsHitWindowOpen => playerController != null && playerController.IsInAttackHitWindow;

        // 本次挥击的命中情况，供调试可视化与 HUD 读取。
        public bool HasHitThisSwing => _hitThisSwing.Count > 0;
        public int HitCountThisSwing => _hitThisSwing.Count;

        // 一次挥击确认命中后广播，携带受击方与命中数据。
        // 调试 HUD 与后续战斗事件同步共用这一条数据源。
        public event Action<HurtBox, HitInfo> HitLanded;


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

            Debug.Log($"[Combat] Hit {hurtBox.OwnerName} for {damage:0.##}. " +
                      $"Attack={hit.AttackId}, Attacker={hit.AttackerId}");

            // 先记账再广播：保证订阅方在回调里读到的 HitCountThisSwing 已是更新后的值。
            _hitThisSwing.Add(hurtBox);
            HitLanded?.Invoke(hurtBox, hit);

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


        private static readonly Color WindowClosedColor = new Color(0.55f, 0.55f, 0.55f, 0.85f);//灰色表示窗口未开
        private static readonly Color WindowOpenNoHitColor = new Color(1f, 0.85f, 0.2f, 0.9f);//黄色表示窗口开着但未命中
        private static readonly Color WindowOpenHitColor = new Color(1f, 0.25f, 0.2f, 0.95f);//红色表示本次挥击已命中

        private void OnDrawGizmos()
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

            // 三态：灰=窗口未开，黄=窗口开着未命中，红=本次挥击已命中。
            // 红态用“本次挥击”而不是“本帧”，否则只亮一帧，肉眼抓不到。
            Color color = !IsHitWindowOpen
                ? WindowClosedColor
                : (HasHitThisSwing ? WindowOpenHitColor : WindowOpenNoHitColor);

            Matrix4x4 previousMatrix = Gizmos.matrix;//保存当前 Gizmos 的矩阵状态
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
            //设置 Gizmos 的矩阵为物体的世界变换矩阵

            // 绘制半透明填充的立方体
            Color fill = color;
            fill.a = 0.12f;
            Gizmos.color = fill;
            Gizmos.DrawCube(box.center, box.size);

            // 绘制线框立方体
            Gizmos.color = color;
            Gizmos.DrawWireCube(box.center, box.size);

            // 必须复原，否则会影响同一帧后续其它 Gizmo 的绘制坐标系。
            Gizmos.matrix = previousMatrix;
        }

    }
}
