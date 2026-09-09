using UnityEngine;

namespace OnlineActionRpg.Client.Battle
{
    // PlayerSpawnPoint 是训练场中的出生点标记。
    public sealed class PlayerSpawnPoint : MonoBehaviour
    {
        [SerializeField] private string spawnId = "spawn_local_01";
        [SerializeField] private bool isLocalPlayerDefault = true;
        [SerializeField] private Color gizmoColor = new Color(0.2f, 0.8f, 1f, 0.85f);

        public string SpawnId =>
            string.IsNullOrWhiteSpace(spawnId) ? gameObject.name : spawnId;

        public bool IsLocalPlayerDefault => isLocalPlayerDefault;

        public Vector3 Position => transform.position;
        public Quaternion Rotation => transform.rotation;

        //在编辑器中修改spawnId为空时，自动使用gameObject.name作为spawnId
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(spawnId))
            {
                spawnId = gameObject.name;
            }
        }

        //用于在scene视图的生点在编辑器中显示Gizmos，方便判断生点位置和朝向
        private void OnDrawGizmos()
        {
            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(transform.position, 0.35f);

            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1.2f);
        }
    }
}
