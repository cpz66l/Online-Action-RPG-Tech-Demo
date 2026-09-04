using System;
using OnlineActionRpg.Client.Network;

namespace OnlineActionRpg.Client.Lobby
{
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

    [Serializable]
    public sealed class EnterLobbyRequestEnvelope : ProtocolEnvelope
    {
        public EmptyPayload payload;
    }

    [Serializable]
    public sealed class EnterLobbyResponseEnvelope : ProtocolEnvelope
    {
        public EnterLobbyResponsePayload payload;
    }

    [Serializable]
    public sealed class CreateRoomRequestEnvelope : ProtocolEnvelope
    {
        public CreateRoomRequestPayload payload;
    }

    [Serializable]
    public sealed class CreateRoomResponseEnvelope : ProtocolEnvelope
    {
        public CreateRoomResponsePayload payload;
    }

    [Serializable]
    public sealed class JoinRoomRequestEnvelope : ProtocolEnvelope
    {
        public JoinRoomRequestPayload payload;
    }

    [Serializable]
    public sealed class JoinRoomResponseEnvelope : ProtocolEnvelope
    {
        public JoinRoomResponsePayload payload;
    }

    [Serializable]
    public sealed class LeaveRoomRequestEnvelope : ProtocolEnvelope
    {
        public LeaveRoomRequestPayload payload;
    }

    [Serializable]
    public sealed class LeaveRoomResponseEnvelope : ProtocolEnvelope
    {
        public LeaveRoomResponsePayload payload;
    }

    [Serializable]
    public sealed class RoomStateNotificationEnvelope : ProtocolEnvelope
    {
        public RoomStateNotificationPayload payload;
    }

    [Serializable]
    public sealed class EnterLobbyResponsePayload
    {
        public RoomPlayerDto playerInfo;
        public RoomDto[] rooms;
    }

    [Serializable]
    public sealed class CreateRoomRequestPayload
    {
        public string roomName;
        public int maxPlayers;
    }

    [Serializable]
    public sealed class CreateRoomResponsePayload
    {
        public RoomDto room;
    }

    [Serializable]
    public sealed class JoinRoomRequestPayload
    {
        public string roomId;
    }

    [Serializable]
    public sealed class JoinRoomResponsePayload
    {
        public RoomDto room;
    }

    [Serializable]
    public sealed class LeaveRoomRequestPayload
    {
        public string roomId;
    }

    [Serializable]
    public sealed class LeaveRoomResponsePayload
    {
        public string roomId;
        public RoomDto room;
    }

    [Serializable]
    public sealed class RoomStateNotificationPayload
    {
        public RoomDto room;
    }

    [Serializable]
    public sealed class RoomDto
    {
        public string roomId;
        public string roomName;
        public string ownerPlayerId;
        public int maxPlayers;
        public string state;
        public RoomPlayerDto[] players;
    }

    [Serializable]
    public sealed class RoomPlayerDto
    {
        public string playerId;
        public string nickname;
    }
}
