using OnlineRpgServer.Account;

namespace OnlineRpgServer.Room;

// 服务端房间模块的核心业务类。
// 不关心 WebSocket 和 JSON，只维护服务端内存里的房间权威状态。
public sealed class RoomService
{
    private const int Ok = 0;
    private const int InvalidArgument = 1001;   //无效参数
    private const int RoomNotFound = 3001;      //未找到房间
    private const int RoomFull = 3002;          //房间已满
    private const int InvalidRoomState = 3003;  //无效房间状态

    private readonly object _gate = new();

    //将房间状态与id对应
    private readonly Dictionary<string, RoomRecord> _roomsById = new(StringComparer.OrdinalIgnoreCase);
    //将玩家id与房间捆绑，一个玩家只能对应一个房间，玩家创建新房间或加入新房间时，要先把他从旧房间移走
    private readonly Dictionary<string, string> _roomIdByPlayerId = new(StringComparer.OrdinalIgnoreCase);

    private int _nextRoomNumber = 10001;

    //用于进入玩家大厅后拉取房间列表
    public IReadOnlyList<RoomSnapshot> GetRoomList()
    {
        lock (_gate)
        {
            return _roomsById.Values
                .Select(CreateSnapshot)
                .ToList();
        }
    }

    //创建房间
    public RoomOperationResult CreateRoom(PlayerSession session, string? roomName, int maxPlayers)
    {
        roomName = Normalize(roomName);

        if (IsMissing(roomName) || maxPlayers <= 0)
        {
            return RoomOperationResult.Fail(InvalidArgument, "Room name and max players are required.");
        }

        lock (_gate)
        {
            //先确保玩家不处于任何房间
            LeaveCurrentRoomIfNeeded(session.PlayerId);

            long now = UnixTimeMilliseconds();
            string roomId = CreateRoomId();

            //实例化一个房间
            RoomRecord room = new()
            {
                RoomId = roomId,
                RoomName = roomName,
                MaxPlayers = maxPlayers,
                CreatedAt = now,
                OwnerPlayerId = session.PlayerId
            };

            //将玩家写入房间的内存字典
            room.PlayersById.Add(session.PlayerId, new RoomPlayerRecord
            {
                PlayerId = session.PlayerId,
                Nickname = session.Nickname,
                JoinedAt = now
            });

            //将该房间加入到内存字典存储
            _roomsById.Add(roomId, room);
            //记录该玩家所在房间id
            _roomIdByPlayerId[session.PlayerId] = roomId;

            return RoomOperationResult.Ok(CreateSnapshot(room));
        }
    }

    //加入房间
    public RoomOperationResult JoinRoom(PlayerSession session, string? roomId)
    {
        roomId = Normalize(roomId);

        if (IsMissing(roomId))
        {
            return RoomOperationResult.Fail(InvalidArgument, "Room id is required.");
        }

        lock (_gate)
        {
            //查询房间
            if (!_roomsById.TryGetValue(roomId, out RoomRecord? room))
            {
                return RoomOperationResult.Fail(RoomNotFound, "Room not found.");
            }
            //玩家去重，避免同一个玩家加入两次
            if (room.PlayersById.ContainsKey(session.PlayerId))
            {
                return RoomOperationResult.Ok(CreateSnapshot(room));
            }
            //判断房间是否已满
            if (room.PlayersById.Count >= room.MaxPlayers)
            {
                return RoomOperationResult.Fail(RoomFull, "Room is full.");
            }

            //房间条件满足，确保玩家不存在其他房间
            LeaveCurrentRoomIfNeeded(session.PlayerId);

            room.PlayersById.Add(session.PlayerId, new RoomPlayerRecord
            {
                PlayerId = session.PlayerId,
                Nickname = session.Nickname,
                JoinedAt = UnixTimeMilliseconds()
            });

            _roomIdByPlayerId[session.PlayerId] = room.RoomId;

            return RoomOperationResult.Ok(CreateSnapshot(room));
        }
    }

    //离开房间
    public RoomOperationResult LeaveRoom(PlayerSession session, string? roomId)
    {
        roomId = Normalize(roomId);

        if (IsMissing(roomId))
        {
            return RoomOperationResult.Fail(InvalidArgument, "Room id is required.");
        }

        lock (_gate)
        {
            if (!_roomsById.TryGetValue(roomId, out RoomRecord? room))
            {
                return RoomOperationResult.Fail(RoomNotFound, "Room not found.");
            }

            if (!room.PlayersById.Remove(session.PlayerId))
            {
                return RoomOperationResult.Fail(InvalidRoomState, "Player is not in this room.");
            }

            _roomIdByPlayerId.Remove(session.PlayerId);

            if (room.PlayersById.Count == 0)
            {
                _roomsById.Remove(room.RoomId);
                return RoomOperationResult.Ok(null);
            }

            if (room.OwnerPlayerId == session.PlayerId)
            {
                room.OwnerPlayerId = room.PlayersById.Values.First().PlayerId;
            }

            return RoomOperationResult.Ok(CreateSnapshot(room));
        }
    }

    //确保玩家不处于任何房间
    private void LeaveCurrentRoomIfNeeded(string playerId)
    {
        //查询当前玩家所在房间的id
        if (!_roomIdByPlayerId.TryGetValue(playerId, out string? currentRoomId))
        {
            //当前玩家不存在任何房间，则继续创建或加入房间
            return;
        }
        //根据房间id查询房间
        if (!_roomsById.TryGetValue(currentRoomId, out RoomRecord? currentRoom))
        {
            //房间id存在但房间已经不存在
            _roomIdByPlayerId.Remove(playerId);
            return;
        }
        //查到玩家当前所在房间，并退出该房间
        currentRoom.PlayersById.Remove(playerId);
        _roomIdByPlayerId.Remove(playerId);

        //若玩家退出房间后，房间玩家数为0，则销毁房间
        if (currentRoom.PlayersById.Count == 0)
        {
            _roomsById.Remove(currentRoom.RoomId);
            return;
        }

        //若玩家退出房间后，房间内还存在其他玩家，且玩家为房主，则将房主移交给剩余第一位玩家
        if (currentRoom.OwnerPlayerId == playerId)
        {
            currentRoom.OwnerPlayerId = currentRoom.PlayersById.Values.First().PlayerId;
        }
    }

    //小helper
    private string CreateRoomId()
    {
        string roomId = $"r_{_nextRoomNumber}";
        _nextRoomNumber++;
        return roomId;
    }

    private static RoomSnapshot CreateSnapshot(RoomRecord room)
    {
        return new RoomSnapshot
        {
            RoomId = room.RoomId,
            RoomName = room.RoomName,
            OwnerPlayerId = room.OwnerPlayerId,
            MaxPlayers = room.MaxPlayers,
            State = room.State.ToString(),
            Players = room.PlayersById.Values
                .Select(player => new RoomPlayerSnapshot
                {
                    PlayerId = player.PlayerId,
                    Nickname = player.Nickname
                })
                .ToList()
        };
    }

    //处理string的空格等问题
    private static string Normalize(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static bool IsMissing(string value)
    {
        return string.IsNullOrWhiteSpace(value);
    }

    private static long UnixTimeMilliseconds()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}

//房间重载结果的定义
public sealed class RoomOperationResult
{
    public bool Success { get; private init; }
    public int Code { get; private init; }
    public string Message { get; private init; } = string.Empty;
    public RoomSnapshot? Room { get; private init; }

    public static RoomOperationResult Ok(RoomSnapshot? room)
    {
        return new RoomOperationResult
        {
            Success = true,
            Code = 0,
            Message = "OK",
            Room = room
        };
    }

    public static RoomOperationResult Fail(int code, string message)
    {
        return new RoomOperationResult
        {
            Success = false,
            Code = code,
            Message = message
        };
    }
}