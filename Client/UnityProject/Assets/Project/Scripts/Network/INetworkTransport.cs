using System;
using System.Threading;
using System.Threading.Tasks;

namespace OnlineActionRpg.Client.Network
{
    public interface INetworkTransport
    {
        bool IsConnected { get; }

        event Action Connected;
        event Action<string> TextMessageReceived;
        event Action<string> Disconnected;
        event Action<Exception> Error;

        Task ConnectAsync(string url, CancellationToken cancellationToken);
        Task SendTextAsync(string message, CancellationToken cancellationToken);
        Task DisconnectAsync(CancellationToken cancellationToken);
    }
}
