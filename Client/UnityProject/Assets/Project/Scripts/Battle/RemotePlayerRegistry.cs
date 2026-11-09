using System;
using System.Collections.Generic;
using OnlineActionRpg.Client.Combat;
using OnlineActionRpg.Client.Loading;
using UnityEngine;

namespace OnlineActionRpg.Client.Battle
{
    // 远端玩家的容器。
    //
    // 不继承 MonoBehaviour：它只是 BattleBootstrap 持有的一张表，
    // 生成与销毁分别用 Object.Instantiate / Object.Destroy，不需要自己成为场景对象。
    // 这样零场景改动，也不会在 Hierarchy 里多一个空节点。
    //
    // 05-1 只负责"按服务端下发的出生点把人摆出来"。
    // 05-3 的插值会按 playerId 在这里查对象并移动它，所以键用 playerId 而不是对象名。
    public sealed class RemotePlayerRegistry
    {
        private readonly Dictionary<string, GameObject> _byPlayerId =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

        public int Count => _byPlayerId.Count;

        // 为名单中除本地玩家外的每个人生成一个远端对象，已存在的不重复生成。
        // 返回本次新建的数量。
        public int SpawnMissing(
            BattlePlayerSpawnInfo[] players,
            string localPlayerId,
            GameObject prefab)
        {
            if (players == null || prefab == null)
            {
                return 0;
            }

            int spawned = 0;

            for (int i = 0; i < players.Length; i++)
            {
                BattlePlayerSpawnInfo spawn = players[i];

                if (string.IsNullOrWhiteSpace(spawn.PlayerId) ||
                    string.Equals(spawn.PlayerId, localPlayerId, StringComparison.OrdinalIgnoreCase) ||
                    _byPlayerId.ContainsKey(spawn.PlayerId))
                {
                    continue;
                }

                GameObject instance = UnityEngine.Object.Instantiate(prefab, spawn.Position, spawn.Rotation);
                instance.name = $"RemotePlayer_{spawn.Nickname}";

                //禁用掉本地玩家专用组件，否则远端玩家会跟着本地玩家一起走。
                DisableLocalOnlyComponents(instance);

                _byPlayerId[spawn.PlayerId] = instance;
                spawned++;
            }

            return spawned;
        }

        // 查找远端玩家对象，找不到返回 null。
        public GameObject Find(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                return null;
            }

            return _byPlayerId.TryGetValue(playerId, out GameObject instance) ? instance : null;
        }

        public void Clear()
        {
            // 销毁所有远端玩家对象实例。
            foreach (GameObject instance in _byPlayerId.Values)
            {
                if (instance != null)
                {
                    UnityEngine.Object.Destroy(instance);
                }
            }

            _byPlayerId.Clear();
        }

        // 远端玩家只保留"站在那里"所需的组件。
        //
        // 下面这些留着会出事：
        // - PlayerInputReader：会读本机输入，远端角色跟着本地玩家一起走
        // - PlayerTargetLock：中键按下时会同时改变两个玩家的锁定目标
        // - AttackHoming / LockOnMarker：会凭空出现锁定标记与吸附
        // - CombatDebugHud：屏幕上出现两套 HUD
        //
        // 保留 LocalPlayerController 与 CharacterAnimatorDriver：
        // 前者负责重力落地与待机朝向，否则角色会停在出生高度浮空；
        // 后者让角色保持 Idle，而不是 T-pose。
        private static void DisableLocalOnlyComponents(GameObject instance)
        {
            Disable<PlayerInputReader>(instance);
            Disable<PlayerTargetLock>(instance);
            Disable<AttackHoming>(instance);
            Disable<LockOnMarker>(instance);
            Disable<CombatDebugHud>(instance);
        }

        private static void Disable<T>(GameObject instance) where T : Behaviour
        {
            T component = instance.GetComponent<T>();

            if (component != null)
            {
                component.enabled = false;
            }
        }
    }
}
