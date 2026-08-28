using System.Text.Json.Serialization;
using OnlineRpgServer.Room;

namespace OnlineRpgServer.Protocol;

//登录成功后会自动请求加入大厅
public static class LobbyMessageIds
{
    public const int EnterLobbyReq = 2001;
    public const int EnterLobbyRes = 2002;
}

public static class RoomMessageIds
{
    public const int CreateRoomReq = 3101;
    public const int CreateRoomRes = 3102;
    public const int JoinRoomReq = 3103;
    public const int JoinRoomRes = 3104;
    public const int LeaveRoomReq = 3105;
    public const int LeaveRoomRes = 3106;
    public const int RoomStateNtf = 3199;
}

public sealed class EnterLobbyResponsePayload
{
    [JsonPropertyName("playerInfo")]
    public required RoomPlayerDto PlayerInfo { get; init; }

    [JsonPropertyName("rooms")]
    public required IReadOnlyList<RoomDto> Rooms { get; init; }
}

public sealed class CreateRoomRequestPayload
{
    [JsonPropertyName("roomName")]
    public string? RoomName { get; init; }

    [JsonPropertyName("maxPlayers")]
    public int MaxPlayers { get; init; }
}

public sealed class CreateRoomResponsePayload
{
    [JsonPropertyName("room")]
    public required RoomDto Room { get; init; }
}

public sealed class JoinRoomRequestPayload
{
    [JsonPropertyName("roomId")]
    public string? RoomId { get; init; }
}

public sealed class JoinRoomResponsePayload
{
    [JsonPropertyName("room")]
    public required RoomDto Room { get; init; }
}

public sealed class LeaveRoomRequestPayload
{
    [JsonPropertyName("roomId")]
    public string? RoomId { get; init; }
}

public sealed class LeaveRoomResponsePayload
{
    [JsonPropertyName("roomId")]
    public required string RoomId { get; init; }

    [JsonPropertyName("room")]
    public RoomDto? Room { get; init; }
}

public sealed class RoomStateNotificationPayload
{
    [JsonPropertyName("room")]
    public required RoomDto Room { get; init; }
}

public sealed class RoomDto
{
    [JsonPropertyName("roomId")]
    public required string RoomId { get; init; }

    [JsonPropertyName("roomName")]
    public required string RoomName { get; init; }

    [JsonPropertyName("ownerPlayerId")]
    public required string OwnerPlayerId { get; init; }

    [JsonPropertyName("maxPlayers")]
    public required int MaxPlayers { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("players")]
    public required IReadOnlyList<RoomPlayerDto> Players { get; init; }

    public static RoomDto FromSnapshot(RoomSnapshot snapshot)
    {
        return new RoomDto
        {
            RoomId = snapshot.RoomId,
            RoomName = snapshot.RoomName,
            OwnerPlayerId = snapshot.OwnerPlayerId,
            MaxPlayers = snapshot.MaxPlayers,
            State = snapshot.State,
            Players = snapshot.Players
                .Select(player => new RoomPlayerDto
                {
                    PlayerId = player.PlayerId,
                    Nickname = player.Nickname
                })
                .ToList()
        };
    }
}

public sealed class RoomPlayerDto
{
    [JsonPropertyName("playerId")]
    public required string PlayerId { get; init; }

    [JsonPropertyName("nickname")]
    public required string Nickname { get; init; }
}
