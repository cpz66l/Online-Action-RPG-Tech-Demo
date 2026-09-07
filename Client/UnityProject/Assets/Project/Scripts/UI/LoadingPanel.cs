using System.Text;
using OnlineActionRpg.Client.Loading;
using OnlineActionRpg.Client.Resource;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OnlineActionRpg.Client.UI
{
    // LoadingPanel 只负责显示 Loading 阶段信息。
    public sealed class LoadingPanel : MonoBehaviour
    {
        [Header("Client")]
        [SerializeField] private LoadingClient loadingClient;
        [SerializeField] private AddressablesResourceService resourceService;

        private bool _isInitializingResources;

        [Header("Root")]
        [SerializeField] private GameObject loadingRoot;

        [Header("Text")]
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text battleIdText;
        [SerializeField] private TMP_Text roomIdText;
        [SerializeField] private TMP_Text sceneKeyText;
        [SerializeField] private TMP_Text requiredAssetsText;
        [SerializeField] private TMP_Text progressText;

        [Header("Progress")]
        [SerializeField] private Slider progressSlider;

        private LoadBattleSceneTaskInfo _currentTask;
        private float _lastReportedProgress = -1f;
        private string _lastReportedStage = string.Empty;

        //每10%进度广播一次.
        private const float ProgressReportStep = 0.1f;

        private void Awake()
        {
            if (loadingClient == null)
            {
                loadingClient = FindFirstObjectByType<LoadingClient>();
            }

            if (resourceService == null)
            {
                resourceService = FindFirstObjectByType<AddressablesResourceService>();
            }

            if (loadingClient != null)
            {
                loadingClient.LoadBattleSceneReceived += HandleLoadBattleSceneReceived;
            }

            if (resourceService != null)
            {
                resourceService.ProgressChanged += HandleResourceProgressChanged;
            }

            Hide();
        }

        private void OnDestroy()
        {
            if (loadingClient != null)
            {
                loadingClient.LoadBattleSceneReceived -= HandleLoadBattleSceneReceived;
            }

            if (resourceService != null)
            {
                resourceService.ProgressChanged -= HandleResourceProgressChanged;
            }
        }

        private async void HandleLoadBattleSceneReceived(LoadBattleSceneTaskInfo task)
        {
            Show();

            SetText(statusText, "Loading task received.");
            SetText(battleIdText, $"BattleId: {task.BattleId}");
            SetText(roomIdText, $"RoomId: {task.RoomId}");
            SetText(sceneKeyText, $"SceneKey: {task.SceneKey}");
            SetText(requiredAssetsText, BuildRequiredAssetsText(task));

            if (progressSlider != null)
            {
                progressSlider.value = 0f;
            }

            if (resourceService == null)
            {
                SetText(statusText, "Resource service missing.");
                SetText(progressText, "Progress: failed before Addressables init.");
                return;
            }

            if (_isInitializingResources)
            {
                SetText(statusText, "Addressables initialization already running.");
                return;
            }

            _isInitializingResources = true;
            SetText(statusText, "Initializing Addressables...");
            SetText(progressText, "Progress: 0%");

            //开启 Addressables 初始化
            bool initialized = await resourceService.InitializeAsync();

            _isInitializingResources = false;

            if (!initialized)
            {
                SetText(statusText, $"Addressables init failed: {resourceService.LastError}");
                return;
            }

            SetText(statusText, "Addressables initialized. Waiting for scene loading step.");
        }

        // 处理资源加载进度变化事件，UI更新显示加载进度。每帧轮询，确保UI显示最新的加载进度。
        private void HandleResourceProgressChanged(ResourceProgressInfo progress)
        {
            if (progressSlider != null)
            {
                progressSlider.value = progress.Progress;
            }

            SetText(progressText, $"Progress: {progress.Progress:P0}");
            SetText(statusText, $"{progress.Stage}: {progress.Message}");
            //每帧轮询，只有相比上次广播进度变化10%进度再广播。
            TryReportLoadProgress(progress);
        }

        private void TryReportLoadProgress(ResourceProgressInfo progress)
        {
            if (loadingClient == null || !_currentTask.IsValid)
            {
                return;
            }

            float currentProgress = Mathf.Clamp01(progress.Progress);
            string currentStage = progress.Stage ?? string.Empty;

            bool stageChanged = currentStage != _lastReportedStage;
            bool reachedStep = _lastReportedProgress < 0f ||
                               currentProgress - _lastReportedProgress >= ProgressReportStep;
            bool completedFirstTime = currentProgress >= 0.999f &&
                                      _lastReportedProgress < 0.999f;

            //当阶段发生变化，或者进度达到10%步长，或者第一次完成时，才广播进度。
            if (!stageChanged && !reachedStep && !completedFirstTime)
            {
                return;
            }

            _lastReportedStage = currentStage;
            _lastReportedProgress = currentProgress;

            //广播加载进度给服务器
            _ = loadingClient.SendLoadProgressAsync(
                _currentTask.BattleId,
                _currentTask.RoomId,
                currentProgress,
                currentStage,
                progress.Message);
        }

        private void Show()
        {
            if (loadingRoot != null)
            {
                loadingRoot.SetActive(true);
            }
        }

        private void Hide()
        {
            if (loadingRoot != null)
            {
                loadingRoot.SetActive(false);
            }

            if (progressSlider != null)
            {
                progressSlider.value = 0f;
            }

            SetText(statusText, "Loading panel ready.");
            SetText(battleIdText, "BattleId: -");
            SetText(roomIdText, "RoomId: -");
            SetText(sceneKeyText, "SceneKey: -");
            SetText(requiredAssetsText, "Required Assets:\n-");
            SetText(progressText, "Progress: -");
        }

        private static string BuildRequiredAssetsText(LoadBattleSceneTaskInfo task)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Required Assets:");

            if (task.RequiredAssets == null || task.RequiredAssets.Length == 0)
            {
                builder.AppendLine("- none");
                return builder.ToString();
            }

            for (int i = 0; i < task.RequiredAssets.Length; i++)
            {
                builder.AppendLine($"- {task.RequiredAssets[i]}");
            }

            return builder.ToString();
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
            {
                text.text = value;
            }
        }
    }
}