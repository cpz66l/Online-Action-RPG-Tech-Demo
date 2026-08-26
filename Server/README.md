# Server

C# / .NET 服务端工程目录。

当前工程位置：

```text
Server/OnlineRpgServer/
```

MVP 服务端职责：连接、账号、房间、战斗 Tick、协议广播和调试日志。

## 当前状态

已创建最小 C# / .NET WebSocket 服务端工程，用于迭代 0 的 Ping / Pong 通信验证。

当前功能：

- `GET /health`：服务健康检查。
- `WebSocket /ws`：协议入口。
- `PingReq -> PingRes`：调试协议，用于验证连接、JSON 包结构和 RTT。

## 运行服务端

在项目根目录执行：

```powershell
dotnet run --project Server\OnlineRpgServer\OnlineRpgServer.csproj
```

默认监听：

```text
http://localhost:5050
ws://localhost:5050/ws
```

## Smoke Test

服务端启动后，在项目根目录执行：

```powershell
Tools\SmokeTests\Test-ServerPing.ps1
```

预期返回类似：

```json
{"ok":true,"url":"ws://localhost:5050/ws","requestId":"smoke-test-001","responseType":"PingRes","code":0}
```
