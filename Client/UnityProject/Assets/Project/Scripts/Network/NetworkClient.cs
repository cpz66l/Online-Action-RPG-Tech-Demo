using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace OnlineActionRpg.Client.Network
{
    public enum NetworkConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Error
    }

    public sealed class NetworkClient : MonoBehaviour
    {
        public event Action<string> TextMessageReceived;

        private readonly object _stateLock = new object();

        private INetworkTransport _transport;   //接口实现多态，可能是WebSocket或UDP/KCP
        private CancellationTokenSource _lifetimeCts;

        private NetworkConnectionState _state = NetworkConnectionState.Disconnected;
        private string _lastSentJson = string.Empty;
        private string _lastReceivedJson = string.Empty;
        private string _lastError = string.Empty;
        private long _lastRttMs = -1;
        private string _lastPingRequestId = string.Empty;
        private long _lastPingSentAtMs;

        public NetworkConnectionState State
        {
            get
            {
                lock (_stateLock)
                {
                    return _state;
                }
            }
        }

        public bool IsConnected
        {
            get
            {
                return _transport != null && _transport.IsConnected;
            }
        }

        private void Awake()
        {
            _transport = new WebSocketTransport();
            _lifetimeCts = new CancellationTokenSource();

            _transport.Connected += HandleConnected;
            _transport.TextMessageReceived += HandleTextMessageReceived;
            _transport.Disconnected += HandleDisconnected;
            _transport.Error += HandleError;
        }

        private void OnDestroy()
        {
            _ = DisconnectAsync();

            if (_lifetimeCts != null)
            {
                _lifetimeCts.Dispose(); //释放资源
                _lifetimeCts = null;    //置空取消令牌源
            }
        }

        public async Task ConnectAsync(string url)
        {
            if (IsConnected)
            {
                return;
            }

            SetState(NetworkConnectionState.Connecting);
            ClearError();

            try
            {
                await _transport.ConnectAsync(url, _lifetimeCts.Token);
            }
            catch (Exception ex)
            {
                SetError(ex.Message);
                SetState(NetworkConnectionState.Error);
            }
        }

        public async Task DisconnectAsync()
        {
            if (_transport == null)
            {
                return;
            }

            try
            {
                await _transport.DisconnectAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                SetError(ex.Message);
            }
            finally
            {
                SetState(NetworkConnectionState.Disconnected);
            }
        }

        //通用发送方法
        public async Task SendJsonAsync(string json)
        {
            if (!IsConnected)
            {
                SetError("Cannot send message: network is not connected.");
                return;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                SetError("Cannot send empty message.");
                return;
            }

            lock (_stateLock)
            {
                _lastSentJson = json;
                _lastError = string.Empty;
            }

            try
            {
                await _transport.SendTextAsync(json, _lifetimeCts.Token);
            }
            catch (Exception ex)
            {
                SetError(ex.Message);
            }
        }

        //发送心跳
        public async Task SendPingAsync()
        {
            if (!IsConnected)
            {
                SetError("Cannot send Ping: network is not connected.");
                return;
            }

            long now = GetUnixTimeMilliseconds();
            string requestId = Guid.NewGuid().ToString("N");

            PingRequestEnvelope request = new PingRequestEnvelope
            {
                msgId = NetworkMessageIds.PingReq,
                type = "PingReq",
                requestId = requestId,
                clientTime = now,
                payload = new PingRequestPayload
                {
                    clientTime = now
                }
            };

            string json = JsonUtility.ToJson(request);

            lock (_stateLock)
            {
                _lastPingRequestId = requestId;
                _lastPingSentAtMs = now;
                _lastSentJson = json;
                _lastError = string.Empty;
            }

            try
            {
                await _transport.SendTextAsync(json, _lifetimeCts.Token);
            }
            catch (Exception ex)
            {
                SetError(ex.Message);
            }
        }

        //用于Unity主线程在update里获取网络当前快照
        public NetworkClientSnapshot GetSnapshot()
        {
            lock (_stateLock)
            {
                return new NetworkClientSnapshot
                {
                    State = _state,
                    LastSentJson = _lastSentJson,
                    LastReceivedJson = _lastReceivedJson,
                    LastError = _lastError,
                    LastRttMs = _lastRttMs
                };
            }
        }


        private void HandleConnected()
        {
            SetState(NetworkConnectionState.Connected);
        }

        private void HandleDisconnected(string reason)
        {
            lock (_stateLock)
            {
                _state = NetworkConnectionState.Disconnected;
                _lastError = reason;
            }
        }

        private void HandleError(Exception ex)
        {
            SetError(ex.Message);
            SetState(NetworkConnectionState.Error);
        }

        private void HandleTextMessageReceived(string json)
        {
            ProtocolEnvelope envelope = JsonUtility.FromJson<ProtocolEnvelope>(json);//将受到的json反序列化回协议信封结构

            lock (_stateLock)
            {
                _lastReceivedJson = json;
            }

            TextMessageReceived?.Invoke(json);
            //WebSocket受到消息后触发事件传到NetworkClient后再触发事件传到AccountMessages

            if (envelope == null || string.IsNullOrEmpty(envelope.type))
            {
                SetError("Received invalid protocol envelope.");
                return;
            }

            if (envelope.type == "PingRes")
            {
                HandlePingResponse(json);
                return;
            }

            if (envelope.type == "ErrorRes")
            {
                SetError(envelope.message);
            }
        }

        private void HandlePingResponse(string json)
        {
            PingResponseEnvelope response = JsonUtility.FromJson<PingResponseEnvelope>(json);
            long receivedAtMs = GetUnixTimeMilliseconds();

            lock (_stateLock)
            {
                if (response.requestId == _lastPingRequestId)
                {
                    _lastRttMs = receivedAtMs - _lastPingSentAtMs;
                }

                _lastError = string.Empty;
            }
        }

        //小helper
        private void SetState(NetworkConnectionState state)
        {
            lock (_stateLock)
            {
                _state = state;
            }
        }

        private void SetError(string error)
        {
            lock (_stateLock)
            {
                _lastError = error;
            }
        }

        private void ClearError()
        {
            lock (_stateLock)
            {
                _lastError = string.Empty;
            }
        }

        private static long GetUnixTimeMilliseconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    //客户端网络快照结构定义
    public struct NetworkClientSnapshot 
    {
        public NetworkConnectionState State;
        public string LastSentJson;
        public string LastReceivedJson;
        public string LastError;
        public long LastRttMs;
    }
}

