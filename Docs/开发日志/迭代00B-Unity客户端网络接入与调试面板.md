# 迭代00B：Unity 客户端网络接入与调试面板

> 开始日期：2026-08-26  
> 当前阶段：Unity Play Mode 通信链路已手动验证通过  
> 迭代性质：补完迭代 00 的客户端侧验收，不提前进入登录业务  
> 本迭代目标：在 Unity Editor 中完成 WebSocket 连接、Ping / Pong、RTT 显示、协议日志和断线提示。

---

## 1. 为什么先做这个迭代

虽然服务端 `PingReq / PingRes` 已通过 PowerShell smoke test 验证，但这只能证明服务端和协议链路可用，还不能证明 Unity 客户端侧已经具备稳定的网络入口。

如果直接进入账号登录，会同时引入 UI 输入、账号状态、错误码、Token、页面跳转和网络连接问题。这样一旦失败，很难判断问题到底出在连接层、协议层、业务层还是 UI 层。

所以本迭代先做一个很小但很关键的客户端调试面板：

```text
Unity Button: Connect
  -> WebSocket 握手
  -> 显示 Connected

Unity Button: Ping
  -> 发送 PingReq
  -> 接收 PingRes
  -> 显示 RTT / 最近协议包

停止服务端
  -> 客户端显示 Disconnected / Error
```

完成后再进入 `迭代01：账号登录闭环`，登录就可以复用这条已经验证过的网络链路。

## 2. 本迭代不做什么

本阶段只验证 Unity 网络入口，不提前实现：

- 注册 / 登录 UI；
- Token 保存；
- 大厅 / 房间；
- 战斗同步；
- Addressables 加载；
- UDP / KCP；
- MessagePack / Protobuf；
- 客户端预测或回滚。

这些内容后续都会做，但不能把“计划做”写成“已经完成”。

## 3. 建议目录结构

在 Unity 工程下创建项目自己的代码目录，避免脚本散落在默认示例目录中：

```text
Client/UnityProject/Assets/Project/Scripts/
  Network/
    INetworkTransport.cs
    WebSocketTransport.cs
    NetworkClient.cs
    ProtocolEnvelope.cs
    NetworkMessageIds.cs

  UI/
    NetworkDebugPanel.cs
```

建议先不要引入复杂框架。第一版目标是“能看懂、能调试、能截图”，不是把网络库一次性设计到最终形态。

## 4. 推荐代码职责划分

| 脚本 | 职责 | 为什么这样拆 |
|---|---|---|
| `INetworkTransport` | 抽象连接、发送、关闭、接收事件 | 后续可以从 WebSocket 换成 KCP / UDP，而业务层少改 |
| `WebSocketTransport` | 只负责 Unity 端 WebSocket 收发 | 隔离底层 API，避免 UI 直接操作 socket |
| `ProtocolEnvelope` | 对齐服务端 JSON 信封字段 | 保证客户端和服务端协议结构一致 |
| `NetworkMessageIds` | 管理 `PingReq = 9001` 等编号 | 避免魔法数字散落在 UI 和业务代码里 |
| `NetworkClient` | 构造请求、计算 RTT、派发响应 | 放置请求响应匹配逻辑，后续登录复用 |
| `NetworkDebugPanel` | 绑定按钮和文本显示 | UI 只表达用户操作，不承载底层通信细节 |

核心边界：

- UI 不直接持有 `ClientWebSocket`。
- `WebSocketTransport` 不理解登录、房间、战斗。
- `NetworkClient` 可以理解 `PingReq / PingRes`，但暂时不处理账号业务。
- 后续登录模块只调用 `NetworkClient.SendRequest(...)`，不重写连接代码。

## 5. 实现步骤建议

### Step 1：创建调试场景或调试面板

可以先使用当前 `SampleScene`，创建一个简单 Canvas：

- `InputField` 或固定文本：服务器地址 `ws://localhost:5050/ws`；
- `Button`：Connect；
- `Button`：Disconnect；
- `Button`：Ping；
- `Text`：连接状态；
- `Text`：RTT；
- `Text`：最近发送包；
- `Text`：最近接收包；
- `Text`：错误信息。

第一版 UI 只要清楚，不追求美术表现。

### Step 2：实现协议数据结构

先让 Unity 端 `ProtocolEnvelope` 对齐服务端字段：

```text
msgId
type
requestId
token
clientTime
serverTime
code
message
payload
```

MVP 可以先把 `payload` 简化为 Ping 专用结构，避免为了一个 Ping 引入复杂泛型协议系统。

### Step 3：实现 WebSocketTransport

关键点：

- `ConnectAsync(string url)`：连接服务端；
- `SendTextAsync(string json)`：发送 UTF-8 JSON 文本；
- `ReceiveLoopAsync()`：循环接收服务端消息；
- `CloseAsync()`：主动关闭连接；
- 出错时把错误事件抛给上层。

注意事项：

- Unity Editor / Windows Demo 可以优先使用 `System.Net.WebSockets.ClientWebSocket`。
- 后续如果做 WebGL，WebSocket API 会不同，本项目当前不以 WebGL 为目标。
- 接收循环必须能停止，建议用 `CancellationTokenSource` 控制生命周期。

### Step 4：实现 NetworkClient

`NetworkClient` 是业务层未来会使用的入口。本阶段先实现：

- `Connect(url)`；
- `Disconnect()`；
- `SendPing()`；
- 收到 `PingRes` 后计算 RTT；
- 保存最近发送 JSON；
- 保存最近接收 JSON；
- 对外暴露状态变化事件。

RTT 计算方式：

```text
rttMs = receiveLocalTimeMs - sendLocalTimeMs
```

`PingRes.payload.clientTime` 用于确认响应对应的是哪次请求；`requestId` 用于后续通用请求响应匹配。

### Step 5：绑定 NetworkDebugPanel

`NetworkDebugPanel` 只做三件事：

1. 监听按钮点击；
2. 调用 `NetworkClient`；
3. 把状态、RTT、协议日志显示到 UI。

不要让 UI 直接拼 JSON，也不要让 UI 直接处理 socket 收包。

### Step 6：手动验证

先启动服务端：

```powershell
dotnet run --project Server\OnlineRpgServer\OnlineRpgServer.csproj
```

再运行服务端 smoke test，确认服务端链路仍然正常：

```powershell
powershell.exe -ExecutionPolicy Bypass -File Tools\SmokeTests\Test-ServerPing.ps1
```

最后打开 Unity Editor：

1. 进入 Play Mode；
2. 点击 Connect；
3. 点击 Ping；
4. 检查 RTT 与最近协议包；
5. 停掉服务端；
6. 检查客户端是否显示断线或错误。

## 6. 本迭代验收标准

- Unity Editor 中可以连接 `ws://localhost:5050/ws`。
- 点击 Ping 后，服务端控制台能看到 `PingReq`。
- Unity 面板能显示 `PingRes`。
- Unity 面板能显示 RTT。
- Unity 面板能显示最近发送包和最近接收包。
- 停止服务端后，Unity 面板能显示断线或错误状态。
- 代码中 UI、NetworkClient、Transport 职责清晰，没有把 socket 逻辑写进按钮脚本。

## 7. 验证证据清单

本迭代完成后建议保留：

- Unity 面板连接成功截图；
- Unity 面板 Ping 成功截图；
- 服务端控制台收到 `PingReq`、发送 `PingRes` 的日志截图；
- 停止服务端后的断线提示截图；
- 如出现问题，补充到 `Docs/Bug记录簿.md`。

## 8. 常见风险和排查顺序

### 8.1 连接失败

优先排查：

1. 服务端是否启动；
2. 端口是否是 `5050`；
3. 地址是否是 `ws://localhost:5050/ws`，不是 `http://localhost:5050/ws`；
4. PowerShell smoke test 是否还能通过；
5. Unity 控制台是否有异常堆栈。

### 8.2 Ping 无响应

优先排查：

1. Unity 发出的 JSON 是否包含 `type = PingReq`；
2. `requestId` 是否非空；
3. `payload.clientTime` 是否存在；
4. 服务端日志是否收到消息；
5. 服务端是否返回 `ErrorRes`。

### 8.3 UI 不刷新

网络接收可能发生在异步回调中。第一版可以把收到的状态缓存到字段里，在 `Update()` 中刷新 UI，避免从非主线程直接操作 Unity UI。

## 9. 本迭代的面试表达边界

完成后可以表达为：

> 在 Unity 客户端实现了最小网络调试面板，通过 WebSocket + JSON 接入本地 .NET 服务端，完成连接、Ping / Pong、RTT 显示、协议日志和断线提示，为后续登录、大厅和战斗同步提供可复用网络入口。

不要表达为：

- 已完成账号系统；
- 已完成大厅房间；
- 已完成战斗同步；
- 已实现 KCP / UDP；
- 已完成商业级网络框架。

## 10. 下一步衔接

本迭代完成后，再进入 `迭代01：账号登录闭环`。

登录迭代会复用：

- `INetworkTransport`；
- `WebSocketTransport`；
- `NetworkClient`；
- `ProtocolEnvelope`；
- 协议日志显示能力；
- 错误状态显示能力。

届时只新增账号协议、账号服务、登录 UI 和 Token 会话管理，不重复造连接层。

## 11. 2026-08-27 开发记录

### 已完成

- 在 Unity 工程中创建项目脚本目录：`Client/UnityProject/Assets/Project/Scripts/`。
- 创建网络传输抽象：`INetworkTransport`。
- 创建 WebSocket 传输实现：`WebSocketTransport`。
- 创建协议消息编号：`NetworkMessageIds`。
- 创建 Unity 端协议结构：`ProtocolEnvelope`、`PingRequestEnvelope`、`PingResponseEnvelope`、`ErrorResponseEnvelope`。
- 创建网络业务入口：`NetworkClient`，负责连接、断开、发送 Ping、接收响应、计算 RTT 和缓存调试快照。
- 创建调试面板脚本：`NetworkDebugPanel`，负责按钮绑定和 UI 文本刷新。

### 已验证

- Unity Editor 日志显示脚本编译成功：`Tundra build success`。
- 服务端项目 `dotnet build Server/OnlineRpgServer/OnlineRpgServer.csproj` 构建成功，0 警告，0 错误。
- 作者在 Unity Play Mode 中完成客户端连接与 Ping / Pong 手动验证，链路可用。

### 手动验收结果

- Unity Play Mode 中点击 Connect 后可连接 `ws://localhost:5050/ws`。
- Unity Play Mode 中点击 Ping 后可收到 `PingRes` 并显示 RTT。
- 调试面板可显示最近发送包和最近接收包。

### 待补证据

- Unity 面板连接成功截图。
- Unity 面板 Ping 成功截图。
- 服务端控制台收发 `PingReq / PingRes` 日志截图。
- 停止服务端后的断线提示截图。

### 本次小修

- 将客户端 C# 文件保存为 UTF-8 编码，避免中文注释在不同工具中显示乱码。
- 给 `NetworkDebugPanel` 的按钮监听增加空引用保护，避免 Inspector 漏拖字段时直接在 `Awake()` 报错。
