using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OnlineActionRpg.Client.Network
{
    public sealed class WebSocketTransport : INetworkTransport
    {
        private const int ReceiveBufferSize = 4096;

        private ClientWebSocket _socket;
        private CancellationTokenSource _receiveCts;

        public bool IsConnected
        {
            get
            {
                return _socket != null && _socket.State == WebSocketState.Open;
            }
        }

        public event Action Connected;
        public event Action<string> TextMessageReceived;
        public event Action<string> Disconnected;
        public event Action<Exception> Error;

        public async Task ConnectAsync(string url, CancellationToken cancellationToken)
        {
            if (IsConnected)
            {
                return;
            }

            CleanupSocket();

            _socket = new ClientWebSocket();
            await _socket.ConnectAsync(new Uri(url), cancellationToken);

            _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            Connected?.Invoke();

            _ = ReceiveLoopAsync(_receiveCts.Token);
        }

        public async Task SendTextAsync(string message, CancellationToken cancellationToken)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("WebSocket is not connected.");
            }

            byte[] bytes = Encoding.UTF8.GetBytes(message);
            ArraySegment<byte> segment = new ArraySegment<byte>(bytes);

            await _socket.SendAsync(
                segment,
                WebSocketMessageType.Text,
                true,
                cancellationToken);
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            if (_receiveCts != null)
            {
                _receiveCts.Cancel();
            }

            if (_socket != null &&
                (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived))
            {
                await _socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "client disconnect",
                    cancellationToken);
            }

            CleanupSocket();
            Disconnected?.Invoke("Client disconnected.");
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[ReceiveBufferSize];

            try
            {
                while (IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    string message = await ReceiveTextMessageAsync(buffer, cancellationToken);

                    if (message == null)
                    {
                        break;
                    }

                    TextMessageReceived?.Invoke(message);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal path when DisconnectAsync cancels the receive loop.
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex);
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    CleanupSocket();
                    Disconnected?.Invoke("Connection closed by remote.");
                }
            }
        }

        private async Task<string> ReceiveTextMessageAsync(byte[] buffer, CancellationToken cancellationToken)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                while (true)
                {
                    WebSocketReceiveResult result = await _socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return null;
                    }

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        throw new InvalidOperationException("Only text WebSocket messages are supported.");
                    }

                    stream.Write(buffer, 0, result.Count);

                    if (result.EndOfMessage)
                    {
                        return Encoding.UTF8.GetString(stream.ToArray());
                    }
                }
            }
        }

        private void CleanupSocket()
        {
            if (_receiveCts != null)
            {
                _receiveCts.Dispose();
                _receiveCts = null;
            }

            if (_socket != null)
            {
                _socket.Dispose();
                _socket = null;
            }
        }
    }
}