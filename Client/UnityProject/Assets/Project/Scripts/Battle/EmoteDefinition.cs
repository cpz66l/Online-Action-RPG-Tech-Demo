using System;
using UnityEngine;

namespace OnlineActionRpg.Client.Battle
{
    //表情定义数据结构。
    [Serializable]
    public sealed class EmoteDefinition
    {
        public string Id;
        public string DisplayName;
        public Sprite Icon;
        public string AnimatorStateName;
        public bool Loop = true;
        public float Duration = 0f;
        public bool CancelOnMove = true;
        public bool CancelOnCombatAction = true;
    }
}