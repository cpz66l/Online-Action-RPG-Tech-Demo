using System;
using System.Threading;
using System.Threading.Tasks;
using OnlineActionRpg.Client.Account;
using OnlineActionRpg.Client.Network;
using UnityEngine;

namespace OnlineActionRpg.Client.Lobby
{
    // LobbyClient 是客户端大厅 / 房间业务入口。
    // 它只负责协议请求、响应解析和事件抛出，不直接操作 UI。
    public sealed class LobbyClient : MonoBehaviour
    {
        [SerializeField] private NetworkClient networkClient;
        [SerializeField] private ClientSession session;

        //记录发送的请求id,方便检查收到的响应是否是当前请求的响应
        //这里请求-响应模式,每次发送请求时生成一个新的requestId,收到响应时检查requestId是否匹配。
        //丢包会导致请求超时,但不会影响后续请求的发送和响应的处理。
        private string _pendingEnterLobbyRequestId = string.Empty;
        private string _pendingCreateRoomRequestId = string.Empty;
        private string _pendingJoinRoomRequestId = string.Empty;
        private string _pendingLeaveRoomRequestId = string.Empty;

        // SynchronizationContext 用于在 Unity 主线程上抛出事件，确保事件处理程序在主线程上执行。
        private SynchronizationContext _unityContext;

        public event Action<EnterLobbyResult> EnterLobbyCompleted;
        public event Action<RoomCommandResult> CreateRoomCompleted;
        public event Action<RoomCommandResult> JoinRoomCompleted;
        public event Action<RoomCommandResult> LeaveRoomCompleted;

        // RoomStateNtf 是服务端主动推送，不对应某一次按钮点击。
        public event Action<RoomDto> RoomStateChanged;

        private void Awake()
        {
            //unityContext 是 Unity 主线程的 SynchronizationContext, 用于在主线程上抛出事件，确保事件处理程序在主线程上执行。
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

        //发送进入大厅请求
        public async Task EnterLobbyAsync()
        {
            if (!EnsureReady(out int code, out string message))
            {
                RaiseEnterLobbyCompleted(EnterLobbyResult.Fail(code, message));
                return;
            }

            long now = GetUnixTimeMilliseconds();
            string requestId = Guid.NewGuid().ToString("N");

            EnterLobbyRequestEnvelope request = new EnterLobbyRequestEnvelope
            {
                msgId = LobbyMessageIds.EnterLobbyReq,
                type = "EnterLobbyReq",
                requestId = requestId,
                token = session.Token,
                clientTime = now,
                payload = new EmptyPayload()
            };

            _pendingEnterLobbyRequestId = requestId;

            string json = JsonUtility.ToJson(request);
            await networkClient.SendJsonAsync(json);
        }

        //发送创建房间请求
        public async Task CreateRoomAsync(string roomName, int maxPlayers)
        {
            if (!EnsureReady(out int code, out string message))
            {
                RaiseCreateRoomCompleted(RoomCommandResult.Fail(code, message));
                return;
            }

            if (string.IsNullOrWhiteSpace(roomName) || maxPlayers <= 0)
            {
                RaiseCreateRoomCompleted(RoomCommandResult.Fail(1001, "Room name and max players are required."));
                return;
            }

            long now = GetUnixTimeMilliseconds();
            string requestId = Guid.NewGuid().ToString("N");

            CreateRoomRequestEnvelope request = new CreateRoomRequestEnvelope
            {
                msgId = RoomMessageIds.CreateRoomReq,
                type = "CreateRoomReq",
                requestId = requestId,
                token = session.Token,
                clientTime = now,
                payload = new CreateRoomRequestPayload
                {
                    roomName = roomName.Trim(),
                    maxPlayers = maxPlayers
                }
            };

            _pendingCreateRoomRequestId = requestId;

            string json = JsonUtility.ToJson(request);
            await networkClient.SendJsonAsync(json);
        }

        //发送加入房间请求
        public async Task JoinRoomAsync(string roomId)
        {
            if (!EnsureReady(out int code, out string message))
            {
                RaiseJoinRoomCompleted(RoomCommandResult.Fail(code, message));
                return;
            }

            if (string.IsNullOrWhiteSpace(roomId))
            {
                RaiseJoinRoomCompleted(RoomCommandResult.Fail(1001, "Room id is required."));
                return;
            }

            long now = GetUnixTimeMilliseconds();
            string requestId = Guid.NewGuid().ToString("N");

            JoinRoomRequestEnvelope request = new JoinRoomRequestEnvelope
            {
                msgId = RoomMessageIds.JoinRoomReq,
                type = "JoinRoomReq",
                requestId = requestId,
                token = session.Token,
                clientTime = now,
                payload = new JoinRoomRequestPayload
                {
                    roomId = roomId.Trim()
                }
            };

            _pendingJoinRoomRequestId = requestId;

            string json = JsonUtility.ToJson(request);
            await networkClient.SendJsonAsync(json);
        }

        //发送离开房间请求
        public async Task LeaveRoomAsync(string roomId)
        {
            if (!EnsureReady(out int code, out string message))
            {
                RaiseLeaveRoomCompleted(RoomCommandResult.Fail(code, message));
                return;
            }

            if (string.IsNullOrWhiteSpace(roomId))
            {
                RaiseLeaveRoomCompleted(RoomCommandResult.Fail(1001, "Room id is required."));
                return;
            }

            long now = GetUnixTimeMilliseconds();
            string requestId = Guid.NewGuid().ToString("N");

            LeaveRoomRequestEnvelope request = new LeaveRoomRequestEnvelope
            {
                msgId = RoomMessageIds.LeaveRoomReq,
                type = "LeaveRoomReq",
                requestId = requestId,
                token = session.Token,
                clientTime = now,
                payload = new LeaveRoomRequestPayload
                {
                    roomId = roomId.Trim()
                }
            };

            _pendingLeaveRoomRequestId = requestId;

            string json = JsonUtility.ToJson(request);
            await networkClient.SendJsonAsync(json);
        }

        //处理服务器发来的文本消息，根据消息类型分发到不同的处理方法
        private void HandleTextMessageReceived(string json)
        {
            //将得到的json反序列化为ProtocolEnvelope对象，获取消息类型type和requestId)
            ProtocolEnvelope envelope = JsonUtility.FromJson<ProtocolEnvelope>(json);

            if (envelope == null || string.IsNullOrEmpty(envelope.type))
            {
                return;
            }
            //根据消息类型分发到不同的处理方法
            if (envelope.type == "EnterLobbyRes")
            {
                HandleEnterLobbyResponse(json);
                return;
            }

            if (envelope.type == "CreateRoomRes")
            {
                HandleCreateRoomResponse(json);
                return;
            }

            if (envelope.type == "JoinRoomRes")
            {
                HandleJoinRoomResponse(json);
                return;
            }

            if (envelope.type == "LeaveRoomRes")
            {
                HandleLeaveRoomResponse(json);
                return;
            }

            if (envelope.type == "RoomStateNtf")
            {
                HandleRoomStateNotification(json);
                return;
            }

            if (envelope.type == "ErrorRes")
            {
                HandleErrorResponse(envelope);
            }
        }

        //处理进入大厅响应
        private void HandleEnterLobbyResponse(string json)
        {
            EnterLobbyResponseEnvelope response = JsonUtility.FromJson<EnterLobbyResponseEnvelope>(json);

            if (response.requestId != _pendingEnterLobbyRequestId)
            {
                return;
            }

            _pendingEnterLobbyRequestId = string.Empty;

            RaiseEnterLobbyCompleted(EnterLobbyResult.Ok(
                response.message,
                response.payload.playerInfo,
                response.payload.rooms));
        }

        //处理创建房间响应
        private void HandleCreateRoomResponse(string json)
        {
            CreateRoomResponseEnvelope response = JsonUtility.FromJson<CreateRoomResponseEnvelope>(json);

            if (response.requestId != _pendingCreateRoomRequestId)
            {
                return;
            }

            _pendingCreateRoomRequestId = string.Empty;

            RaiseCreateRoomCompleted(RoomCommandResult.Ok(
                response.message,
                response.payload.room.roomId,
                response.payload.room));
        }

        //处理加入房间响应
        private void HandleJoinRoomResponse(string json)
        {
            JoinRoomResponseEnvelope response = JsonUtility.FromJson<JoinRoomResponseEnvelope>(json);

            if (response.requestId != _pendingJoinRoomRequestId)
            {
                return;
            }

            _pendingJoinRoomRequestId = string.Empty;

            RaiseJoinRoomCompleted(RoomCommandResult.Ok(
                response.message,
                response.payload.room.roomId,
                response.payload.room));
        }

        //处理离开房间响应
        private void HandleLeaveRoomResponse(string json)
        {
            LeaveRoomResponseEnvelope response = JsonUtility.FromJson<LeaveRoomResponseEnvelope>(json);

            if (response.requestId != _pendingLeaveRoomRequestId)
            {
                return;
            }

            _pendingLeaveRoomRequestId = string.Empty;

            RaiseLeaveRoomCompleted(RoomCommandResult.Ok(
                response.message,
                response.payload.roomId,
                response.payload.room));
        }

        //处理房间状态通知
        private void HandleRoomStateNotification(string json)
        {
            RoomStateNotificationEnvelope notification = JsonUtility.FromJson<RoomStateNotificationEnvelope>(json);

            if (notification == null || notification.payload == null || notification.payload.room == null)
            {
                return;
            }

            RaiseRoomStateChanged(notification.payload.room);
        }

        //处理错误响应
        private void HandleErrorResponse(ProtocolEnvelope response)
        {
            if (response.requestId == _pendingEnterLobbyRequestId)
            {
                _pendingEnterLobbyRequestId = string.Empty;
                RaiseEnterLobbyCompleted(EnterLobbyResult.Fail(response.code, response.message));
                return;
            }

            if (response.requestId == _pendingCreateRoomRequestId)
            {
                _pendingCreateRoomRequestId = string.Empty;
                RaiseCreateRoomCompleted(RoomCommandResult.Fail(response.code, response.message));
                return;
            }

            if (response.requestId == _pendingJoinRoomRequestId)
            {
                _pendingJoinRoomRequestId = string.Empty;
                RaiseJoinRoomCompleted(RoomCommandResult.Fail(response.code, response.message));
                return;
            }

            if (response.requestId == _pendingLeaveRoomRequestId)
            {
                _pendingLeaveRoomRequestId = string.Empty;
                RaiseLeaveRoomCompleted(RoomCommandResult.Fail(response.code, response.message));
            }
        }

        // 确保网络客户端和会话已准备好
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

        //在主线程上抛出事件，确保事件处理程序在主线程上执行
        private void RaiseEnterLobbyCompleted(EnterLobbyResult result)
        {
            RaiseOnMainThread(() => EnterLobbyCompleted?.Invoke(result));
        }

        private void RaiseCreateRoomCompleted(RoomCommandResult result)
        {
            RaiseOnMainThread(() => CreateRoomCompleted?.Invoke(result));
        }

        private void RaiseJoinRoomCompleted(RoomCommandResult result)
        {
            RaiseOnMainThread(() => JoinRoomCompleted?.Invoke(result));
        }

        private void RaiseLeaveRoomCompleted(RoomCommandResult result)
        {
            RaiseOnMainThread(() => LeaveRoomCompleted?.Invoke(result));
        }

        private void RaiseRoomStateChanged(RoomDto room)
        {
            RaiseOnMainThread(() => RoomStateChanged?.Invoke(room));
        }

        private void RaiseOnMainThread(Action action)
        {
            if (action == null)
            {
                return;
            }

            // 如果当前 SynchronizationContext 是 Unity 的主线程上下文，则直接调用 action，否则使用 Post 方法将 action 发布到主线程上下文中执行。
            if (_unityContext == null || SynchronizationContext.Current == _unityContext)
            {
                action.Invoke();
                return;
            }

            _unityContext.Post(_ => action.Invoke(), null);
        }

        //小helper
        private static long GetUnixTimeMilliseconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    // 进入大厅结果的数据结构
    public readonly struct EnterLobbyResult
    {
        public readonly bool Success;
        public readonly int Code;
        public readonly string Message;
        public readonly RoomPlayerDto PlayerInfo;
        public readonly RoomDto[] Rooms;

        private EnterLobbyResult(
            bool success,
            int code,
            string message,
            RoomPlayerDto playerInfo,
            RoomDto[] rooms)
        {
            Success = success;
            Code = code;
            Message = message ?? string.Empty;
            PlayerInfo = playerInfo;
            Rooms = rooms ?? Array.Empty<RoomDto>();
        }

        public static EnterLobbyResult Ok(string message, RoomPlayerDto playerInfo, RoomDto[] rooms)
        {
            return new EnterLobbyResult(true, 0, message, playerInfo, rooms);
        }

        public static EnterLobbyResult Fail(int code, string message)
        {
            return new EnterLobbyResult(false, code, message, null, Array.Empty<RoomDto>());
        }
    }

    // 房间命令结果的数据结构
    public readonly struct RoomCommandResult
    {
        public readonly bool Success;
        public readonly int Code;
        public readonly string Message;
        public readonly string RoomId;
        public readonly RoomDto Room;

        private RoomCommandResult(
            bool success,
            int code,
            string message,
            string roomId,
            RoomDto room)
        {
            Success = success;
            Code = code;
            Message = message ?? string.Empty;
            RoomId = roomId ?? string.Empty;
            Room = room;
        }

        public static RoomCommandResult Ok(string message, string roomId, RoomDto room)
        {
            return new RoomCommandResult(true, 0, message, roomId, room);
        }

        public static RoomCommandResult Fail(int code, string message)
        {
            return new RoomCommandResult(false, code, message, string.Empty, null);
        }
    }
}