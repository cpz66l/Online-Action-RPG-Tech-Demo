using System;
using System.Threading;
using System.Threading.Tasks;
using OnlineActionRpg.Client.Network;
using OnlineActionRpg.Client.Account;
using UnityEngine;

namespace OnlineActionRpg.Client.Loading
{
    // LoadingClient 只负责 Loading 阶段协议。
    public sealed class LoadingClient : MonoBehaviour
    {
        [SerializeField] private NetworkClient networkClient;
        [SerializeField] private ClientSession session;

        private SynchronizationContext _unityContext;

        public event Action<LoadBattleSceneTaskInfo> LoadBattleSceneReceived;

        public LoadBattleSceneTaskInfo CurrentTask { get; private set; }

        private void Awake()
        {
            _unityContext = SynchronizationContext.Current;

            if (networkClient == null)
            {
                networkClient = FindFirstObjectByType<NetworkClient>();
            }

            if (session == null)
            {
                session = FindFirstObjectByType<ClientSession>();
            }

            if (networkClient != null)
            {
                networkClient.TextMessageReceived += HandleTextMessageReceived;
            }
        }

        private void OnDestroy()
        {
            if (networkClient != null)
            {
                networkClient.TextMessageReceived -= HandleTextMessageReceived;
            }
        }

        // 处理接收到的文本消息，解析为 LoadBattleSceneNotificationEnvelope ，再将信封的payload具体数据封装成TaskInfo，
        // 再抛到unity主线程,触发事件,将加载战斗场景任务的信息传给客户端。
        private void HandleTextMessageReceived(string json)
        {
            ProtocolEnvelope envelope = JsonUtility.FromJson<ProtocolEnvelope>(json);

            if (envelope == null || envelope.type != "LoadBattleSceneNtf")
            {
                return;
            }

            LoadBattleSceneNotificationEnvelope notification =
                JsonUtility.FromJson<LoadBattleSceneNotificationEnvelope>(json);

            if (notification == null || notification.payload == null)
            {
                return;
            }

            LoadBattleSceneTaskInfo task = LoadBattleSceneTaskInfo.FromPayload(notification.payload);

            if (!task.IsValid)
            {
                Debug.LogWarning($"Invalid LoadBattleSceneNtf payload: {json}");
                return;
            }

            RaiseOnMainThread(() =>
            {
                CurrentTask = task;
                Debug.Log(
                    $"LoadBattleSceneNtf received. BattleId={task.BattleId}, " +
                    $"RoomId={task.RoomId}, SceneKey={task.SceneKey}, AssetCount={task.RequiredAssetCount}");

                LoadBattleSceneReceived?.Invoke(task);
            });
        }

        //将客户端加载进度发送给服务器，包含战斗ID、房间ID、加载进度、阶段和消息。
        public async Task SendLoadProgressAsync(string battleId,
            string roomId,
            float progress,
            string stage,
            string message)
        {
            if (networkClient == null || !networkClient.IsConnected)
            {
                Debug.LogWarning("Cannot send load progress: network is not connected.");
                return;
            }

            if (session == null || !session.IsLoggedIn)
            {
                Debug.LogWarning("Cannot send load progress: session is not logged in.");
                return;
            }

            if (string.IsNullOrWhiteSpace(battleId) || string.IsNullOrWhiteSpace(roomId))
            {
                Debug.LogWarning("Cannot send load progress: battleId or roomId is empty.");
                return;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            ClientLoadProgressNotificationEnvelope notification = new ClientLoadProgressNotificationEnvelope
            {
                msgId = LoadingMessageIds.ClientLoadProgressNtf,
                type = "ClientLoadProgressNtf",
                token = session.Token,
                clientTime = now,
                payload = new ClientLoadProgressNotificationPayload
                {
                    battleId = battleId,
                    roomId = roomId,
                    progress = Mathf.Clamp01(progress),
                    stage = stage ?? string.Empty,
                    message = message ?? string.Empty
                }
            };

            string json = JsonUtility.ToJson(notification);
            await networkClient.SendJsonAsync(json);
        }

        private void RaiseOnMainThread(Action action)
        {
            if (action == null)
            {
                return;
            }

            if (_unityContext == null || SynchronizationContext.Current == _unityContext)
            {
                action.Invoke();
                return;
            }

            _unityContext.Post(_ => action.Invoke(), null);
        }
    }

    // LoadBattleSceneTaskInfo 结构体用于封装加载战斗场景任务的信息。
    public readonly struct LoadBattleSceneTaskInfo
    {
        public readonly string BattleId;
        public readonly string RoomId;
        public readonly string SceneKey;
        public readonly string[] RequiredAssets;

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(BattleId) &&
            !string.IsNullOrWhiteSpace(RoomId) &&
            !string.IsNullOrWhiteSpace(SceneKey);

        public int RequiredAssetCount => RequiredAssets != null ? RequiredAssets.Length : 0;

        //构造函数私有化，确保只能通过 FromPayload 方法创建实例。
        private LoadBattleSceneTaskInfo(
            string battleId,
            string roomId,
            string sceneKey,
            string[] requiredAssets)
        {
            BattleId = battleId ?? string.Empty;
            RoomId = roomId ?? string.Empty;
            SceneKey = sceneKey ?? string.Empty;
            RequiredAssets = requiredAssets ?? Array.Empty<string>();
        }

        // 从 LoadBattleSceneNotificationPayload 创建 LoadBattleSceneTaskInfo 实例。
        public static LoadBattleSceneTaskInfo FromPayload(LoadBattleSceneNotificationPayload payload)
        {
            if (payload == null)
            {
                return new LoadBattleSceneTaskInfo(string.Empty, string.Empty, string.Empty, Array.Empty<string>());
            }

            return new LoadBattleSceneTaskInfo(
                payload.battleId,
                payload.roomId,
                payload.sceneKey,
                payload.requiredAssets);
        }
    }
}