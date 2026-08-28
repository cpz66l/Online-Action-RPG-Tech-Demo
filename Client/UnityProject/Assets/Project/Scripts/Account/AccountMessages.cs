using System;
using OnlineActionRpg.Client.Network;

namespace OnlineActionRpg.Client.Account
{
    //账号协议表格模板
    public static class AccountMessageIds
    {
        public const int RegisterReq = 1001;
        public const int RegisterRes = 1002;
        public const int LoginReq = 1003;
        public const int LoginRes = 1004;
    }

    //账号数据协议的封装
    [Serializable]
    public sealed class RegisterRequestEnvelope : ProtocolEnvelope
    {
        //ProtocolEnvelope里继承的是固定协议数据
        public RegisterRequestPayload payload;//payload里的才是具体数据
    }

    [Serializable]
    public sealed class RegisterResponseEnvelope : ProtocolEnvelope
    {
        public RegisterResponsePayload payload;
    }

    [Serializable]
    public sealed class LoginRequestEnvelope : ProtocolEnvelope
    {
        public LoginRequestPayload payload;
    }

    [Serializable]
    public sealed class LoginResponseEnvelope : ProtocolEnvelope
    {
        public LoginResponsePayload payload;
    }

    //账号协议的具体数据
    [Serializable]
    public sealed class RegisterRequestPayload
    {
        public string username;
        public string password;
        public string nickname;
    }

    [Serializable]
    public sealed class RegisterResponsePayload
    {
        public string playerId;
        public string nickname;
    }

    [Serializable]
    public sealed class LoginRequestPayload
    {
        public string username;
        public string password;
    }

    [Serializable]
    public sealed class LoginResponsePayload
    {
        public string token;
        public string playerId;
        public string nickname;
    }
}
