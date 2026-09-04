using System;
using OnlineActionRpg.Client.Account;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;
using OnlineActionRpg.Client.Lobby;
using OnlineActionRpg.Client.Network;

namespace OnlineActionRpg.Client.UI
{
    // LoginPanel 只负责账号界面交互：读取输入、调用 AccountClient、显示结果。
    // 登录成功后切换到 Lobby 占位面板，真正的大厅逻辑会放到后续迭代。
    public sealed class LoginPanel : MonoBehaviour
    {
        private const string DefaultServerUrl = "ws://localhost:5050/ws";

        [Header("Network")]
        [SerializeField] private NetworkClient networkClient;
        [SerializeField] private TMP_InputField serverUrlInput;
        [SerializeField] private Button connectButton;
        [SerializeField] private Button disconnectButton;
        [SerializeField] private TMP_Text networkStateText;

        [Header("Account")]
        [SerializeField] private AccountClient accountClient;
        [SerializeField] private ClientSession session;

        [Header("Lobby")]
        [SerializeField] private LobbyClient lobbyClient;

        [Header("Input")]
        [SerializeField] private TMP_InputField usernameInput;
        [SerializeField] private TMP_InputField passwordInput;
        [SerializeField] private TMP_InputField nicknameInput;

        [Header("Buttons")]
        [SerializeField] private Button registerButton;
        [SerializeField] private Button loginButton;

        [Header("Panels")]
        [SerializeField] private GameObject loginPanelRoot;
        [SerializeField] private GameObject lobbyPanelRoot;

        [Header("Text")]
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text sessionText;

        private bool _isWaitingResponse;

        private void Awake()
        {
            if (networkClient == null)
            {
                networkClient = FindFirstObjectByType<NetworkClient>();
            }

            if (accountClient == null)
            {
                accountClient = FindFirstObjectByType<AccountClient>();
            }

            if (session == null)
            {
                session = FindFirstObjectByType<ClientSession>();
            }

            if (lobbyClient == null)
            {
                lobbyClient = FindFirstObjectByType<LobbyClient>();
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

            if (registerButton != null)
            {
                registerButton.onClick.AddListener(OnRegisterClicked);
            }

            if (loginButton != null)
            {
                loginButton.onClick.AddListener(OnLoginClicked);
            }

            if (accountClient != null)
            {
                accountClient.RegisterCompleted += HandleRegisterCompleted;
                accountClient.LoginCompleted += HandleLoginCompleted;
            }

            if (lobbyClient != null)
            {
                lobbyClient.EnterLobbyCompleted += HandleEnterLobbyCompleted;
            }

            ShowLoginPanel();
            SetStatus("请先连接服务器，然后选择注册或登录");
            RefreshConnectionStateText();
            RefreshSessionText();
            RefreshButtons();
        }

        private void Update()
        {
            RefreshConnectionStateText();
            RefreshSessionText();
            RefreshButtons();
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

            if (registerButton != null)
            {
                registerButton.onClick.RemoveListener(OnRegisterClicked);
            }

            if (loginButton != null)
            {
                loginButton.onClick.RemoveListener(OnLoginClicked);
            }

            if (accountClient != null)
            {
                accountClient.RegisterCompleted -= HandleRegisterCompleted;
                accountClient.LoginCompleted -= HandleLoginCompleted;
            }

            if (lobbyClient != null)
            {
                lobbyClient.EnterLobbyCompleted -= HandleEnterLobbyCompleted;
            }
        }

        //按下注册键注册
        private async void OnConnectClicked()
        {
            if (_isWaitingResponse)
            {
                return;
            }

            if (networkClient == null)
            {
                SetStatus("连接失败。NetworkClient is missing.");
                return;
            }

            string url = GetServerUrl();

            if (!IsValidWebSocketUrl(url))
            {
                SetStatus("连接失败。服务器地址应类似 ws://localhost:5050/ws");
                return;
            }

            SetWaiting(true);
            SetStatus($"正在连接服务器：{url}");

            await networkClient.ConnectAsync(url);

            SetWaiting(false);
            RefreshConnectionStateText();

            if (networkClient.IsConnected)
            {
                SetStatus("服务器连接成功，可以注册或登录。");
                return;
            }

            NetworkClientSnapshot snapshot = networkClient.GetSnapshot();
            string error = string.IsNullOrEmpty(snapshot.LastError) ? "Unknown error." : snapshot.LastError;
            SetStatus($"服务器连接失败。{error}");
        }

        private async void OnDisconnectClicked()
        {
            if (_isWaitingResponse)
            {
                return;
            }

            if (networkClient == null)
            {
                SetStatus("断开失败。NetworkClient is missing.");
                return;
            }

            SetWaiting(true);
            SetStatus("正在断开服务器连接...");

            await networkClient.DisconnectAsync();

            SetWaiting(false);
            RefreshConnectionStateText();
            SetStatus("服务器连接已断开。");
        }

        private async void OnRegisterClicked()
        {
            if (!CanSubmitAccountRequest())
            {
                return;
            }

            string username = GetInputText(usernameInput);
            string password = GetInputText(passwordInput);
            string nickname = GetInputText(nicknameInput);

            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(nickname))
            {
                SetStatus("注册失败！请填写账号、密码、昵称。");
                return;
            }

            SetWaiting(true);
            SetStatus("注册中...");

            await accountClient.RegisterAsync(username, password, nickname);
        }

        //按下登录键登录
        private async void OnLoginClicked()
        {
            if (!CanSubmitAccountRequest())
            {
                return;
            }

            string username = GetInputText(usernameInput);
            string password = GetInputText(passwordInput);

            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password))
            {
                SetStatus("登录失败！请填写账号或密码。");
                return;
            }

            SetWaiting(true);
            SetStatus("登录中...");

            await accountClient.LoginAsync(username, password);
        }

        private void HandleRegisterCompleted(AccountOperationResult result)
        {
            SetWaiting(false);

            if (result.Success)
            {
                SetStatus($"注册成功。 PlayerId: {result.PlayerId}.");
                return;
            }

            SetStatus($"注册失败。 Code: {result.Code}, Message: {result.Message}");
        }

        private void HandleLoginCompleted(AccountOperationResult result)
        {
            SetWaiting(false);

            if (!result.Success)
            {
                SetStatus($"登录失败。 Code: {result.Code}, Message: {result.Message}");
                return;
            }

            SetStatus($"登录成功。欢迎, {result.Nickname}。正在进入大厅...");
            RefreshSessionText();
            ShowLobbyPlaceholder();
            _ = EnterLobbyAfterLoginAsync();
        }

        private async Task EnterLobbyAfterLoginAsync()
        {
            if (lobbyClient == null)
            {
                SetStatus("进入大厅失败。LobbyClient is missing.");
                return;
            }

            await lobbyClient.EnterLobbyAsync();
        }

        private void HandleEnterLobbyCompleted(EnterLobbyResult result)
        {
            if (!result.Success)
            {
                SetStatus($"进入大厅失败。 Code: {result.Code}, Message: {result.Message}");
                return;
            }

            int roomCount = result.Rooms != null ? result.Rooms.Length : 0;
            SetStatus($"进入大厅成功。当前房间数: {roomCount}");
        }

        private void ShowLoginPanel()
        {
            if (loginPanelRoot != null)
            {
                loginPanelRoot.SetActive(true);
            }

            if (lobbyPanelRoot != null)
            {
                lobbyPanelRoot.SetActive(false);
            }
        }

        private void ShowLobbyPlaceholder()
        {
            if (loginPanelRoot != null)
            {
                loginPanelRoot.SetActive(false);
            }

            if (lobbyPanelRoot != null)
            {
                lobbyPanelRoot.SetActive(true);
            }
        }

        private void SetWaiting(bool isWaiting)
        {
            _isWaitingResponse = isWaiting;
            RefreshButtons();
        }

        private bool CanSubmitAccountRequest()
        {
            if (_isWaitingResponse)
            {
                return false;
            }

            if (networkClient == null)
            {
                SetStatus("操作失败。NetworkClient is missing.");
                return false;
            }

            if (!networkClient.IsConnected)
            {
                SetStatus("请先连接服务器。");
                return false;
            }

            return true;
        }

        private void RefreshButtons()
        {
            bool hasNetworkClient = networkClient != null;
            NetworkConnectionState state = hasNetworkClient ? networkClient.State : NetworkConnectionState.Disconnected;
            bool isConnected = hasNetworkClient && networkClient.IsConnected;
            bool isConnecting = state == NetworkConnectionState.Connecting;
            bool canInteract = !_isWaitingResponse;
            bool canSubmitAccount = canInteract && isConnected;

            if (registerButton != null)
            {
                registerButton.interactable = canSubmitAccount;
            }

            if (loginButton != null)
            {
                loginButton.interactable = canSubmitAccount;
            }

            if (connectButton != null)
            {
                connectButton.interactable = canInteract && hasNetworkClient && !isConnected && !isConnecting;
            }

            if (disconnectButton != null)
            {
                disconnectButton.interactable = canInteract && isConnected;
            }

            if (serverUrlInput != null)
            {
                serverUrlInput.interactable = canInteract && !isConnected && !isConnecting;
            }
        }

        private void RefreshConnectionStateText()
        {
            if (networkStateText == null)
            {
                return;
            }

            if (networkClient == null)
            {
                networkStateText.text = "Server: Missing NetworkClient";
                return;
            }

            NetworkClientSnapshot snapshot = networkClient.GetSnapshot();
            string errorLine = string.IsNullOrEmpty(snapshot.LastError)
                ? string.Empty
                : $"\nError: {snapshot.LastError}";

            networkStateText.text = $"Server: {snapshot.State}\nUrl: {GetServerUrl()}{errorLine}";
        }

        private void RefreshSessionText()
        {
            if (sessionText == null)
            {
                return;
            }

            if (session == null || !session.IsLoggedIn)
            {
                sessionText.text = "Session: Not logged in";
                return;
            }

            sessionText.text =
                $"Session: Logged in\n" +
                $"PlayerId: {session.PlayerId}\n" +
                $"Nickname: {session.Nickname}\n" +
                $"Token: {CreateTokenPreview(session.Token)}";
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        private static string GetInputText(TMP_InputField input)
        {
            return input != null ? input.text.Trim() : string.Empty;
        }

        private string GetServerUrl()
        {
            string url = GetInputText(serverUrlInput);
            return string.IsNullOrWhiteSpace(url) ? DefaultServerUrl : url;
        }

        private static bool IsValidWebSocketUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            return uri.Scheme == "ws" || uri.Scheme == "wss";
        }

        private static string CreateTokenPreview(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return string.Empty;
            }

            return token.Length <= 18 ? token : token.Substring(0, 18) + "...";
        }
    }
}
