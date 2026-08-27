namespace OnlineRpgServer.Account;

// 服务端内存中的账号记录。服务端重启前一直存在内存里。
// MVP 阶段先明文保存密码，只用于本地 Demo 验证；正式项目应使用加密哈希和持久化存储。

public sealed class AccountRecord
{
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required string PlayerId { get; init; }
    public required string Nickname { get; init; }
    public required long CreatedAt { get; init; }
}

// 登录成功后创建的会话记录，并发一个 token。
// token 是客户端后续访问大厅、房间、战斗接口时携带的临时通行证。
public sealed class PlayerSession
{
    public required string Token { get; init; }
    public required string Username { get; init; }
    public required string PlayerId { get; init; }
    public required string Nickname { get; init; }
    public required long CreatedAt { get; init; }

    public long LastActiveAt { get; set; }
}

//账号：注册后存在，直到服务端重启或删除
//会话：每次登录生成，后续可能过期、刷新、断线清理