using System;

namespace OnlineActionRpg.Client.Network
{
    [Serializable]
    public class ProtocolEnvelope
    {
        public int msgId;
        public string type;
        public string requestId;
        public string token;
        public long clientTime;
        public long serverTime;
        public int code;
        public string message;
    }

    // sealed 表示这个类不能再被继承，适合用于明确不会扩展的协议数据结构。
    [Serializable]
    public sealed class PingRequestEnvelope : ProtocolEnvelope
    {
        public PingRequestPayload payload;
    }

    [Serializable]
    public sealed class PingResponseEnvelope : ProtocolEnvelope
    {
        public PingResponsePayload payload;
    }

    [Serializable]
    public sealed class ErrorResponseEnvelope : ProtocolEnvelope
    {
        public EmptyPayload payload;
    }

    [Serializable]
    public sealed class PingRequestPayload
    {
        public long clientTime;
    }

    [Serializable]
    public sealed class PingResponsePayload
    {
        public long clientTime;
        public long serverTime;
    }

    [Serializable]
    public sealed class EmptyPayload
    {
    }
}

