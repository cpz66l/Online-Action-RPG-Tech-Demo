using System.Text.Json;
using OnlineRpgServer.Account;
using OnlineRpgServer.Battle;
using OnlineRpgServer.Connection;
using OnlineRpgServer.Room;
using static OnlineRpgServer.Protocol.ProtocolErrors;
using static OnlineRpgServer.Protocol.ServerTime;

namespace OnlineRpgServer.Protocol;

// 消息分发器：把一条原始 JSON 变成"要回什么包 + 要顺带广播什么"。
//
// 它只做分发，不直接往 socket 上写任何东西 —— 广播由 ConnectionHandler 拿到失败
// MessageDispatchResult 之后交给 ServerBroadcaster 执行。
// 这条边界是刻意的：迭代 05 的 Battle Tick 广播没有"触发请求"，
// 如果分发器能自己广播，两条流就会混在一起。
public sealed class MessageRouter
{
    private readonly AccountService _accountService;
    private readonly RoomService _roomService;
    private readonly ConnectionRegistry _connectionRegistry;
    private readonly BattleService _battleService;
    private readonly ILogger<MessageRouter> _logger;

    public MessageRouter(
        AccountService accountService,
        RoomService roomService,
        ConnectionRegistry connectionRegistry,
        BattleService battleService,
        ILogger<MessageRouter> logger)
    {
        _accountService = accountService;
        _roomService = roomService;
        _connectionRegistry = connectionRegistry;
        _battleService = battleService;
        _logger = logger;
    }

    public MessageDispatchResult BuildResponse(string rawMessage, string connectionId)
    {
        try
        {
            // 先反序列化通用信封，再根据 type 做业务分发。
            var envelope = JsonSerializer.Deserialize<ProtocolEnvelope>(rawMessage);

            if (envelope is null || string.IsNullOrWhiteSpace(envelope.Type))
            {
                return ToDispatchResult(CreateErrorResponse(null, 1001, "Invalid protocol envelope."));
            }

            return envelope.Type switch
            {
                //心跳请求
                "PingReq" => ToDispatchResult(CreatePingResponse(envelope)),
                //注册请求
                "RegisterReq" => ToDispatchResult(CreateRegisterResponse(envelope, _accountService)),
                //登录请求
                "LoginReq" => ToDispatchResult(CreateLoginResponse(envelope, _accountService, connectionId, _connectionRegistry)),
                //进入大厅请求
                "EnterLobbyReq" => ToDispatchResult(CreateEnterLobbyResponse(envelope, _accountService, _roomService)),
                //创建房间请求：除了给请求者回包，还要把新的房间状态广播给房间成员
                "CreateRoomReq" => CreateCreateRoomDispatchResult(envelope, _accountService, _roomService),
                //加入房间请求：除了给请求者回包，还要通知房间内其他玩家成员列表变了
                "JoinRoomReq" => CreateJoinRoomDispatchResult(envelope, _accountService, _roomService),
                //离开房间请求：除了给请求者回包，还要通知留下来的玩家房主/成员列表变了
                "LeaveRoomReq" => CreateLeaveRoomDispatchResult(envelope, _accountService, _roomService),
                //准备状态请求：除了给请求者回包，还要通知房间内所有玩家准备状态变了
                "ReadyReq" => CreateReadyDispatchResult(envelope, _accountService, _roomService),
                //开始战斗请求：成功后把房间推进到 Loading，并广播权威房间状态
                "StartBattleReq" => CreateStartBattleDispatchResult(envelope, _accountService, _roomService),
                //客户端加载进度通知：成功时只记录日志，不回业务响应。
                "ClientLoadProgressNtf" => CreateClientLoadProgressDispatchResult(envelope, _logger, _accountService),
                //客户端加载完成请求
                "ClientBattleReadyReq" => CreateClientBattleReadyDispatchResult(envelope, _accountService, _roomService),
                //未定义的响应
                _ => ToDispatchResult(CreateErrorResponse(envelope.RequestId, 1001, $"Unsupported message type: {envelope.Type}"))
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON message.");
            return ToDispatchResult(CreateErrorResponse(null, 1001, "Invalid JSON message."));
        }
    }

    //有响应的分发结果，通常用于客户端请求时，需要回包
    private static MessageDispatchResult ToDispatchResult(
        object response,
        RoomSnapshot? roomStateToNotify = null,
        LoadBattleSceneDispatch? loadBattleSceneToNotify = null,
        BattleStartDispatch? battleStartToNotify = null)
    {
        return new MessageDispatchResult(
            JsonSerializer.Serialize(response),
            roomStateToNotify,
            loadBattleSceneToNotify,
            battleStartToNotify);
    }

    //无响应的分发结果，通常用于客户端广播通知时，不需要回包
    private static MessageDispatchResult ToNoResponseDispatchResult(
        RoomSnapshot? roomStateToNotify = null,
        LoadBattleSceneDispatch? loadBattleSceneToNotify = null,
        BattleStartDispatch? battleStartToNotify = null)
    {
        return new MessageDispatchResult(
            null,
            roomStateToNotify,
            loadBattleSceneToNotify,
            battleStartToNotify);
    }

    //业务分发

    //心跳响应
    private static object CreatePingResponse(ProtocolEnvelope request)
    {
        // clientTime 原样带回，客户端可以用“收到响应时间 - 发出请求时间”计算 RTT。
        var clientTime = request.ClientTime ?? TryGetPayloadClientTime(request.Payload) ?? 0;
        var serverTime = UnixTimeMilliseconds();

        return new
        {
            //信封的包装内容，每个信封必备
            msgId = DebugMessageIds.PingRes,
            type = "PingRes",
            requestId = request.RequestId,
            code = 0,
            message = "OK",
            serverTime,
            //具体的业务数据
            payload = new PingResponsePayload
            {
                ClientTime = clientTime,
                ServerTime = serverTime
            }
        };
    }

    //注册响应
    private static object CreateRegisterResponse(ProtocolEnvelope request, AccountService accountService)
    {
        //处理注册请求，转换Payload的数据内容
        RegisterRequestPayload? payload = request.Payload.Deserialize<RegisterRequestPayload>();

        if (payload is null)
        {
            return CreateErrorResponse(request.RequestId, 1001, "Invalid RegisterReq payload.");
        }

        var result = accountService.Register(payload.Username, payload.Password, payload.Nickname);

        if (!result.Success)
        {
            return CreateErrorResponse(request.RequestId, result.Code, result.Message);
        }

        return new
        {
            //信封的包装内容，每个信封必备
            msgId = AccountMessageIds.RegisterRes,
            type = "RegisterRes",
            requestId = request.RequestId,
            code = result.Code,
            message = result.Message,
            serverTime = UnixTimeMilliseconds(),
            //具体的业务数据
            payload = new RegisterResponsePayload
            {
                PlayerId = result.PlayerId,
                Nickname = result.Nickname
            }
        };
    }

    //登录响应
    private static object CreateLoginResponse(
        ProtocolEnvelope request,
        AccountService accountService,
        string connectionId,
        ConnectionRegistry connectionRegistry)
    {
        LoginRequestPayload? payload = request.Payload.Deserialize<LoginRequestPayload>();

        if (payload is null)
        {
            return CreateErrorResponse(request.RequestId, 1001, "Invalid LoginReq payload.");
        }

        var result = accountService.Login(payload.Username, payload.Password);

        if (!result.Success)
        {
            return CreateErrorResponse(request.RequestId, result.Code, result.Message);
        }

        var session = accountService.GetSession(result.Token);

        if (session is not null)
        {
            connectionRegistry.BindSession(connectionId, session);
        }

        return new
        {
            //信封的包装内容，每个信封必备
            msgId = AccountMessageIds.LoginRes,
            type = "LoginRes",
            requestId = request.RequestId,
            code = result.Code,
            message = result.Message,
            serverTime = UnixTimeMilliseconds(),
            //具体的业务数据
            payload = new LoginResponsePayload
            {
                Token = result.Token,
                PlayerId = result.PlayerId,
                Nickname = result.Nickname
            }
        };
    }

    //进入大厅响应
    private static object CreateEnterLobbyResponse(
        ProtocolEnvelope request,
        AccountService accountService,
        RoomService roomService)
    {
        //拿到请求携带的令牌，就得知请求者的身份了
        var session = accountService.GetSession(request.Token);

        if (session is null)
        {
            return CreateErrorResponse(request.RequestId, 1002, "Login token is required.");
        }

        //拉取房间列表
        var rooms = roomService.GetRoomList()
            .Select(RoomDto.FromSnapshot)
            .ToList();

        return new
        {
            msgId = LobbyMessageIds.EnterLobbyRes,
            type = "EnterLobbyRes",
            requestId = request.RequestId,
            code = 0,
            message = "OK",
            serverTime = UnixTimeMilliseconds(),
            payload = new EnterLobbyResponsePayload
            {
                PlayerInfo = new RoomPlayerDto
                {
                    PlayerId = session.PlayerId,
                    Nickname = session.Nickname,
                    IsReady = false,
                    IsBattleReady = false
                },
                Rooms = rooms
            }
        };
    }

    //创造房间分发结果
    private static MessageDispatchResult CreateCreateRoomDispatchResult(
        ProtocolEnvelope request,
        AccountService accountService,
        RoomService roomService)
    {
        var session = accountService.GetSession(request.Token);

        if (session is null)
        {
            return ToDispatchResult(CreateErrorResponse(request.RequestId, 1002, "Login token is required."));
        }

        //拿到请求信封中的具体数据payload
        CreateRoomRequestPayload? payload = request.Payload.Deserialize<CreateRoomRequestPayload>();

        if (payload is null)
        {
            return ToDispatchResult(CreateErrorResponse(request.RequestId, 1001, "Invalid CreateRoomReq payload."));
        }

        var result = roomService.CreateRoom(session, payload.RoomName, payload.MaxPlayers);

        if (!result.Success || result.Room is null)
        {
            return ToDispatchResult(CreateErrorResponse(request.RequestId, result.Code, result.Message));
        }

        var response = new
        {
            msgId = RoomMessageIds.CreateRoomRes,
            type = "CreateRoomRes",
            requestId = request.RequestId,
            code = result.Code,
            message = result.Message,
            serverTime = UnixTimeMilliseconds(),
            payload = new CreateRoomResponsePayload
            {
                //不直接拿取result.Room，而是读取result.Room快照，避免修改服务器房间权威
                Room = RoomDto.FromSnapshot(result.Room)
            }
        };

        // 状态变更成功后，把同一份权威快照交给外层 HandleConnectionAsync 做 RoomStateNtf 广播。
        return ToDispatchResult(response, result.Room);
    }

    //加入房间分发结果
    private static MessageDispatchResult CreateJoinRoomDispatchResult(
        ProtocolEnvelope request,
        AccountService accountService,
        RoomService roomService)
    {
        var session = accountService.GetSession(request.Token);

        if (session is null)
        {
            return ToDispatchResult(CreateErrorResponse(request.RequestId, 1002, "Login token is required."));
        }


        JoinRoomRequestPayload? payload = request.Payload.Deserialize<JoinRoomRequestPayload>();

        if (payload is null)
        {
            return ToDispatchResult(CreateErrorResponse(request.RequestId, 1001, "Invalid JoinRoomReq payload."));
        }

        //尝试加入房间，并获取行为结果
        var result = roomService.JoinRoom(session, payload.RoomId);

        if (!result.Success || result.Room is null)
        {
            return ToDispatchResult(CreateErrorResponse(request.RequestId, result.Code, result.Message));
        }

        var response = new
        {
            msgId = RoomMessageIds.JoinRoomRes,
            type = "JoinRoomRes",
            requestId = request.RequestId,
            code = result.Code,
            message = result.Message,
            serverTime = UnixTimeMilliseconds(),
            payload = new JoinRoomResponsePayload
            {
                Room = RoomDto.FromSnapshot(result.Room)
            }
        };

        return ToDispatchResult(response, result.Room);
    }

    //离开房间分发结果
    private static MessageDispatchResult CreateLeaveRoomDispatchResult(
        ProtocolEnvelope request,
        AccountService accountService,
        RoomService roomService)
    {
        var session = accountService.GetSession(request.Token);

        if (session is null)
        {
            return ToDispatchResult(CreateErrorResponse(request.RequestId, 1002, "Login token is required."));
        }

        LeaveRoomRequestPayload? payload = request.Payload.Deserialize<LeaveRoomRequestPayload>();

        if (payload is null || string.IsNullOrWhiteSpace(payload.RoomId))
        {
            return ToDispatchResult(CreateErrorResponse(request.RequestId, 1001, "Invalid LeaveRoomReq payload."));
        }

        var result = roomService.LeaveRoom(session, payload.RoomId);

        if (!result.Success)
        {
            return ToDispatchResult(CreateErrorResponse(request.RequestId, result.Code, result.Message));
        }

        var response = new
        {
            msgId = RoomMessageIds.LeaveRoomRes,
            type = "LeaveRoomRes",
            requestId = request.RequestId,
            code = result.Code,
            message = result.Message,
            serverTime = UnixTimeMilliseconds(),
            payload = new LeaveRoomResponsePayload
            {
                RoomId = payload.RoomId,
                Room = result.Room is null ? null : RoomDto.FromSnapshot(result.Room)
            }
        };

        // result.Room 为 null 代表最后一名玩家离开、房间销毁；这时已经没有房间成员需要广播。
        return ToDispatchResult(response, result.Room);
    }

    //准备状态分发结果
    private static MessageDispatchResult CreateReadyDispatchResult(
        ProtocolEnvelope request,
        AccountService accountService,
        RoomService roomService)
    {
        var session = accountService.GetSession(request.Token);

        if (session is null)
        {
            return ToDispatchResult(CreateErrorResponse(request.RequestId, 1002, "Login token is required."));
        }

        ReadyRequestPayload? payload = request.Payload.Deserialize<ReadyRequestPayload>();

        if (payload is null)
        {
            return ToDispatchResult(CreateErrorResponse(request.RequestId, 1001, "Invalid ReadyReq payload."));
        }

        var result = roomService.SetReady(session, payload.RoomId, payload.IsReady);

        if (!result.Success || result.Room is null)
        {
            return ToDispatchResult(CreateErrorResponse(request.RequestId, result.Code, result.Message));
        }

        var response = new
        {
            msgId = RoomMessageIds.ReadyRes,
            type = "ReadyRes",
            requestId = request.RequestId,
            code = result.Code,
            message = result.Message,
            serverTime = UnixTimeMilliseconds(),
            payload = new ReadyResponsePayload
            {
                Room = RoomDto.FromSnapshot(result.Room)
            }
        };

        return ToDispatchResult(response, result.Room);
    }

    //组装开始战斗的派发结果
    private static MessageDispatchResult CreateStartBattleDispatchResult(
        ProtocolEnvelope request,
        AccountService accountService,
        RoomService roomService)
    {
        var session = accountService.GetSession(request.Token);

        if (session is null)
        {
            return ToDispatchResult(CreateErrorResponse(request.RequestId, 1002, "Login token is required."));
        }

        StartBattleRequestPayload? payload = request.Payload.Deserialize<StartBattleRequestPayload>();

        if (payload is null)
        {
            return ToDispatchResult(CreateErrorResponse(request.RequestId, 1001, "Invalid StartBattleReq payload."));
        }

        var result = roomService.StartBattle(session, payload.RoomId);

        if (!result.Success || result.Room is null)
        {
            return ToDispatchResult(CreateErrorResponse(request.RequestId, result.Code, result.Message));
        }

        var response = new
        {
            msgId = RoomMessageIds.StartBattleRes,
            type = "StartBattleRes",
            requestId = request.RequestId,
            code = result.Code,
            message = result.Message,
            serverTime = UnixTimeMilliseconds(),
            payload = new StartBattleResponsePayload
            {
                //调用RoomDto.FromSnapshot(result.Room)而不是直接使用result.Room，避免修改服务器房间权威状态
                // RoomDto类的静态方法，用于实例化出一个新的RoomDto对象，并将房间快照转换为客户端可用的格式
                Room = RoomDto.FromSnapshot(result.Room)
            }
        };

        var loadBattleSceneNotification = new LoadBattleSceneDispatch(
        result.Room,
        CreateLoadBattleScenePayload(result.Room));

        return ToDispatchResult(
            response,
            roomStateToNotify: result.Room,
            loadBattleSceneToNotify: loadBattleSceneNotification);
    }

    //客户端加载进度通知的派发结果，成功时只记录日志，不回业务响应。
    private static MessageDispatchResult CreateClientLoadProgressDispatchResult(
        ProtocolEnvelope request,
        ILogger logger,
        AccountService accountService)
    {
        var session = accountService.GetSession(request.Token);

        if (session is null)
        {
            return ToDispatchResult(CreateErrorResponse(request.RequestId, 1002, "Login token is required."));
        }

        ClientLoadProgressNotificationPayload? payload =
            request.Payload.Deserialize<ClientLoadProgressNotificationPayload>();

        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.BattleId) ||
            string.IsNullOrWhiteSpace(payload.RoomId))
        {
            return ToDispatchResult(CreateErrorResponse(request.RequestId, 1001, "Invalid ClientLoadProgressNtf payload."));
        }

        float progress = Math.Clamp(payload.Progress, 0f, 1f);

        logger.LogInformation(
            "Client load progress. PlayerId={PlayerId}, RoomId={RoomId}, BattleId={BattleId}, Stage={Stage}, Progress={Progress:P0}, Message={Message}",
            session.PlayerId,
            payload.RoomId,
            payload.BattleId,
            payload.Stage,
            progress,
            payload.Message);

        return ToNoResponseDispatchResult();
    }

    //客户端加载完成请求的派发结果，成功时会广播房间状态和战斗开始通知
    private MessageDispatchResult CreateClientBattleReadyDispatchResult(
        ProtocolEnvelope request,
        AccountService accountService,
        RoomService roomService)
    {
        var session = accountService.GetSession(request.Token);

        if (session is null)
        {
            return ToDispatchResult(CreateErrorResponse(request.RequestId, 1002, "Login token is required."));
        }

        ClientBattleReadyRequestPayload? payload =
            request.Payload.Deserialize<ClientBattleReadyRequestPayload>();

        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.BattleId) ||
            string.IsNullOrWhiteSpace(payload.RoomId))
        {
            return ToDispatchResult(CreateErrorResponse(request.RequestId, 1001, "Invalid ClientBattleReadyReq payload."));
        }

        //标记玩家已准备好，尝试推进房间状态
        var result = roomService.MarkBattleReady(session, payload.RoomId);

        if (!result.Success || result.Room is null)
        {
            return ToDispatchResult(CreateErrorResponse(request.RequestId, result.Code, result.Message));
        }

        var response = new
        {
            msgId = LoadingMessageIds.ClientBattleReadyRes,
            type = "ClientBattleReadyRes",
            requestId = request.RequestId,
            code = result.Code,
            message = result.Message,
            serverTime = UnixTimeMilliseconds(),
            payload = new ClientBattleReadyResponsePayload
            {
                BattleId = payload.BattleId,
                Room = RoomDto.FromSnapshot(result.Room)
            }
        };

        BattleStartDispatch? battleStartDispatch = null;

        if (result.BattleStarted)
        {
            // 全员就绪，真正的战斗从这里开始：创建实例、分配出生点、把名单一起下发。
            // 出生点是服务端分配的，客户端只负责把人摆到指定坐标。
            List<(string PlayerId, string Nickname)> members = result.Room.Players
                .Select(player => (player.PlayerId, player.Nickname))
                .ToList();

            BattleInstance battle = _battleService.CreateBattle(
                payload.BattleId,
                payload.RoomId,
                members);

            battleStartDispatch = new BattleStartDispatch(
                result.Room,
                new BattleStartNotificationPayload
                {
                    BattleId = payload.BattleId,
                    RoomId = payload.RoomId,
                    ServerStartTime = UnixTimeMilliseconds(),
                    Players = battle.GetPlayers()
                        .Select(state => new BattlePlayerSpawnDto
                        {
                            PlayerId = state.PlayerId,
                            Nickname = state.Nickname,
                            SpawnIndex = state.SpawnIndex,
                            Position = new BattleVector3Dto
                            {
                                X = state.Position.X,
                                Y = state.Position.Y,
                                Z = state.Position.Z
                            },
                            Yaw = state.Yaw
                        })
                        .ToList()
                });
        }

        return ToDispatchResult(
            response,
            roomStateToNotify: result.Room,
            battleStartToNotify: battleStartDispatch);
    }

    // 加载战斗场景通知的负载。
    // 原本与广播逻辑放在一起，但它其实是"分发阶段就要确定的业务参数"，
    // 所以跟着 StartBattle 的分发一起留在这里。
    private static LoadBattleSceneNotificationPayload CreateLoadBattleScenePayload(RoomSnapshot room)
    {
        return new LoadBattleSceneNotificationPayload
        {
            BattleId = $"b_{Guid.NewGuid():N}",
            RoomId = room.RoomId,
            SceneKey = "BattleArena_Training",
            RequiredAssets =
            [
                "Character_Knight",
                "Skill_Slash",
                "Vfx_Hit"
            ]
            //先硬编码战斗场景Key与需要加载的资源，后续可以根据房间配置或玩家选择来动态指定
        };
    }

    private static long? TryGetPayloadClientTime(JsonElement payload)
    {
        // 兼容两种写法：clientTime 可以放在信封顶层，也可以放在 payload 内。
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return payload.TryGetProperty("clientTime", out var clientTimeElement) && clientTimeElement.TryGetInt64(out var clientTime)
            ? clientTime
            : null;
    }
}
