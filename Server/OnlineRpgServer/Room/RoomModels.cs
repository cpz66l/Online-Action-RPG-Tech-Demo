using System;
using System.Collections.Generic;

namespace OnlineRpgServer.Room;

// 房间状态：等待房间，后续加入Ready/Loading/Battle
public enum RoomState
{
    Waiting = 0
}

// 服务端内存中的房间记录。
// 这是服务端权威状态，客户端不能自己决定这里面的成员和房主。
public sealed class RoomRecord
{
    public required string RoomId { get; init; }
    public required string RoomName { get; init; }
    public required int MaxPlayers { get; init; }
    public required long CreatedAt { get; init; }

    public required string OwnerPlayerId { get; set; }
    public RoomState State { get; set; } = RoomState.Waiting;

    //一个房间有一个字典，通过玩家Id记录查询房间内的玩家
    public Dictionary<string, RoomPlayerRecord> PlayersById { get; } =
        new(StringComparer.OrdinalIgnoreCase);//忽略大小写差异
}

// 服务端内存中的房间玩家记录。
// 它来自登录后的 PlayerSession，不由客户端随便上传决定。
public sealed class RoomPlayerRecord
{
    public required string PlayerId { get; init; }
    public required string Nickname { get; init; }
    public required long JoinedAt { get; init; }
}

// 给外部读取用的房间快照。
// 不把 RoomRecord 直接暴露出去，避免外部误改服务端权威状态。
public sealed class RoomSnapshot
{
    public required string RoomId { get; init; }
    public required string RoomName { get; init; }
    public required string OwnerPlayerId { get; init; }
    public required int MaxPlayers { get; init; }
    public required string State { get; init; }
    public required IReadOnlyList<RoomPlayerSnapshot> Players { get; init; }
}

// 给外部读取用的房间玩家快照。
public sealed class RoomPlayerSnapshot
{
    public required string PlayerId { get; init; }
    public required string Nickname { get; init; }
}