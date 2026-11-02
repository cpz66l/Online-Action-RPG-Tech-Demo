using OnlineActionRpg.Client.Combat;
using UnityEngine;

namespace OnlineActionRpg.Client.Battle
{
    // 锁定目标的视觉锚点：一个跟随目标的小球。
    // 独立对象，不挂在目标上——目标不应该知道“自己被锁定了”。
    public sealed class LockOnMarker : MonoBehaviour
    {
        [SerializeField] private PlayerTargetLock targetLock;

        [Header("Visual")]
        [SerializeField] private float markerSize = 0.22f;
        [SerializeField] private float heightOffset = 0.35f;
        [SerializeField] private Color markerColor = new Color(1f, 0.55f, 0.1f);

        private CombatTarget _target;
        private GameObject _marker;
        private Material _material;

        private void Awake()
        {
            if (targetLock == null)
            {
                targetLock = GetComponent<PlayerTargetLock>();
            }

            CreateMarker();
        }

        private void OnEnable()
        {
            if (targetLock == null)
            {
                return;
            }

            // 先退订再订阅，避免重绑或重生时重复挂载。
            targetLock.TargetChanged -= HandleTargetChanged;
            targetLock.TargetChanged += HandleTargetChanged;

            HandleTargetChanged(targetLock.CurrentTarget);
        }

        private void OnDisable()
        {
            if (targetLock != null)
            {
                targetLock.TargetChanged -= HandleTargetChanged;
            }

            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (_marker != null)
            {
                Destroy(_marker);
            }

            if (_material != null)
            {
                Destroy(_material);
            }
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                SetVisible(false);
                return;
            }

            _marker.transform.position = _target.LockPosition + Vector3.up * heightOffset;
        }

        private void HandleTargetChanged(CombatTarget target)
        {
            _target = target;
            SetVisible(target != null);
        }

        private void CreateMarker()
        {
            _marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _marker.name = "LockOnMarker";

            // 必须去掉 Collider：相机的 SphereCast 碰撞掩码是 ~0，
            // 留着它会让锁定后相机被自己画的球挡住而突然拉近。
            Collider markerCollider = _marker.GetComponent<Collider>();

            if (markerCollider != null)
            {
                Destroy(markerCollider);
            }

            _marker.transform.localScale = Vector3.one * markerSize;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader != null)
            {
                _material = new Material(shader);
                _material.SetColor("_BaseColor", markerColor);
                _marker.GetComponent<Renderer>().sharedMaterial = _material;
            }
            else
            {
                _marker.GetComponent<Renderer>().material.color = markerColor;
            }

            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (_marker != null && _marker.activeSelf != visible)
            {
                _marker.SetActive(visible);
            }
        }
    }
}
