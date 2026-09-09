namespace OnlineActionRpg.Client.Battle
{
    // BattleContext 保存当前战斗场景运行时上下文。
    public readonly struct BattleContext
    {
        public readonly string BattleId;
        public readonly string RoomId;
        public readonly string LocalPlayerId;
        public readonly string LocalNickname;
        public readonly string SceneName;
        public readonly bool IsEditorFallback;

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(BattleId) &&
            !string.IsNullOrWhiteSpace(RoomId) &&
            !string.IsNullOrWhiteSpace(LocalPlayerId);

        public BattleContext(
            string battleId,
            string roomId,
            string localPlayerId,
            string localNickname,
            string sceneName,
            bool isEditorFallback)
        {
            BattleId = battleId ?? string.Empty;
            RoomId = roomId ?? string.Empty;
            LocalPlayerId = localPlayerId ?? string.Empty;
            LocalNickname = localNickname ?? string.Empty;
            SceneName = sceneName ?? string.Empty;
            IsEditorFallback = isEditorFallback;
        }

        public override string ToString()
        {
            return $"BattleId={BattleId}, RoomId={RoomId}, LocalPlayerId={LocalPlayerId}, " +
                   $"Nickname={LocalNickname}, Scene={SceneName}, EditorFallback={IsEditorFallback}";
        }
    }
}
