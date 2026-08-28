using UnityEngine;

namespace OnlineActionRpg.Client.Account
{
    // ClientSession 保存客户端当前登录会话。
    // 第一版只保存在内存中，退出 Play Mode 后会清空。
    public sealed class ClientSession : MonoBehaviour
    {
        public string Token { get; private set; } = string.Empty;
        public string PlayerId { get; private set; } = string.Empty;
        public string Nickname { get; private set; } = string.Empty;

        public bool IsLoggedIn
        {
            get
            {
                return !string.IsNullOrWhiteSpace(Token) &&
                       !string.IsNullOrWhiteSpace(PlayerId);
            }
        }

        public void SetLoginSession(string token, string playerId, string nickname)
        {
            Token = token ?? string.Empty;
            PlayerId = playerId ?? string.Empty;
            Nickname = nickname ?? string.Empty;
        }

        public void Clear()
        {
            Token = string.Empty;
            PlayerId = string.Empty;
            Nickname = string.Empty;
        }
    }
}

