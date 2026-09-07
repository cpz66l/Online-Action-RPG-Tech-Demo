using System;
using OnlineActionRpg.Client.Network;

namespace OnlineActionRpg.Client.Loading
{
    public static class LoadingMessageIds
    {
        public const int LoadBattleSceneNtf = 4001;
        public const int ClientLoadProgressNtf = 4002;
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
}