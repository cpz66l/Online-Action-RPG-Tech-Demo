using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace OnlineActionRpg.Client.Resource
{
    // AddressablesResourceService 只负责资源系统能力。
    public sealed class AddressablesResourceService : MonoBehaviour
    {
        public event Action<ResourceProgressInfo> ProgressChanged;
        public bool IsInitialized { get; private set; }
        public bool IsInitializing { get; private set; }
        public string LastError { get; private set; } = string.Empty;

        private AsyncOperationHandle<SceneInstance> _loadedBattleSceneHandle;
        private bool _hasLoadedBattleScene;
        private string _loadedBattleSceneKey = string.Empty;

        private Task<bool> _initializeTask;

        public Task<bool> InitializeAsync()
        {
            if (IsInitialized)
            {
                RaiseProgress("AddressablesInitialized", 1f, "Addressables already initialized.");
                return Task.FromResult(true);
            }

            if (_initializeTask != null)
            {
                return _initializeTask;
            }

            _initializeTask = InitializeInternalAsync();
            return _initializeTask;
        }

        private async Task<bool> InitializeInternalAsync()
        {
            IsInitializing = true;
            LastError = string.Empty;

            RaiseProgress("AddressablesInitializing", 0f, "Initializing Addressables...");

            //为 false 时，返回的 AsyncOperationHandle 需要手动释放，以便在初始化过程中监控进度；
            //若为 true，则操作完成后自动释放，但无法获取中间进度。
            AsyncOperationHandle<IResourceLocator> handle = Addressables.InitializeAsync(false);
            //加载 Addressables 的配置和资源定位表（IResourceLocator）
            try
            {
                while (!handle.IsDone)
                {
                    RaiseProgress(
                        "AddressablesInitializing",
                        handle.PercentComplete,
                        "Initializing Addressables...");

                    //等待下一帧，避免阻塞主线程。
                    await Task.Yield();
                }

                if (handle.Status != AsyncOperationStatus.Succeeded)
                {
                    LastError = handle.OperationException != null
                        ? handle.OperationException.Message
                        : "Addressables initialization failed.";

                    RaiseProgress("AddressablesInitializeFailed", 1f, LastError);
                    return false;
                }

                IsInitialized = true;
                RaiseProgress("AddressablesInitialized", 1f, "Addressables initialized.");
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                RaiseProgress("AddressablesInitializeFailed", 1f, LastError);
                return false;
            }
            finally
            {
                IsInitializing = false;

                if (handle.IsValid())
                {
                    //释放与 AsyncOperationHandle 关联的资源（包括内部使用的引用计数），防止内存泄漏。
                    Addressables.Release(handle);
                }

                if (!IsInitialized)
                {
                    _initializeTask = null;
                }
            }
        }

        // 加载战斗场景的异步方法，返回一个 Task<bool>，表示加载是否成功。
        public async Task<bool> LoadBattleSceneAsync(string sceneKey, LoadSceneMode loadMode = LoadSceneMode.Additive)
        {
            sceneKey = sceneKey != null ? sceneKey.Trim() : string.Empty;

            if (string.IsNullOrWhiteSpace(sceneKey))
            {
                LastError = "Battle scene key is required.";
                RaiseProgress("BattleSceneLoadFailed", 1f, LastError);
                return false;
            }

            if (_hasLoadedBattleScene && _loadedBattleSceneHandle.IsValid())
            {
                RaiseProgress("BattleSceneLoaded", 1f, $"Battle scene already loaded: {_loadedBattleSceneKey}");
                return true;
            }

            if (!IsInitialized)
            {
                bool initialized = await InitializeAsync();

                if (!initialized)
                {
                    return false;
                }
            }

            RaiseProgress("BattleSceneLoading", 0f, $"Loading battle scene: {sceneKey}");

            AsyncOperationHandle<SceneInstance> handle = Addressables.LoadSceneAsync(
                sceneKey,
                loadMode,
                true);

            try
            {
                while (!handle.IsDone)
                {
                    RaiseProgress(
                        "BattleSceneLoading",
                        handle.PercentComplete,
                        $"Loading battle scene: {sceneKey}");

                    await Task.Yield();
                }

                if (handle.Status != AsyncOperationStatus.Succeeded)
                {
                    LastError = handle.OperationException != null
                        ? handle.OperationException.Message
                        : $"Battle scene load failed: {sceneKey}";

                    RaiseProgress("BattleSceneLoadFailed", 1f, LastError);
                    return false;
                }

                _loadedBattleSceneHandle = handle;
                _hasLoadedBattleScene = true;
                _loadedBattleSceneKey = sceneKey;

                Scene loadedScene = handle.Result.Scene;

                if (loadedScene.IsValid())
                { 
                    SceneManager.SetActiveScene(loadedScene);
                    //让Unity后续创建对象时默认归属到BattleArena_Training
                }

                RaiseProgress("BattleSceneLoaded", 1f, $"Battle scene loaded: {sceneKey}");
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                RaiseProgress("BattleSceneLoadFailed", 1f, LastError);

                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }

                return false;
            }
        }

        private void RaiseProgress(string stage, float progress, string message)
        {
            ProgressChanged?.Invoke(new ResourceProgressInfo(
                stage,
                Mathf.Clamp01(progress),
                message));
        }
    }

    public readonly struct ResourceProgressInfo
    {
        public readonly string Stage;
        public readonly float Progress;
        public readonly string Message;

        public ResourceProgressInfo(string stage, float progress, string message)
        {
            Stage = stage ?? string.Empty;
            Progress = Mathf.Clamp01(progress);
            Message = message ?? string.Empty;
        }
    }
}
