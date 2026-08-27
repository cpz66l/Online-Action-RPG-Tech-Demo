namespace OnlineRpgServer.Account;

// AccountService 是服务端账号模块的核心业务类。
// 它不关心 WebSocket，也不关心 JSON，只处理注册、登录和 token 会话。
public sealed class AccountService
{
    //状态码
    private const int Ok = 0;                               //有效
    private const int InvalidArgument = 1001;               //无效参数
    private const int AccountAlreadyExists = 2001;          //账号存在
    private const int InvalidUsernameOrPassword = 2002;     //账号或者密码错误

    private readonly object _gate = new();

    //账号表：通过用户名找到账号档案。
    private readonly Dictionary<string, AccountRecord> _accountsByUsername = new(StringComparer.OrdinalIgnoreCase);//大小写不敏感
    //在线会话表：通过 token 找到当前登录玩家。
    private readonly Dictionary<string, PlayerSession> _sessionsByToken = new();

    private int _nextPlayerNumber = 10001;

    //注册
    public AccountRegisterResult Register(string? username, string? password, string? nickname)
    {
        username = Normalize(username);
        password = Normalize(password);
        nickname = Normalize(nickname);

        if (IsMissing(username) || IsMissing(password) || IsMissing(nickname))
        {
            return AccountRegisterResult.Fail(InvalidArgument, "Username, password and nickname are required.");
        }

        lock (_gate)
        {
            if (_accountsByUsername.ContainsKey(username))
            {
                return AccountRegisterResult.Fail(AccountAlreadyExists, "Account already exists.");
            }

            long now = UnixTimeMilliseconds();
            string playerId = CreatePlayerId();

            AccountRecord account = new()
            {
                Username = username,
                Password = password,
                PlayerId = playerId,
                Nickname = nickname,
                CreatedAt = now
            };

            _accountsByUsername.Add(username, account);

            return AccountRegisterResult.Ok(playerId, nickname);
        }
    }

    //登录
    public AccountLoginResult Login(string? username, string? password)
    {
        username = Normalize(username);
        password = Normalize(password);

        if (IsMissing(username) || IsMissing(password))
        {
            return AccountLoginResult.Fail(InvalidArgument, "Username and password are required.");
        }

        lock (_gate)
        {
            if (!_accountsByUsername.TryGetValue(username, out AccountRecord? account))
            {
                return AccountLoginResult.Fail(InvalidUsernameOrPassword, "Invalid username or password.");
            }

            if (account.Password != password)
            {
                return AccountLoginResult.Fail(InvalidUsernameOrPassword, "Invalid username or password.");
            }

            long now = UnixTimeMilliseconds();
            string token = CreateToken();

            //会话
            PlayerSession session = new()
            {
                Token = token,
                Username = account.Username,
                PlayerId = account.PlayerId,
                Nickname = account.Nickname,
                CreatedAt = now,
                LastActiveAt = now
            };

            _sessionsByToken[token] = session;//登录成功后，将玩家加入在线会话表

            return AccountLoginResult.Ok(token, account.PlayerId, account.Nickname);
        }
    }

    public PlayerSession? GetSession(string? token)
    {
        token = Normalize(token);

        if (IsMissing(token))
        {
            return null;
        }

        lock (_gate)
        {
            if (!_sessionsByToken.TryGetValue(token, out PlayerSession? session))
            {
                return null;
            }

            session.LastActiveAt = UnixTimeMilliseconds();
            return session;
        }
    }

    //小helper
    private string CreatePlayerId()
    {
        string playerId = $"p_{_nextPlayerNumber}";
        _nextPlayerNumber++;
        return playerId;
    }

    private static string CreateToken()
    {
        return $"token_{Guid.NewGuid():N}";
    }

    private static string Normalize(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static bool IsMissing(string value)
    {
        return string.IsNullOrWhiteSpace(value);
    }

    private static long UnixTimeMilliseconds()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}

//账号注册结果类
public sealed class AccountRegisterResult
{
    public bool Success { get; private init; }
    public int Code { get; private init; }
    public string Message { get; private init; } = string.Empty;
    public string PlayerId { get; private init; } = string.Empty;
    public string Nickname { get; private init; } = string.Empty;

    public static AccountRegisterResult Ok(string playerId, string nickname)
    {
        return new AccountRegisterResult
        {
            Success = true,
            Code = 0,
            Message = "OK",
            PlayerId = playerId,
            Nickname = nickname
        };
    }

    public static AccountRegisterResult Fail(int code, string message)
    {
        return new AccountRegisterResult
        {
            Success = false,
            Code = code,
            Message = message
        };
    }
}

//账号登录结果类
public sealed class AccountLoginResult
{
    public bool Success { get; private init; }
    public int Code { get; private init; }
    public string Message { get; private init; } = string.Empty;
    public string Token { get; private init; } = string.Empty;
    public string PlayerId { get; private init; } = string.Empty;
    public string Nickname { get; private init; } = string.Empty;

    //登陆成功给一个随机生成的唯一token
    public static AccountLoginResult Ok(string token, string playerId, string nickname)
    {
        return new AccountLoginResult
        {
            Success = true,
            Code = 0,
            Message = "OK",
            Token = token,
            PlayerId = playerId,
            Nickname = nickname
        };
    }

    public static AccountLoginResult Fail(int code, string message)
    {
        return new AccountLoginResult
        {
            Success = false,
            Code = code,
            Message = message
        };
    }
}