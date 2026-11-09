using OnlineActionRpg.Client.Account;
using OnlineActionRpg.Client.Lobby;
using OnlineActionRpg.Client.Network;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace OnlineActionRpg.Client.Loading
{
    // LoadingClient 只负责 Loading 阶段协议。
    public sealed class LoadingClient : MonoBehaviour
    {
        [SerializeField] private NetworkClient networkClient;
        [SerializeField] private ClientSession session;

        private SynchronizationContext _unityContext;

        private string _pendingBattleReadyRequestId = string.Empty;

        public event Action<ClientBattleReadyResult> BattleReadyCompleted;
        public event Action<BattleStartInfo> BattleStartReceived;
        public event Action<LoadBattleSceneTaskInfo> LoadBattleSceneReceived;

        public LoadBattleSceneTaskInfo CurrentTask { get; private set; }

        // 最近一次收到的开战信息。
        // BattleBootstrap 在战斗场景里读它，取本玩家的出生点与同伴名单。
        public BattleStartInfo LastBattleStart { get; private set; }

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

        // 处理接收到的文本消息，根据消息类型分发到不同的处理方法。
        private void HandleTextMessageReceived(string json)
        {
            ProtocolEnvelope envelope = JsonUtility.FromJson<ProtocolEnvelope>(json);

            if (envelope == null || string.IsNullOrWhiteSpace(envelope.type))
            {
                return;
            }

            switch (envelope.type)
            {
                case "LoadBattleSceneNtf":
                    HandleLoadBattleSceneNotification(json);
                    break;

                case "ClientBattleReadyRes":
                    HandleClientBattleReadyResponse(json);
                    break;

                case "BattleStartNtf":
                    HandleBattleStartNotification(json);
                    break;

                case "ErrorRes":
                    HandleErrorResponse(envelope);
                    break;
            }
        }

        //处理 LoadBattleSceneNtf 消息，解析负载并触发 LoadBattleSceneReceived 事件。
        private void HandleLoadBattleSceneNotification(string json)
        {
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

        //处理 ClientBattleReadyRes 消息，解析负载并触发 BattleReadyCompleted 事件。
        private void HandleClientBattleReadyResponse(string json)
        {
            ClientBattleReadyResponseEnvelope response =
                JsonUtility.FromJson<ClientBattleReadyResponseEnvelope>(json);

            if (response == null || response.requestId != _pendingBattleReadyRequestId)
            {
                return;
            }

            _pendingBattleReadyRequestId = string.Empty;

            RaiseBattleReadyCompleted(ClientBattleReadyResult.Ok(
                response.message,
                response.payload.battleId,
                response.payload.room));
        }

        //处理 BattleStartNtf 消息，解析负载并触发 BattleStartReceived 事件。
        private void HandleBattleStartNotification(string json)
        {
            BattleStartNotificationEnvelope notification =
                JsonUtility.FromJson<BattleStartNotificationEnvelope>(json);

            if (notification == null || notification.payload == null)
            {
                return;
            }

            BattleStartInfo info = BattleStartInfo.FromPayload(notification.payload);

            if (!info.IsValid)
            {
                Debug.LogWarning($"Invalid BattleStartNtf payload: {json}");
                return;
            }

            RaiseOnMainThread(() =>
            {
                LastBattleStart = info;

                Debug.Log(
                    $"BattleStartNtf received. BattleId={info.BattleId}, " +
                    $"RoomId={info.RoomId}, ServerStartTime={info.ServerStartTime}, " +
                    $"Players={info.Players.Length}");

                BattleStartReceived?.Invoke(info);
            });
        }

        //处理 ErrorRes 消息，如果请求ID匹配，则触发 BattleReadyCompleted 事件，表示请求失败。
        private void HandleErrorResponse(ProtocolEnvelope response)
        {
            if (response.requestId == _pendingBattleReadyRequestId)
            {
                _pendingBattleReadyRequestId = string.Empty;
                RaiseBattleReadyCompleted(ClientBattleReadyResult.Fail(response.code, response.message));
            }
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

        //发送客户端准备就绪消息给服务器，包含战斗ID和房间ID，并处理响应。
        public async Task SendBattleReadyAsync(string battleId, string roomId)
        {
            if (!EnsureReady(out int code, out string message))
            {
                RaiseBattleReadyCompleted(ClientBattleReadyResult.Fail(code, message));
                return;
            }

            if (string.IsNullOrWhiteSpace(battleId) || string.IsNullOrWhiteSpace(roomId))
            {
                RaiseBattleReadyCompleted(ClientBattleReadyResult.Fail(1001, "Battle id and room id are required."));
                return;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string requestId = Guid.NewGuid().ToString("N");

            ClientBattleReadyRequestEnvelope request = new ClientBattleReadyRequestEnvelope
            {
                msgId = LoadingMessageIds.ClientBattleReadyReq,
                type = "ClientBattleReadyReq",
                requestId = requestId,
                token = session.Token,
                clientTime = now,
                payload = new ClientBattleReadyRequestPayload
                {
                    battleId = battleId,
                    roomId = roomId
                }
            };

            _pendingBattleReadyRequestId = requestId;

            string json = JsonUtility.ToJson(request);
            await networkClient.SendJsonAsync(json);
        }

        private bool EnsureReady(out int code, out string message)
        {
            if (networkClient == null)
            {
                code = 1001;
                message = "NetworkClient is missing.";
                return false;
            }

            if (!networkClient.IsConnected)
            {
                code = 1001;
                message = "Network is not connected.";
                return false;
            }

            if (session == null || !session.IsLoggedIn)
            {
                code = 1002;
                message = "Login session is required.";
                return false;
            }

            code = 0;
            message = "OK";
            return true;
        }

        //将ClientBattleReadyResult抛到主线程上。
        private void RaiseBattleReadyCompleted(ClientBattleReadyResult result)
        {
            RaiseOnMainThread(() => BattleReadyCompleted?.Invoke(result));
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

    //用于封装客户端准备就绪请求的结果信息。
    public readonly struct ClientBattleReadyResult
    {
        public readonly bool Success;
        public readonly int Code;
        public readonly string Message;
        public readonly string BattleId;
        public readonly RoomDto Room;

        private ClientBattleReadyResult(bool success, int code, string message, string battleId, RoomDto room)
        {
            Success = success;
            Code = code;
            Message = message ?? string.Empty;
            BattleId = battleId ?? string.Empty;
            Room = room;
        }

        public static ClientBattleReadyResult Ok(string message, string battleId, RoomDto room)
        {
            return new ClientBattleReadyResult(true, 0, message, battleId, room);
        }

        public static ClientBattleReadyResult Fail(int code, string message)
        {
            return new ClientBattleReadyResult(false, code, message, string.Empty, null);
        }
    }

    //用于收到服务器的战斗开始广播时，封装战斗开始信息的结构体。
    public readonly struct BattleStartInfo
    {
        public readonly string BattleId;
        public readonly string RoomId;
        public readonly long ServerStartTime;

        // 服务端分配的本场成员与出生点。为空时说明对端是旧版本或字段缺失。
        public readonly BattlePlayerSpawnInfo[] Players;

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(BattleId) &&
            !string.IsNullOrWhiteSpace(RoomId) &&
            ServerStartTime > 0;

        private BattleStartInfo(
            string battleId,
            string roomId,
            long serverStartTime,
            BattlePlayerSpawnInfo[] players)
        {
            BattleId = battleId ?? string.Empty;
            RoomId = roomId ?? string.Empty;
            ServerStartTime = serverStartTime;
            Players = players ?? Array.Empty<BattlePlayerSpawnInfo>();
        }

        // 从服务器传来的 BattleStartNotificationPayload 组装 BattleStartInfo 实例。
        public static BattleStartInfo FromPayload(BattleStartNotificationPayload payload)
        {
            if (payload == null)
            {
                return new BattleStartInfo(string.Empty, string.Empty, 0, null);
            }

            BattlePlayerSpawnPayload[] source = payload.players ?? Array.Empty<BattlePlayerSpawnPayload>();
            BattlePlayerSpawnInfo[] players = new BattlePlayerSpawnInfo[source.Length];

            for (int i = 0; i < source.Length; i++)
            {
                players[i] = BattlePlayerSpawnInfo.FromPayload(source[i]);
            }

            return new BattleStartInfo(payload.battleId, payload.roomId, payload.serverStartTime, players);
        }

        // 找出本玩家的出生信息。找不到返回 false，调用方应回退到场景里的出生点。
        public bool TryFindPlayer(string playerId, out BattlePlayerSpawnInfo spawn)
        {
            spawn = default;

            if (string.IsNullOrWhiteSpace(playerId) || Players == null)
            {
                return false;
            }

            for (int i = 0; i < Players.Length; i++)
            {
                if (string.Equals(Players[i].PlayerId, playerId, StringComparison.OrdinalIgnoreCase))
                {
                    spawn = Players[i];
                    return true;
                }
            }

            return false;
        }
    }

    // 单个玩家的出生信息，已转换成 Unity 类型，供 BattleBootstrap 直接使用。
    public readonly struct BattlePlayerSpawnInfo
    {
        public readonly string PlayerId;
        public readonly string Nickname;
        public readonly int SpawnIndex;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        private BattlePlayerSpawnInfo(
            string playerId,
            string nickname,
            int spawnIndex,
            Vector3 position,
            Quaternion rotation)
        {
            PlayerId = playerId ?? string.Empty;
            Nickname = nickname ?? string.Empty;
            SpawnIndex = spawnIndex;
            Position = position;
            Rotation = rotation;
        }

        public static BattlePlayerSpawnInfo FromPayload(BattlePlayerSpawnPayload payload)
        {
            if (payload == null)
            {
                return default;
            }

            Vector3 position = payload.position != null
                ? new Vector3(payload.position.x, payload.position.y, payload.position.z)
                : Vector3.zero;

            return new BattlePlayerSpawnInfo(
                payload.playerId,
                payload.nickname,
                payload.spawnIndex,
                position,
                Quaternion.Euler(0f, payload.yaw, 0f));
        }
    }

}