# Online Action RPG Tech Demo

面向游戏客户端开发实习求职的联机动作 RPG 客户端技术纵切 Demo。

这个项目是第二个作品集项目，用来补齐《背包幸存者》之外的商业客户端技术链路：账号登录、大厅房间、异步资源加载、联机动作战斗、网络同步、调试工具、性能记录和演示交付。

## 当前状态

项目已完成 v0.2 阶段：基础通信、账号登录、大厅房间、Ready / StartBattle 入口已经跑通，并完成服务端 smoke test 与 Unity 双客户端手动验证。当前进入 v0.3：异步资源与场景流启动阶段。

已完成：

- 项目立项、架构、协议和迭代规划文档。
- Unity 客户端工程目录统一为 `Client/UnityProject/`。
- C# / .NET 服务端最小工程：`Server/OnlineRpgServer/`。
- Unity 客户端 WebSocket 连接、Ping / Pong、RTT 与协议日志调试面板。
- 注册 / 登录协议、Token 会话保存和登录后进入大厅入口。
- 大厅 / 房间链路：进入大厅、创建房间、加入房间、离开房间和 `RoomStateNtf` 广播。
- Ready / StartBattle 链路：成员准备、房主开始、服务端校验后进入 `Loading` 状态。
- 房主断线清理：单人房销毁，多人房转移房主并广播剩余房间状态。
- PowerShell smoke tests：Ping、账号、房间、断线清理、Ready / StartBattle。
- Unity 双客户端手动验证：创建房间、加入房间、Ready、StartBattle、进入 Loading 入口。
- Git 基线配置，包括 Unity / .NET `.gitignore` 和 `.gitattributes`。
- 工程巡检报告与 Bug / 性能记录模板。

未完成：

- Addressables 初始化、Loading UI、异步场景加载和资源生命周期管理。
- 战斗场景、角色加载、动作战斗和网络同步。
- Windows Build、性能记录和演示视频。

## 项目目标

MVP 只追求一条小而完整、可解释、可演示的商业客户端流程：

```text
启动客户端
  -> 注册 / 登录
  -> 进入大厅
  -> 创建或加入房间
  -> 准备并开始
  -> 异步加载训练场
  -> 2 个客户端进入同一战斗场景
  -> 同步移动、朝向、普攻、技能、受击、血量
  -> 结算
  -> 返回大厅
```

第一版不追求大世界、复杂 Boss、完整养成、商业级账号安全、复杂预测回滚或线上部署。优先保证链路闭环、问题可观察、证据可复盘。

## 技术选型

| 方向 | 当前选择 | 说明 |
|---|---|---|
| 客户端 | Unity `6000.3.20f1` + C# | 已创建 URP 客户端工程 |
| 渲染管线 | URP | 适合轻量动作 Demo 和性能验证 |
| 输入 | Unity Input System | 已在客户端包清单中启用 |
| 服务端 | C# / .NET `net9.0` | 已创建最小 WebSocket 服务端 |
| 网络 | WebSocket | 当前用于登录、大厅、房间等低频可靠控制消息 |
| 协议 | JSON | 已用于 Ping、账号、大厅、房间、Ready / StartBattle |
| 资源 | Addressables，待接入 | 用于展示异步加载和资源所有权 |

## 目录结构

```text
Online Action RPG Tech Demo/
  Client/
    UnityProject/              # Unity 客户端工程
  Server/
    OnlineRpgServer/           # C# / .NET 服务端工程
  Docs/
    项目立项书.md
    系统架构设计.md
    协议设计.md
    迭代模块规划.md
    资产来源与许可.md
    工程巡检报告-2026-08-13.md
    Bug记录簿.md
    性能验证记录.md
    开发日志/
  Tools/
    SmokeTests/                # 服务端自动化冒烟测试脚本
    ProtocolGenerator/         # 协议生成工具预留
    BuildScripts/              # 构建脚本预留
  Builds/                      # 本地构建输出，不提交实际 Build 包
  README.md
```

## 打开客户端工程

使用 Unity Hub 打开：

```text
Client/UnityProject/
```

推荐 Unity Editor 版本：

```text
6000.3.20f1
```

当前客户端已接入最小登录、大厅、房间和 Ready / StartBattle UI。进入 `Loading` 后暂不切换场景，异步加载流程放在 v0.3 实现。

## 运行状态

当前 v0.2 链路已验证：客户端连接服务端后可注册 / 登录、进入大厅、创建或加入房间、切换 Ready 状态，并由房主开始进入 `Loading` 入口。

启动服务端：

```powershell
dotnet run --project Server\OnlineRpgServer\OnlineRpgServer.csproj
```

服务端启动后可执行 smoke tests：

```powershell
Tools\SmokeTests\Test-ServerPing.ps1
Tools\SmokeTests\Test-ServerAccount.ps1
Tools\SmokeTests\Test-ServerRoom.ps1
Tools\SmokeTests\Test-ServerRoomDisconnect.ps1
Tools\SmokeTests\Test-ServerRoomReadyStart.ps1
```

Ready / StartBattle 专项测试已验证返回：

```json
{"ok":true,"finalState":"Loading","notificationCount":7}
```

v0.3 待实现目标：

- Loading 协议与 Loading UI。
- Addressables 初始化与异步加载进度显示。
- 从房间 `Loading` 状态进入战斗场景加载流程。
- 加载失败提示、重试或返回大厅。

## 核心文档

- [项目立项书](Docs/项目立项书.md)
- [系统架构设计](Docs/系统架构设计.md)
- [协议设计](Docs/协议设计.md)
- [迭代模块规划](Docs/迭代模块规划.md)
- [资产来源与许可](Docs/资产来源与许可.md)
- [工程巡检报告](Docs/工程巡检报告-2026-08-13.md)
- [Bug 记录簿](Docs/Bug记录簿.md)
- [性能验证记录](Docs/性能验证记录.md)

## 求职展示原则

- 只把已经实现并验证过的功能写成完成项。
- 对未完成能力使用“计划接入”“待实现”“后续扩展”等表述。
- 每个迭代都保留截图、日志、Bug 记录、Profiler 或录屏证据。
- 面试讲述重点放在设计取舍、模块边界、验证方法和问题复盘，而不是堆技术名词。

## 当前推荐下一步

进入 v0.3：先设计 Loading 协议和最小 LoadingPanel，再接入 Addressables 初始化与战斗场景异步加载。不要把角色加载、技能特效、战斗实例和网络同步一次性混入同一小步。
