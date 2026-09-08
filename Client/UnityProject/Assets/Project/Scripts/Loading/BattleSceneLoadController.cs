using OnlineActionRpg.Client.Resource;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OnlineActionRpg.Client.Loading
{
    // BattleSceneLoadController 负责把 BattleStartNtf 转成实际场景加载动作。
    // 它是流程控制层，不负责 UI 显示，也不负责角色生成和战斗逻辑。
    public sealed class BattleSceneLoadController : MonoBehaviour
    {
        [SerializeField] private LoadingClient loadingClient;
        [SerializeField] private AddressablesResourceService resourceService;
        [SerializeField] private LoadSceneMode loadSceneMode = LoadSceneMode.Additive;
        [SerializeField] private Camera lobbyCamera;
        [SerializeField] private AudioListener lobbyAudioListener;
        [SerializeField] private GameObject[] lobbyObjectsToHideAfterBattleLoaded;

        private bool _isLoadingBattleScene;

        private void Awake()
        {
            if (loadingClient == null)
            {
                loadingClient = FindFirstObjectByType<LoadingClient>();
            }

            if (resourceService == null)
            {
                resourceService = FindFirstObjectByType<AddressablesResourceService>();
            }

            if (lobbyCamera == null)
            {
                lobbyCamera = Camera.main;
            }

            if (lobbyAudioListener == null && lobbyCamera != null)
            {
                lobbyAudioListener = lobbyCamera.GetComponent<AudioListener>();
            }

            if (loadingClient != null)
            {
                loadingClient.BattleStartReceived += HandleBattleStartReceived;
            }
        }

        private void OnDestroy()
        {
            if (loadingClient != null)
            {
                loadingClient.BattleStartReceived -= HandleBattleStartReceived;
            }
        }

        private async void HandleBattleStartReceived(BattleStartInfo info)
        {
            if (_isLoadingBattleScene)
            {
                return;
            }

            if (resourceService == null)
            {
                Debug.LogWarning("Cannot load battle scene: AddressablesResourceService is missing.");
                return;
            }

            LoadBattleSceneTaskInfo currentTask = loadingClient != null
                ? loadingClient.CurrentTask
                : default;

            if (!currentTask.IsValid)
            {
                Debug.LogWarning("Cannot load battle scene: current loading task is invalid.");
                return;
            }

            if (currentTask.BattleId != info.BattleId || currentTask.RoomId != info.RoomId)
            {
                Debug.LogWarning(
                    $"BattleStartNtf does not match current loading task. " +
                    $"TaskBattleId={currentTask.BattleId}, NtfBattleId={info.BattleId}, " +
                    $"TaskRoomId={currentTask.RoomId}, NtfRoomId={info.RoomId}");
                return;
            }

            _isLoadingBattleScene = true;

            Debug.Log($"Loading battle scene from BattleStartNtf. SceneKey={currentTask.SceneKey}");

            //开始加载战斗场景
            bool loaded = await resourceService.LoadBattleSceneAsync(
                currentTask.SceneKey,
                loadSceneMode);

            _isLoadingBattleScene = false;

            if (!loaded)
            {
                Debug.LogWarning($"Battle scene load failed: {resourceService.LastError}");
                return;
            }

            DisableLobbyPresentation();

            Debug.Log($"Battle scene loaded. BattleId={info.BattleId}, SceneKey={currentTask.SceneKey}");
        }
        private void DisableLobbyPresentation()
        {
            if (lobbyCamera != null)
            {
                lobbyCamera.enabled = false;
            }

            if (lobbyAudioListener != null)
            {
                lobbyAudioListener.enabled = false;
            }

            if (lobbyObjectsToHideAfterBattleLoaded == null)
            {
                return;
            }

            for (int i = 0; i < lobbyObjectsToHideAfterBattleLoaded.Length; i++)
            {
                GameObject target = lobbyObjectsToHideAfterBattleLoaded[i];

                if (target != null)
                {
                    target.SetActive(false);
                }
            }
        }
    }
}
