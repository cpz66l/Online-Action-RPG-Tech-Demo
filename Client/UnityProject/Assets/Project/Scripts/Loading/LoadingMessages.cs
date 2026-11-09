using System;
using OnlineActionRpg.Client.Network;
using OnlineActionRpg.Client.Lobby;

namespace OnlineActionRpg.Client.Loading
{
    public static class LoadingMessageIds
    {
        public const int LoadBattleSceneNtf = 4001;
        public const int ClientLoadProgressNtf = 4002;
        public const int ClientBattleReadyReq = 4003;
        public const int ClientBattleReadyRes = 4004;
        public const int BattleStartNtf = 4005;
    }

    [Serializable]
    public sealed class LoadBattleSceneNotificationEnvelope : ProtocolEnvelope
    {
        public LoadBattleSceneNotificationPayload payload;
    }

    [Serializable]
    public sealed class ClientLoadProgressNotificationEnvelope : ProtocolEnvelope
    {
        public ClientLoadProgressNotificationPayload payload;
    }

    [Serializable]
    public sealed class ClientBattleReadyRequestEnvelope : ProtocolEnvelope
    {
        public ClientBattleReadyRequestPayload payload;
    }

    [Serializable]
    public sealed class ClientBattleReadyResponseEnvelope : ProtocolEnvelope
    {
        public ClientBattleReadyResponsePayload payload;
    }

    [Serializable]
    public sealed class BattleStartNotificationEnvelope : ProtocolEnvelope
    {
        public BattleStartNotificationPayload payload;
    }

    [Serializable]
    public sealed class LoadBattleSceneNotificationPayload
    {
        public string battleId;
        public string roomId;
        public string sceneKey;
        public string[] requiredAssets;
    }

    [Serializable]
    public sealed class ClientLoadProgressNotificationPayload
    {
        public string battleId;
        public string roomId;
        public float progress;
        public string stage;
        public string message;
    }

    [Serializable]
    public sealed class ClientBattleReadyRequestPayload
    {
        public string battleId;
        public string roomId;
    }

    [Serializable]
    public sealed class ClientBattleReadyResponsePayload
    {
        public string battleId;
        public RoomDto room;
    }

    [Serializable]
    public sealed class BattleStartNotificationPayload
    {
        public string battleId;
        public string roomId;
        public long serverStartTime;

        // 本场战斗的全体成员与各自的出生点。由服务端分配，客户端只负责把人摆到指定位置。
        public BattlePlayerSpawnPayload[] players;
    }

    [Serializable]
    public sealed class BattlePlayerSpawnPayload
    {
        public string playerId;
        public string nickname;
        public int spawnIndex;
        public Vector3Payload position;
        public float yaw;
    }

    [Serializable]
    public sealed class Vector3Payload
    {
        public float x;
        public float y;
        public float z;
    }
}