using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace OnlineActionRpg.Client.Battle
{
    // BattleSceneEntry 是训练场景的入口标记，负责场景级的一次性初始化。
    public sealed class BattleSceneEntry : MonoBehaviour
    {
        private void Start()
        {
            Debug.Log("BattleArena_Training entered.");

            SuppressDuplicateEventSystem();
        }

        // 训练场是以 Additive 方式叠加到大堂场景上的（BattleSceneLoadController.loadSceneMode = Additive），
        // 两个场景各带一个 EventSystem，运行时会触发
        // "There are 2 event systems in the scene"，并让两套输入模块同时处理输入。
        //
        // 本场景没有需要射线检测的交互 UI：
        // - EmoteWheelView 自己用 ScreenPointToLocalPointInRectangle 算坐标，不走 EventSystem
        // - CombatDebugHud 用 OnGUI
        // - 木桩血条只显示，不接收点击
        // 所以叠加运行时禁用本场景的 EventSystem，保留大堂那一个。
        //
        // 如果将来战斗场景要加需要点击的 UI（结算面板按钮等），有两个选择：
        // 把 UI 挪进大堂的 Canvas，或者改成禁用大堂的 EventSystem —— 不能让两个同时活着。
        private void SuppressDuplicateEventSystem()
        {
            // 单场景直接 Play 时只有一个 EventSystem，不冲突，保持原样。
            if (SceneManager.sceneCount <= 1)
            {
                return;
            }

            Scene scene = gameObject.scene;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                EventSystem[] systems = root.GetComponentsInChildren<EventSystem>(true);

                foreach (EventSystem system in systems)
                {
                    system.enabled = false;

                    Debug.Log($"Disabled duplicate EventSystem in battle scene: {system.name}");
                }
            }
        }
    }
}
