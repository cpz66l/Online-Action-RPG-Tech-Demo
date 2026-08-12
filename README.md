# Online Action RPG Tech Demo

面向游戏客户端开发实习求职的联机动作 RPG 客户端技术纵切 Demo。

这个项目是第二个作品集项目，用来补齐《背包幸存者》之外的商业客户端技术链路：账号登录、大厅房间、异步资源加载、联机动作战斗、网络同步、调试工具、性能记录和演示交付。

## 当前状态

项目处于工程启动早期：文档骨架和 Unity 客户端工程已建立，服务端工程仍是占位目录，联网功能尚未实现。

已完成：

- 项目立项、架构、协议和迭代规划文档。
- Unity 客户端工程目录统一为 `Client/UnityProject/`。
- Git 基线配置，包括 Unity / .NET `.gitignore` 和 `.gitattributes`。
- 工程巡检报告与 Bug / 性能记录模板。

未完成：

- C# / .NET 服务端工程。
- WebSocket Ping / Pong 通信验证。
- 登录、大厅、房间、加载、战斗和同步功能。
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
| 服务端 | C# / .NET Console，待创建 | MVP 先降低服务端复杂度 |
| 网络 | WebSocket，待实现 | 第一版优先可读、可调试、可演示 |
| 协议 | JSON，待实现 | 后续可扩展 MessagePack / Protobuf |
| 资源 | Addressables，待接入 | 用于展示异步加载和资源所有权 |

## 目录结构

```text
Online Action RPG Tech Demo/
  Client/
    UnityProject/              # Unity 客户端工程
  Server/
    OnlineRpgServer/           # C# / .NET 服务端工程，占位中
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

当前客户端仍是 Unity 初始工程，还没有接入项目自己的启动场景、网络模块或 UI 流程。

## 运行状态

当前还没有完整运行链路。

下一步迭代 0 的最小验收目标是：

- 创建 `Server/OnlineRpgServer/` 的 .NET 服务端工程。
- 实现 WebSocket 连接。
- 定义 `PingReq / PingRes`。
- 客户端能连接本地服务端并显示 RTT。
- 断开服务端后客户端能显示断线状态。

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

进入迭代 0：项目骨架与通信验证。先跑通本地服务端与 Unity 客户端的 Ping / Pong，再开始登录和大厅功能。
