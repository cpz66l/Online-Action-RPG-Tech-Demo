using System;
using System.Threading.Tasks;
using OnlineActionRpg.Client.Network;
using UnityEngine;
using System.Threading;

namespace OnlineActionRpg.Client.Account
{
    // 客户端账号业务入口。
    // 负责组装 RegisterReq / LoginReq，发送给 NetworkClient，并解析账号响应。
    public sealed class AccountClient : MonoBehaviour
    {
        [SerializeField] private NetworkClient networkClient;
        [SerializeField] private ClientSession session;

        private string _pendingRegisterRequestId = string.Empty;
        private string _pendingLoginRequestId = string.Empty;
        private SynchronizationContext _unityContext;

        public event Action<AccountOperationResult> RegisterCompleted;
        public event Action<AccountOperationResult> LoginCompleted;

        private void Awake()
        {
            //捕获Unity主线程上下文，缓存到_unityContext
            _unityContext = SynchronizationContext.Current;

            if (networkClient == null)
            {
                networkClient = FindFirstObjectByType<NetworkClient>();
            }

            if (session == null)
            {
                session = FindFirstObjectByType<ClientSession>();
            }

            if (networkClient != null)
            {
                networkClient.TextMessageReceived += HandleTextMessageReceived;
            }
        }

        private void OnDestroy()
        {
            if (networkClient != null)
            {
                networkClient.TextMessageReceived -= HandleTextMessageReceived;
            }
        }

        //注册
        public async Task RegisterAsync(string username, string password, string nickname)
        {
            if (!EnsureNetworkReady(RegisterCompleted))
            {
                return;
            }

            long now = GetUnixTimeMilliseconds();
            string requestId = Guid.NewGuid().ToString("N");

            //将注册表填好
            RegisterRequestEnvelope request = new RegisterRequestEnvelope
            {
                msgId = AccountMessageIds.RegisterReq,
                type = "RegisterReq",
                requestId = requestId,
                clientTime = now,
                payload = new RegisterRequestPayload
                {
                    username = username,
                    password = password,
                    nickname = nickname
                }
            };

            _pendingRegisterRequestId = requestId;//记住注册请求id,方便对应响应
            //将注册表打成json,发给networkClient中转，转发到服务器
            string json = JsonUtility.ToJson(request);
            await networkClient.SendJsonAsync(json);
        }

        //登录
        public async Task LoginAsync(string username, string password)
        {
            if (!EnsureNetworkReady(LoginCompleted))
            {
                return;
            }

            long now = GetUnixTimeMilliseconds();
            string requestId = Guid.NewGuid().ToString("N");

            LoginRequestEnvelope request = new LoginRequestEnvelope
            {
                msgId = AccountMessageIds.LoginReq,
                type = "LoginReq",
                requestId = requestId,
                clientTime = now,
                payload = new LoginRequestPayload
                {
                    username = username,
                    password = password
                }
            };

            _pendingLoginRequestId = requestId;
            string json = JsonUtility.ToJson(request);
            await networkClient.SendJsonAsync(json);
        }

        //从接收到的服务器响应中拿到属于自己的请求响应
        private void HandleTextMessageReceived(string json)
        {
            //先把服务器发来的json反序列化成读得懂的协议信封
            ProtocolEnvelope envelope = JsonUtility.FromJson<ProtocolEnvelope>(json);

            if (envelope == null || string.IsNullOrEmpty(envelope.type))
            {
                return;
            }

            //根据响应类型，分发给不同的业务去操作
            if (envelope.type == "RegisterRes")
            {
                HandleRegisterResponse(json);
                return;
            }

            if (envelope.type == "LoginRes")
            {
                HandleLoginResponse(json);
                return;
            }

            if (envelope.type == "ErrorRes")
            {
                HandleErrorResponse(envelope);
            }
        }

        //处理注册响应
        private void HandleRegisterResponse(string json)
        {
            RegisterResponseEnvelope response = JsonUtility.FromJson<RegisterResponseEnvelope>(json);

            //确认该响应是否匹配注册请求
            if (response.requestId != _pendingRegisterRequestId)
            {
                return;
            }

            //匹配成功，初始化，等待下一次注册请求
            _pendingRegisterRequestId = string.Empty;

            RaiseRegisterCompleted(AccountOperationResult.Ok(
                response.message,
                response.payload.playerId,
                response.payload.nickname,
                string.Empty));
        }

        //处理登录响应
        private void HandleLoginResponse(string json)
        {
            LoginResponseEnvelope response = JsonUtility.FromJson<LoginResponseEnvelope>(json);

            if (response.requestId != _pendingLoginRequestId)
            {
                return;
            }

            _pendingLoginRequestId = string.Empty;

            if (session != null)
            {
                session.SetLoginSession(
                    response.payload.token,
                    response.payload.playerId,
                    response.payload.nickname);
            }

            RaiseLoginCompleted(AccountOperationResult.Ok(
                response.message,
                response.payload.playerId,
                response.payload.nickname,
                response.payload.token));
        }

        //处理错误响应
        private void HandleErrorResponse(ProtocolEnvelope response)
        {
            //分别处理登录与注册的错误响应
            if (response.requestId == _pendingRegisterRequestId)
            {
                _pendingRegisterRequestId = string.Empty;

                RaiseRegisterCompleted(AccountOperationResult.Fail(
                    response.code,
                    response.message));

                return;
            }

            if (response.requestId == _pendingLoginRequestId)
            {
                _pendingLoginRequestId = string.Empty;

                RaiseLoginCompleted(AccountOperationResult.Fail(
                    response.code,
                    response.message));
            }
        }

        //根据操作，确认网络准备状态。
        private bool EnsureNetworkReady(Action<AccountOperationResult> callback)
        {
            if (networkClient == null)
            {
                callback?.Invoke(AccountOperationResult.Fail(1001, "NetworkClient is missing."));
                return false;
            }

            if (!networkClient.IsConnected)
            {
                callback?.Invoke(AccountOperationResult.Fail(1001, "Network is not connected."));
                return false;
            }

            return true;
        }

        private static long GetUnixTimeMilliseconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private void RaiseRegisterCompleted(AccountOperationResult result)
        {
            RaiseOnMainThread(() => RegisterCompleted?.Invoke(result));
        }

        private void RaiseLoginCompleted(AccountOperationResult result)
        {
            RaiseOnMainThread(() => LoginCompleted?.Invoke(result));
        }

        private void RaiseOnMainThread(Action action)
        {
            if (action == null)
            {
                return;
            }

            if (_unityContext == null || SynchronizationContext.Current == _unityContext)
            {
                action.Invoke();
                return;
            }

            //使用Post将事件的Invoke放进Unity主线程专属的全局任务队列,
            //让主线程安全监听
            _unityContext.Post(_ => action.Invoke(), null);
        }
    }

    //操作账号结果的数据定义
    public readonly struct AccountOperationResult
    {
        public readonly bool Success;
        public readonly int Code;
        public readonly string Message;
        public readonly string PlayerId;
        public readonly string Nickname;
        public readonly string Token;

        private AccountOperationResult(
            bool success,
            int code,
            string message,
            string playerId,
            string nickname,
            string token)
        {
            Success = success;
            Code = code;
            Message = message ?? string.Empty;
            PlayerId = playerId ?? string.Empty;
            Nickname = nickname ?? string.Empty;
            Token = token ?? string.Empty;
        }

        //静态方法，根据结果构造不同AccountOperationResult数据
        public static AccountOperationResult Ok(string message, string playerId, string nickname, string token)
        {
            return new AccountOperationResult(true, 0, message, playerId, nickname, token);
        }

        public static AccountOperationResult Fail(int code, string message)
        {
            return new AccountOperationResult(false, code, message, string.Empty, string.Empty, string.Empty);
        }
    }
}

