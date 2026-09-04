using System;
using System.Text;
using OnlineActionRpg.Client.Lobby;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OnlineActionRpg.Client.UI
{
    // LobbyPanel 只负责大厅 / 房间界面表现：
    // 读取输入、调用 LobbyClient、显示房间列表和当前房间状态。
    public sealed class LobbyPanel : MonoBehaviour
    {
        [Header("Lobby Client")]
        [SerializeField] private LobbyClient lobbyClient;

        [Header("Input")]
        [SerializeField] private TMP_InputField roomNameInput;
        [SerializeField] private TMP_InputField maxPlayersInput;
        [SerializeField] private TMP_InputField joinRoomIdInput;

        [Header("Buttons")]
        [SerializeField] private Button refreshButton;
        [SerializeField] private Button createButton;
        [SerializeField] private Button createRoomButton;
        [SerializeField] private Button joinRoomButton;
        [SerializeField] private Button leaveRoomButton;
        [SerializeField] private Button creatCloseButton;

        [Header("Text")]
        [SerializeField] private TMP_Text lobbyStatusText;
        [SerializeField] private TMP_Text roomListText;
        [SerializeField] private TMP_Text currentRoomText;

        [Header("Plate")]
        [SerializeField] private GameObject roomPlate;
        [SerializeField] private GameObject creatRoomPlate;

        private RoomDto[] _rooms = Array.Empty<RoomDto>();
        private RoomDto _currentRoom;
        private bool _isWaitingResponse;

        private void Awake()
        {
            if (lobbyClient == null)
            {
                lobbyClient = FindFirstObjectByType<LobbyClient>();
            }

            if (refreshButton != null)
            {
                refreshButton.onClick.AddListener(OnRefreshClicked);
            }

            if (createRoomButton != null)
            {
                createRoomButton.onClick.AddListener(OnCreateRoomClicked);
            }

            if (createButton != null)
            {
                createButton.onClick.AddListener(OnCreateClicked);
            }

            if(creatCloseButton != null)
            {
                creatCloseButton.onClick.AddListener(OnCreateCloseClicked);
            }

            if (joinRoomButton != null)
            {
                joinRoomButton.onClick.AddListener(OnJoinRoomClicked);
            }

            if (leaveRoomButton != null)
            {
                leaveRoomButton.onClick.AddListener(OnLeaveRoomClicked);
            }

            if (lobbyClient != null)
            {
                lobbyClient.EnterLobbyCompleted += HandleEnterLobbyCompleted;
                lobbyClient.CreateRoomCompleted += HandleCreateRoomCompleted;
                lobbyClient.JoinRoomCompleted += HandleJoinRoomCompleted;
                lobbyClient.LeaveRoomCompleted += HandleLeaveRoomCompleted;
                lobbyClient.RoomStateChanged += HandleRoomStateChanged;
            }


            if (creatRoomPlate != null)
            {
                creatRoomPlate.SetActive(false);
            }

            if (roomPlate != null)
            {
                roomPlate.SetActive(false);
            }
            RefreshRoomListText();
            RefreshCurrentRoomText();
            SetStatus("Lobby ready.");
            RefreshButtons();
        }

        private void OnDestroy()
        {
            if (refreshButton != null)
            {
                refreshButton.onClick.RemoveListener(OnRefreshClicked);
            }

            if (createRoomButton != null)
            {
                createRoomButton.onClick.RemoveListener(OnCreateRoomClicked);
            }

            if (createButton != null)
            {
                createButton.onClick.RemoveListener(OnCreateClicked);
            }

            if (creatCloseButton != null)
            {
                creatCloseButton.onClick.RemoveListener(OnCreateCloseClicked);
            }

            if (joinRoomButton != null)
            {
                joinRoomButton.onClick.RemoveListener(OnJoinRoomClicked);
            }

            if (leaveRoomButton != null)
            {
                leaveRoomButton.onClick.RemoveListener(OnLeaveRoomClicked);
            }

            if (lobbyClient != null)
            {
                lobbyClient.EnterLobbyCompleted -= HandleEnterLobbyCompleted;
                lobbyClient.CreateRoomCompleted -= HandleCreateRoomCompleted;
                lobbyClient.JoinRoomCompleted -= HandleJoinRoomCompleted;
                lobbyClient.LeaveRoomCompleted -= HandleLeaveRoomCompleted;
                lobbyClient.RoomStateChanged -= HandleRoomStateChanged;
            }
        }

        private async void OnRefreshClicked()
        {
            if (!CanSendRequest())
            {
                return;
            }

            SetWaiting(true);
            SetStatus("Refreshing lobby...");

            //重写进一次大厅，拉取最新的房间列表。注意：如果当前客户端在房间中，EnterLobbyRes 里返回的房间列表是空的。
            await lobbyClient.EnterLobbyAsync();
        }

        private async void OnCreateRoomClicked()
        {
            if (!CanSendRequest())
            {
                return;
            }

            string roomName = GetInputText(roomNameInput);
            int maxPlayers = ParseMaxPlayers();

            if (string.IsNullOrWhiteSpace(roomName))
            {
                SetStatus("Create room failed. Room name is required.");
                return;
            }

            if (maxPlayers <= 0)
            {
                SetStatus("Create room failed. Max players must be greater than 0.");
                return;
            }

            SetWaiting(true);
            SetStatus("Creating room...");

            if (creatRoomPlate != null)
            {
                creatRoomPlate.SetActive(false);
            }

            await lobbyClient.CreateRoomAsync(roomName, maxPlayers);
        }

        private async void OnJoinRoomClicked()
        {
            if (!CanSendRequest())
            {
                return;
            }

            string roomId = NormalizeRoomIdInput(GetInputText(joinRoomIdInput));

            if (string.IsNullOrWhiteSpace(roomId))
            {
                SetStatus("Join room failed. Room id is required.");
                return;
            }

            SetWaiting(true);
            SetStatus("Joining room...");

            await lobbyClient.JoinRoomAsync(roomId);
        }

        private async void OnLeaveRoomClicked()
        {
            if (!CanSendRequest())
            {
                return;
            }

            if (_currentRoom == null || string.IsNullOrWhiteSpace(_currentRoom.roomId))
            {
                SetStatus("Leave room failed. You are not in a room.");
                return;
            }

            string roomId = _currentRoom.roomId;

            SetWaiting(true);
            SetStatus("Leaving room...");

            await lobbyClient.LeaveRoomAsync(roomId);
        }

        private void OnCreateClicked()
        {
            if (creatRoomPlate != null)
            {
                creatRoomPlate.SetActive(true);
            }
        }

        private void OnCreateCloseClicked()
        {
            if (creatRoomPlate != null)
            {
                creatRoomPlate.SetActive(false);
            }
        }


        private bool CanSendRequest()
        {
            if (_isWaitingResponse)
            {
                return false;
            }

            if (lobbyClient == null)
            {
                SetStatus("LobbyClient is missing.");
                return false;
            }

            return true;
        }

        //当服务器发回响应，开始处理操作请求
        private void HandleEnterLobbyCompleted(EnterLobbyResult result)
        {
            SetWaiting(false);

            if (!result.Success)
            {
                SetStatus($"Enter lobby failed. Code: {result.Code}, Message: {result.Message}");
                return;
            }

            _rooms = result.Rooms ?? Array.Empty<RoomDto>();
            RefreshRoomListText();

            SetStatus($"Enter lobby success. Room count: {_rooms.Length}");
        }

        private async void HandleCreateRoomCompleted(RoomCommandResult result)
        {
            SetWaiting(false);

            if (!result.Success)
            {
                SetStatus($"Create room failed. Code: {result.Code}, Message: {result.Message}");
                return;
            }

            _currentRoom = result.Room;
            RefreshCurrentRoomText();

            SetStatus($"Create room success. RoomId: {result.RoomId}");

            if(roomPlate != null)
            {
                roomPlate.SetActive(true);
            }

            await RefreshLobbyAfterRoomChangedAsync();
        }

        private async void HandleJoinRoomCompleted(RoomCommandResult result)
        {
            SetWaiting(false);

            if (!result.Success)
            {
                SetStatus($"Join room failed. Code: {result.Code}, Message: {result.Message}");
                return;
            }

            _currentRoom = result.Room;
            RefreshCurrentRoomText();

            SetStatus($"Join room success. RoomId: {result.RoomId}");

            if (roomPlate != null)
            {
                roomPlate.SetActive(true);
            }

            await RefreshLobbyAfterRoomChangedAsync();
        }

        private async void HandleLeaveRoomCompleted(RoomCommandResult result)
        {
            SetWaiting(false);

            if (!result.Success)
            {
                SetStatus($"Leave room failed. Code: {result.Code}, Message: {result.Message}");
                return;
            }

            // 对当前客户端来说，LeaveRoomRes 成功就代表自己已经离开当前房间。
            // 即使服务端返回了剩余房间快照，那也是给仍留在房间里的玩家看的状态。
            _currentRoom = null;
            RefreshCurrentRoomText();

            SetStatus($"Leave room success. RoomId: {result.RoomId}");
            if (roomPlate != null)
            {
                roomPlate.SetActive(false);
            }
            await RefreshLobbyAfterRoomChangedAsync();
        }

        private void HandleRoomStateChanged(RoomDto room)
        {
            if (room == null)
            {
                return;
            }

            _currentRoom = room;
            RefreshCurrentRoomText();

            SetStatus($"Room state updated. RoomId: {room.roomId}");
        }

        private async System.Threading.Tasks.Task RefreshLobbyAfterRoomChangedAsync()
        {
            if (lobbyClient == null)
            {
                return;
            }

            await lobbyClient.EnterLobbyAsync();
        }

        private void RefreshRoomListText()
        {
            if (roomListText == null)
            {
                return;
            }

            if (_rooms == null || _rooms.Length == 0)
            {
                roomListText.text = "Room List:\n(empty)";
                return;
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Room List:");

            for (int i = 0; i < _rooms.Length; i++)
            {
                RoomDto room = _rooms[i];

                if (room == null)
                {
                    continue;
                }

                int playerCount = room.players != null ? room.players.Length : 0;

                builder.AppendLine(
                    $"{i + 1}. [{room.roomId}] {room.roomName}  " +
                    $"{playerCount}/{room.maxPlayers}  Owner: {room.ownerPlayerId}  State: {room.state}");
            }

            roomListText.text = builder.ToString();
        }

        private void RefreshCurrentRoomText()
        {
            if (currentRoomText == null)
            {
                return;
            }

            if (_currentRoom == null)
            {
                currentRoomText.text = "Current Room:\n(not in room)";
                RefreshButtons();
                return;
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Current Room:");
            builder.AppendLine($"RoomId: {_currentRoom.roomId}");
            builder.AppendLine($"Name: {_currentRoom.roomName}");
            builder.AppendLine($"Owner: {_currentRoom.ownerPlayerId}");
            builder.AppendLine($"State: {_currentRoom.state}");

            int playerCount = _currentRoom.players != null ? _currentRoom.players.Length : 0;
            builder.AppendLine($"Players: {playerCount}/{_currentRoom.maxPlayers}");

            if (_currentRoom.players != null)
            {
                for (int i = 0; i < _currentRoom.players.Length; i++)
                {
                    RoomPlayerDto player = _currentRoom.players[i];

                    if (player == null)
                    {
                        continue;
                    }

                    string ownerTag = player.playerId == _currentRoom.ownerPlayerId ? " (Owner)" : string.Empty;
                    builder.AppendLine($"- {player.nickname} [{player.playerId}]{ownerTag}");
                }
            }

            currentRoomText.text = builder.ToString();
            RefreshButtons();
        }

        private void RefreshButtons()
        {
            bool canClick = !_isWaitingResponse;
            bool inRoom = _currentRoom != null && !string.IsNullOrWhiteSpace(_currentRoom.roomId);

            if (refreshButton != null)
            {
                refreshButton.interactable = canClick;
            }

            if (createRoomButton != null)
            {
                createRoomButton.interactable = canClick;
            }

            if (joinRoomButton != null)
            {
                joinRoomButton.interactable = canClick;
            }

            if (leaveRoomButton != null)
            {
                leaveRoomButton.interactable = canClick && inRoom;
            }
        }

        private void SetWaiting(bool isWaiting)
        {
            _isWaitingResponse = isWaiting;
            RefreshButtons();
        }

        private void SetStatus(string message)
        {
            if (lobbyStatusText != null)
            {
                lobbyStatusText.text = message;
            }
        }

        private int ParseMaxPlayers()
        {
            string value = GetInputText(maxPlayersInput);

            if (int.TryParse(value, out int maxPlayers))
            {
                return maxPlayers;
            }

            return 2;
        }

        private static string GetInputText(TMP_InputField input)
        {
            return input != null ? input.text.Trim() : string.Empty;
        }
        private static string NormalizeRoomIdInput(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            value = value.Trim();

            if (value.Length >= 2 && value[0] == '[' && value[value.Length - 1] == ']')
            {
                return value.Substring(1, value.Length - 2).Trim();
            }

            return value;
        }
    }
}