using System;
using UnityEngine;

namespace OnlineActionRpg.Client.Combat
{
    // 训练木桩：用来验证“攻击成立 -> 命中 -> 扣血”的本地闭环。
    public sealed class TrainingDummy : MonoBehaviour, IDamageable
    {
        [Header("Health")]
        [SerializeField] private float maxHp = 100f;
        [SerializeField] private bool resetAfterDeath = true;
        [SerializeField] private float respawnDelay = 1.5f;

        [Header("Debug")]
        [SerializeField] private bool logHits = true;

        private float _respawnTimer;

        public float MaxHp => maxHp;
        public float CurrentHp { get; private set; }
        public bool IsAlive => CurrentHp > 0f;
        public int HitCount { get; private set; }
        public float LastHitDamage { get; private set; }

        // 参数依次是：木桩、本次命中数据、变化后的 HP。
        public event Action<TrainingDummy, HitInfo, float> Damaged;
        public event Action<TrainingDummy> Died;
        public event Action<TrainingDummy> Respawned;

        private void Awake()
        {
            CurrentHp = maxHp;
        }

        private void Update()
        {
            if (_respawnTimer <= 0f)
            {
                return;
            }

            _respawnTimer -= Time.deltaTime;

            if (_respawnTimer <= 0f)
            {
                ResetDummy();
            }
        }

        // HitBox / HurtBox 调用的统一入口。
        public void ApplyDamage(in HitInfo hit)
        {
            if (!hit.IsValid || !IsAlive)
            {
                return;
            }

            CurrentHp = Mathf.Max(0f, CurrentHp - hit.Damage);
            HitCount++;
            LastHitDamage = hit.Damage;

            if (logHits)
            {
                Debug.Log($"[TrainingDummy] {name} took {hit.Damage:0.##} from {hit.AttackerId} " +
                          $"({hit.AttackId}). HP={CurrentHp:0.#}/{maxHp:0.#}");
            }

            Damaged?.Invoke(this, hit, CurrentHp);

            if (CurrentHp > 0f)
            {
                return;
            }

            if (logHits)
            {
                Debug.Log($"[TrainingDummy] {name} is destroyed. Total hits={HitCount}.");
            }

            Died?.Invoke(this);

            if (resetAfterDeath)
            {
                _respawnTimer = respawnDelay;
            }
        }

        // 供后续 GM 指令或调试面板使用的显式重置。
        public void ResetDummy()
        {
            CurrentHp = maxHp;
            HitCount = 0;
            LastHitDamage = 0f;
            _respawnTimer = 0f;

            Respawned?.Invoke(this);
        }
    }
}