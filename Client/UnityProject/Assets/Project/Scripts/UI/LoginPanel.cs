using OnlineActionRpg.Client.Account;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OnlineActionRpg.Client.UI
{
    // LoginPanel 只负责账号界面交互：读取输入、调用 AccountClient、显示结果。
    // 登录成功后切换到 Lobby 占位面板，真正的大厅逻辑会放到后续迭代。
    public sealed class LoginPanel : MonoBehaviour
    {
        [Header("Account")]
        [SerializeField] private AccountClient accountClient;
        [SerializeField] private ClientSession session;

        [Header("Input")]
        [SerializeField] private TMP_InputField usernameInput;
        [SerializeField] private TMP_InputField passwordInput;
        [SerializeField] private TMP_InputField nicknameInput;

        [Header("Buttons")]
        [SerializeField] private Button registerButton;
        [SerializeField] private Button loginButton;

        [Header("Panels")]
        [SerializeField] private GameObject loginPanelRoot;
        [SerializeField] private GameObject lobbyPlaceholderRoot;

        [Header("Text")]
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text sessionText;

        private bool _isWaitingResponse;

        private void Awake()
        {
            if (accountClient == null)
            {
                accountClient = FindFirstObjectByType<AccountClient>();
            }

            if (session == null)
            {
                session = FindFirstObjectByType<ClientSession>();
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

            ShowLoginPanel();
            SetStatus("请连接服务器，然后选择注册或登录");
            RefreshSessionText();
        }

        private void Update()
        {
            RefreshSessionText();
        }

        private void OnDestroy()
        {
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
        }

        //按下注册键注册
        private async void OnRegisterClicked()
        {
            if (_isWaitingResponse)
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
            if (_isWaitingResponse)
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

            SetStatus($"注册成功。 欢迎, {result.Nickname}.");
            RefreshSessionText();
            ShowLobbyPlaceholder();
        }

        private void ShowLoginPanel()
        {
            if (loginPanelRoot != null)
            {
                loginPanelRoot.SetActive(true);
            }

            if (lobbyPlaceholderRoot != null)
            {
                lobbyPlaceholderRoot.SetActive(false);
            }
        }

        private void ShowLobbyPlaceholder()
        {
            if (loginPanelRoot != null)
            {
                loginPanelRoot.SetActive(false);
            }

            if (lobbyPlaceholderRoot != null)
            {
                lobbyPlaceholderRoot.SetActive(true);
            }
        }

        private void SetWaiting(bool isWaiting)
        {
            _isWaitingResponse = isWaiting;

            //如果正在等待响应，则按钮不可交互
            if (registerButton != null)
            {
                registerButton.interactable = !isWaiting;
            }

            if (loginButton != null)
            {
                loginButton.interactable = !isWaiting;
            }
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
