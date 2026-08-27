using OnlineActionRpg.Client.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OnlineActionRpg.Client.UI
{
    public sealed class NetworkDebugPanel : MonoBehaviour
    {
        private const string DefaultServerUrl = "ws://localhost:5050/ws";

        [Header("Network")]
        [SerializeField] private NetworkClient networkClient;

        [Header("Controls")]
        [SerializeField] private TMP_InputField serverUrlInput;
        [SerializeField] private Button connectButton;
        [SerializeField] private Button disconnectButton;
        [SerializeField] private Button pingButton;

        [Header("Status Text")]
        [SerializeField] private TMP_Text stateText;
        [SerializeField] private TMP_Text rttText;
        [SerializeField] private TMP_Text lastSentText;
        [SerializeField] private TMP_Text lastReceivedText;
        [SerializeField] private TMP_Text errorText;

        private void Awake()
        {
            if (networkClient == null)
            {
                networkClient = FindFirstObjectByType<NetworkClient>();
            }

            if (serverUrlInput != null && string.IsNullOrWhiteSpace(serverUrlInput.text))
            {
                serverUrlInput.text = DefaultServerUrl;
            }

            if (connectButton != null)
            {
                connectButton.onClick.AddListener(OnConnectClicked);
            }

            if (disconnectButton != null)
            {
                disconnectButton.onClick.AddListener(OnDisconnectClicked);
            }

            if (pingButton != null)
            {
                pingButton.onClick.AddListener(OnPingClicked);
            }
        }

        private void Update()
        {
            if (networkClient == null)
            {
                SetText(stateText, "State: Missing NetworkClient");
                return;
            }

            NetworkClientSnapshot snapshot = networkClient.GetSnapshot();

            SetText(stateText, $"State: {snapshot.State}");
            SetText(rttText, snapshot.LastRttMs >= 0 ? $"RTT: {snapshot.LastRttMs} ms" : "RTT: -");
            SetText(lastSentText, $"Last Sent:\n{snapshot.LastSentJson}");
            SetText(lastReceivedText, $"Last Received:\n{snapshot.LastReceivedJson}");
            SetText(errorText, string.IsNullOrEmpty(snapshot.LastError) ? "Error: -" : $"Error: {snapshot.LastError}");

            bool isConnected = snapshot.State == NetworkConnectionState.Connected;
            bool isConnecting = snapshot.State == NetworkConnectionState.Connecting;

            if (connectButton != null)
            {
                connectButton.interactable = !isConnected && !isConnecting;
                //如果已经连接或者正在连接，则连接按钮无法交互
            }

            if (disconnectButton != null)
            {
                disconnectButton.interactable = isConnected;
                //断开连接按钮只有在已连接的状态下可交互
            }

            if (pingButton != null)
            {
                pingButton.interactable = isConnected;
                //心跳请求按钮只有在已连接的状态下才可交互
            }
        }

        private void OnDestroy()
        {
            if (connectButton != null)
            {
                connectButton.onClick.RemoveListener(OnConnectClicked);
            }

            if (disconnectButton != null)
            {
                disconnectButton.onClick.RemoveListener(OnDisconnectClicked);
            }

            if (pingButton != null)
            {
                pingButton.onClick.RemoveListener(OnPingClicked);
            }
        }

        private async void OnConnectClicked()
        {
            if (networkClient == null)
            {
                return;
            }

            string url = serverUrlInput != null && !string.IsNullOrWhiteSpace(serverUrlInput.text)
                ? serverUrlInput.text
                : DefaultServerUrl;

            await networkClient.ConnectAsync(url);
        }

        private async void OnDisconnectClicked()
        {
            if (networkClient == null)
            {
                return;
            }

            await networkClient.DisconnectAsync();
        }

        private async void OnPingClicked()
        {
            if (networkClient == null)
            {
                return;
            }

            await networkClient.SendPingAsync();
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }
    }
}
