using TMPro;
using UnityEngine;

namespace OnlineActionRpg.Client.Combat
{
    // 只负责把 TrainingDummy 的 HP 显示到世界空间文本上，不参与任何伤害计算。
    public sealed class TrainingDummyHpDisplay : MonoBehaviour
    {
        [SerializeField] private TrainingDummy dummy;
        [SerializeField] private TMP_Text label;
        [SerializeField] private bool faceCamera = true;
        [SerializeField] private string format = "HP {0:0.#} / {1:0.#}";

        private Camera _mainCamera;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            Refresh();
        }

        private void OnEnable()
        {
            ResolveReferences();


            if (dummy != null)
            {
                dummy.Damaged += HandleDamaged;
                dummy.Respawned += HandleRespawned;
            }
        }

        private void OnDisable()
        {
            if (dummy != null)
            {
                dummy.Damaged -= HandleDamaged;
                dummy.Respawned -= HandleRespawned;
            }
        }

        private void LateUpdate()
        {
            if (!faceCamera || label == null)
            {
                return;
            }

            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }

            if (_mainCamera == null)
            {
                return;
            }

            Transform labelTransform = label.transform;
            Vector3 direction = labelTransform.position - _mainCamera.transform.position;

            if (direction.sqrMagnitude > 0.0001f)
            {
                labelTransform.rotation = Quaternion.LookRotation(direction);
            }
        }

        private void HandleDamaged(TrainingDummy source, HitInfo hit, float hp)
        {
            Refresh();
        }

        private void HandleRespawned(TrainingDummy source)
        {
            Refresh();
        }

        private void Refresh()
        {
            if (dummy == null || label == null)
            {
                return;
            }

            label.text = string.Format(format, dummy.CurrentHp, dummy.MaxHp);
        }

        private void ResolveReferences()
        {
            if (dummy == null)
            {
                dummy = GetComponentInParent<TrainingDummy>();
            }

            if (label == null)
            {
                label = GetComponentInChildren<TMP_Text>();
            }
        }
    }
}