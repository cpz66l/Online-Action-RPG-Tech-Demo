using OnlineRpgServer.Room;

namespace OnlineRpgServer.Protocol;

//消息派发，大部分情况需要返回 ResponseJson，
//少数情况不需要进行回包，如客户端广播加载进度通知时，
//少数需要广播房间状态或加载战斗场景通知时，
//才会返回 RoomStateToNotify 或 LoadBattleSceneToNotify。
public sealed record MessageDispatchResult(
    string? ResponseJson,
    RoomSnapshot? RoomStateToNotify,
    LoadBattleSceneDispatch? LoadBattleSceneToNotify,
    BattleStartDispatch? BattleStartToNotify);

//加载战斗场景通知的派发结果，包含房间快照和通知负载
public sealed record LoadBattleSceneDispatch(
    RoomSnapshot Room,
    LoadBattleSceneNotificationPayload Payload);

//开始战斗通知的派发结果，包含房间快照和通知负载
public sealed record BattleStartDispatch(
    RoomSnapshot Room,
    BattleStartNotificationPayload Payload);
