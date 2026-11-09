using System.Text;
using OnlineActionRpg.Client.Account;
using OnlineActionRpg.Client.Loading;
using UnityEngine;

namespace OnlineActionRpg.Client.Battle
{
    // BattleBootstrap 是战斗场景的启动入口。
    public sealed class BattleBootstrap : MonoBehaviour
    {
        [Header("Runtime References")]
        [SerializeField] private LoadingClient loadingClient;
        [SerializeField] private ClientSession session;

        [Header("Scene References")]
        [SerializeField] private PlayerSpawnPoint[] spawnPoints;
        [SerializeField] private bool collectSpawnPointsOnStart = true;

        //编辑器单场景测试兜底，在没有走全流程加载任务的情况下，
        //允许使用调试信息创建BattleContext，而不影响后续的正式流程。
        [Header("Editor Fallback")]
        [SerializeField] private bool allowEditorFallbackContext = true;
        [SerializeField] private string debugBattleId = "debug_battle_001";
        [SerializeField] private string debugRoomId = "debug_room_001";
        [SerializeField] private string debugPlayerId = "debug_player_001";
        [SerializeField] private string debugNickname = "DebugPlayer";

        [Header("Local Player")]
        [SerializeField] private GameObject localPlayerPrefab;
        [SerializeField] private bool spawnLocalPlayerOnStart = true;
        [SerializeField] private string spawnedLocalPlayerName = "LocalPlayer";
        [SerializeField] private ThirdPersonCameraController cameraController;
        [SerializeField] private EmoteWheelView emoteWheelView;
        [SerializeField] private LockOnCamera lockOnCamera;

        // 远端玩家的容器。普通类，随 BattleBootstrap 一起创建，不需要在场景里挂载。
        private RemotePlayerRegistry _remotePlayers;

        public GameObject LocalPlayerInstance { get; private set; }

        public BattleContext CurrentContext { get; private set; }
        public bool IsInitialized { get; private set; }

        private void Start()
        {
            Initialize();
        }

        private void Initialize()
        {
            ResolveRuntimeReferences();

            // 如果spawnPoints未在Inspector中设置，
            // 或者collectSpawnPointsOnStart为true，则自动收集场景中的PlayerSpawnPoint。
            if (collectSpawnPointsOnStart || spawnPoints == null || spawnPoints.Length == 0)
            {
                spawnPoints = FindObjectsByType<PlayerSpawnPoint>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
            }

            CurrentContext = BuildBattleContext();
            IsInitialized = CurrentContext.IsValid;

            if (!CurrentContext.IsValid)
            {
                Debug.LogWarning("BattleBootstrap initialized with invalid context. " +
                                 "If you opened BattleArena_Training directly, enable editor fallback.");
            }

            Debug.Log($"BattleBootstrap initialized. {CurrentContext}");
            Debug.Log(BuildSpawnPointSummary());

            if (spawnLocalPlayerOnStart)
            {
                SpawnLocalPlayer();
            }
        }

        private void ResolveRuntimeReferences()
        {
            if (loadingClient == null)
            {
                loadingClient = FindFirstObjectByType<LoadingClient>();
            }

            if (session == null)
            {
                session = FindFirstObjectByType<ClientSession>();
            }
        }

        private BattleContext BuildBattleContext()
        {
            string sceneName = gameObject.scene.name;

            // 如果有有效的加载任务，则使用加载任务中的信息创建BattleContext。
            if (loadingClient != null && loadingClient.CurrentTask.IsValid)
            {
                LoadBattleSceneTaskInfo task = loadingClient.CurrentTask;

                string playerId = session != null ? session.PlayerId : string.Empty;
                string nickname = session != null ? session.Nickname : string.Empty;

                return new BattleContext(
                    task.BattleId,
                    task.RoomId,
                    playerId,
                    nickname,
                    sceneName,
                    false);
            }

            //如果没有有效的加载任务，但允许编辑器回退，则使用调试信息创建BattleContext。
            if (allowEditorFallbackContext)
            {
                return new BattleContext(
                    debugBattleId,
                    debugRoomId,
                    debugPlayerId,
                    debugNickname,
                    sceneName,
                    true);
            }

            // 如果没有有效的加载任务，也不允许编辑器回退，则创建一个无效的BattleContext。
            return new BattleContext(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                sceneName,
                false);
        }

        // 生成spawnPoints的摘要信息，用于调试和日志输出。
        private string BuildSpawnPointSummary()
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                return "BattleBootstrap found 0 PlayerSpawnPoint. Please add at least one spawn point.";
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"BattleBootstrap found {spawnPoints.Length} PlayerSpawnPoint(s).");

            for (int i = 0; i < spawnPoints.Length; i++)
            {
                PlayerSpawnPoint point = spawnPoints[i];

                if (point == null)
                {
                    builder.AppendLine($"  [{i}] null");
                    continue;
                }

                Vector3 pos = point.Position;
                builder.AppendLine(
                    $"  [{i}] {point.SpawnId}, Default={point.IsLocalPlayerDefault}, " +
                    $"Pos=({pos.x:F2}, {pos.y:F2}, {pos.z:F2})");
            }

            return builder.ToString();
        }

        // 在BattleBootstrap中生成本地玩家实例。
        private void SpawnLocalPlayer()
        {
            // 如果LocalPlayerInstance已经存在，则不再生成新的实例。
            if (LocalPlayerInstance != null)
            {
                return;
            }

            if (localPlayerPrefab == null)
            {
                Debug.LogWarning("BattleBootstrap cannot spawn local player: localPlayerPrefab is missing.");
                return;
            }

            // 优先用服务端下发的出生点；拿不到才回退到场景里的出生点标记。
            if (!TryResolveServerSpawn(out Vector3 spawnPosition, out Quaternion spawnRotation, out string spawnSource))
            {
                PlayerSpawnPoint spawnPoint = GetDefaultSpawnPoint();

                if (spawnPoint == null)
                {
                    Debug.LogWarning("BattleBootstrap cannot spawn local player: no PlayerSpawnPoint found.");
                    return;
                }

                spawnPosition = spawnPoint.Position;
                spawnRotation = spawnPoint.Rotation;
                spawnSource = $"SceneSpawnPoint({spawnPoint.SpawnId})";
            }

            LocalPlayerInstance = Instantiate(localPlayerPrefab, spawnPosition, spawnRotation);
            LocalPlayerInstance.name = spawnedLocalPlayerName;

            LocalPlayerController playerController = LocalPlayerInstance.GetComponent<LocalPlayerController>();
            PlayerInputReader inputReader = LocalPlayerInstance.GetComponent<PlayerInputReader>();
            LocalEmoteController emoteController = LocalPlayerInstance.GetComponent<LocalEmoteController>();
            PlayerTargetLock targetLock = LocalPlayerInstance.GetComponent<PlayerTargetLock>();

            if (cameraController == null)
            {
                cameraController = FindFirstObjectByType<ThirdPersonCameraController>();
            }

            if (emoteWheelView == null)
            {
                emoteWheelView = FindFirstObjectByType<EmoteWheelView>();
            }

            if (lockOnCamera == null)
            {
                lockOnCamera = FindFirstObjectByType<LockOnCamera>();
            }

            if (cameraController != null)
            {
                cameraController.SetTarget(LocalPlayerInstance.transform);

                if (playerController != null)
                {
                    playerController.SetCameraTransform(cameraController.transform);
                }
            }

            if (emoteWheelView != null)
            {
                emoteWheelView.SetTarget(LocalPlayerInstance.transform);
            }

            if (lockOnCamera != null && targetLock != null)
            {
                lockOnCamera.Bind(targetLock);
            }

            Debug.Log($"Local player spawned. PlayerId={CurrentContext.LocalPlayerId}, " +
                      $"Source={spawnSource}, Position={spawnPosition}");

            SpawnRemotePlayers();
        }

        private PlayerSpawnPoint GetDefaultSpawnPoint()
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                return null;
            }

            for (int i = 0; i < spawnPoints.Length; i++)
            {
                PlayerSpawnPoint point = spawnPoints[i];

                if (point != null && point.IsLocalPlayerDefault)
                {
                    return point;
                }
            }

            return spawnPoints[0];
        }

        // 服务端在 BattleStartNtf 里下发了每个人的出生点。
        // 拿到就用它；拿不到（编辑器兜底、或对端没带 players）回退到场景里的出生点标记。
        private bool TryResolveServerSpawn(
            out Vector3 position,
            out Quaternion rotation,
            out string source)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            source = string.Empty;

            if (loadingClient == null || CurrentContext.IsEditorFallback)
            {
                return false;
            }

            BattleStartInfo startInfo = loadingClient.LastBattleStart;

            if (!startInfo.TryFindPlayer(CurrentContext.LocalPlayerId, out BattlePlayerSpawnInfo spawn))
            {
                Debug.LogWarning(
                    $"BattleStartNtf has no spawn for local player {CurrentContext.LocalPlayerId}. " +
                    "Falling back to scene spawn point.");

                return false;
            }

            position = spawn.Position;
            rotation = spawn.Rotation;
            source = $"Server(spawnIndex={spawn.SpawnIndex})";

            return true;
        }

        // 按服务端下发的名单生成远端玩家。
        // 05-1 只摆位置，不接快照、不做插值 —— 那是 05-3 的事。
        private void SpawnRemotePlayers()
        {
            if (CurrentContext.IsEditorFallback || loadingClient == null)
            {
                return;
            }

            BattleStartInfo startInfo = loadingClient.LastBattleStart;

            if (startInfo.Players == null || startInfo.Players.Length == 0)
            {
                Debug.LogWarning("BattleStartNtf carries no players; remote players will not be spawned.");
                return;
            }

            if (_remotePlayers == null)
            {
                _remotePlayers = new RemotePlayerRegistry();
            }

            int spawned = _remotePlayers.SpawnMissing(
                startInfo.Players,
                CurrentContext.LocalPlayerId,
                localPlayerPrefab);

            Debug.Log($"Remote players spawned. new={spawned}, " +
                      $"total={_remotePlayers.Count}, expected={startInfo.Players.Length - 1}");
        }
    }
}