param(
    [string]$Url = "ws://localhost:5050/ws",
    [string]$UsernamePrefix = "",
    [string]$Password = "123456"
)

# Smoke Test: verifies ReadyReq / StartBattleReq room gate rules.
# Start the server first: dotnet run --project Server\OnlineRpgServer\OnlineRpgServer.csproj
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($UsernamePrefix)) {
    $UsernamePrefix = "room_ready_start_" + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
}

$ct = [System.Threading.CancellationToken]::None
$sockets = @()
$scenarios = @()
$notifications = @()

function New-RequestId {
    return [Guid]::NewGuid().ToString("N")
}

function New-ConnectedSocket {
    $socket = New-Object System.Net.WebSockets.ClientWebSocket
    $connectTask = $socket.ConnectAsync([Uri]$script:Url, $script:ct)
    $connectTask.Wait()
    $script:sockets += $socket
    return $socket
}

function Send-TextMessage {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket,
        [string]$Message
    )

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Message)
    $segment = New-Object 'System.ArraySegment[byte]' -ArgumentList @(,$bytes)
    $sendTask = $Socket.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $script:ct)
    $sendTask.Wait()
}

function Receive-TextMessage {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket,
        [int]$TimeoutMilliseconds = 5000
    )

    $buffer = New-Object byte[] 8192
    $stream = New-Object System.IO.MemoryStream
    $receiveCts = [System.Threading.CancellationTokenSource]::CreateLinkedTokenSource($script:ct)

    if ($TimeoutMilliseconds -gt 0) {
        $receiveCts.CancelAfter($TimeoutMilliseconds)
    }

    try {
        while ($true) {
            $segment = New-Object 'System.ArraySegment[byte]' -ArgumentList @(,$buffer)
            $receiveTask = $Socket.ReceiveAsync($segment, $receiveCts.Token)

            try {
                $receiveTask.Wait()
            }
            catch [System.AggregateException] {
                $inner = $_.Exception.InnerException

                if ($inner -is [System.OperationCanceledException]) {
                    throw "Timed out waiting for WebSocket text message."
                }

                throw
            }

            $result = $receiveTask.Result

            if ($result.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
                throw "Server closed the WebSocket connection before sending a response."
            }

            if ($result.MessageType -ne [System.Net.WebSockets.WebSocketMessageType]::Text) {
                throw "Expected WebSocket text message, got '$($result.MessageType)'."
            }

            $stream.Write($buffer, 0, $result.Count)

            if ($result.EndOfMessage) {
                return [System.Text.Encoding]::UTF8.GetString($stream.ToArray())
            }
        }
    }
    finally {
        $stream.Dispose()
        $receiveCts.Dispose()
    }
}

function Add-Scenario {
    param(
        [string]$Name,
        [object]$Exchange
    )

    $script:scenarios += [pscustomobject]@{
        name = $Name
        responseType = $Exchange.Response.type
        code = $Exchange.Response.code
    }
}

function Get-JsonArrayCount {
    param([object]$Value)

    if ($null -eq $Value) {
        return 0
    }

    return @($Value).Count
}

function Add-RoomStateNotificationRecord {
    param(
        [object]$Message,
        [string]$Json,
        [string]$Scenario
    )

    $room = $Message.payload.room

    $script:notifications += [pscustomobject]@{
        scenario = $Scenario
        roomId = $room.roomId
        ownerPlayerId = $room.ownerPlayerId
        state = $room.state
        playerCount = Get-JsonArrayCount -Value $room.players
        json = $Json
    }
}

function Invoke-ServerRequest {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket,
        [int]$MsgId,
        [string]$Type,
        [hashtable]$Payload,
        [string]$Token = ""
    )

    $requestId = New-RequestId
    $clientTime = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $requestObject = @{
        msgId = $MsgId
        type = $Type
        requestId = $requestId
        clientTime = $clientTime
        payload = $Payload
    }

    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        $requestObject.token = $Token
    }

    $requestJson = $requestObject | ConvertTo-Json -Compress -Depth 12
    Send-TextMessage -Socket $Socket -Message $requestJson

    while ($true) {
        $responseJson = Receive-TextMessage -Socket $Socket
        $response = $responseJson | ConvertFrom-Json

        if ($response.requestId -eq $requestId) {
            break
        }

        if ($response.type -eq "RoomStateNtf") {
            Add-RoomStateNotificationRecord -Message $response -Json $responseJson -Scenario "while waiting for $Type"
            continue
        }

        throw "[$Type] Expected response requestId '$requestId', got '$($response.requestId)': $responseJson"
    }

    return [pscustomobject]@{
        RequestId = $requestId
        RequestJson = $requestJson
        ResponseJson = $responseJson
        Response = $response
    }
}

function Assert-ServerResponse {
    param(
        [object]$Exchange,
        [string]$Scenario,
        [string]$ExpectedType,
        [int]$ExpectedCode
    )

    $response = $Exchange.Response

    if ($response.type -ne $ExpectedType) {
        throw "[$Scenario] Expected type '$ExpectedType', got '$($response.type)': $($Exchange.ResponseJson)"
    }

    if ($response.code -ne $ExpectedCode) {
        throw "[$Scenario] Expected code '$ExpectedCode', got '$($response.code)': $($Exchange.ResponseJson)"
    }

    if ($response.requestId -ne $Exchange.RequestId) {
        throw "[$Scenario] Expected requestId '$($Exchange.RequestId)', got '$($response.requestId)'."
    }
}

function Assert-NotEmpty {
    param(
        [string]$Value,
        [string]$Message
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw $Message
    }
}

function Get-RoomPlayer {
    param(
        [object]$Room,
        [string]$PlayerId
    )

    foreach ($player in @($Room.players)) {
        if ($null -ne $player -and $player.playerId -eq $PlayerId) {
            return $player
        }
    }

    return $null
}

function Assert-RoomHasPlayer {
    param(
        [object]$Room,
        [string]$PlayerId,
        [string]$Scenario
    )

    $player = Get-RoomPlayer -Room $Room -PlayerId $PlayerId

    if ($null -eq $player) {
        throw "[$Scenario] Expected room '$($Room.roomId)' to contain player '$PlayerId'."
    }
}

function Assert-PlayerReadyState {
    param(
        [object]$Room,
        [string]$PlayerId,
        [bool]$ExpectedReady,
        [string]$Scenario
    )

    $player = Get-RoomPlayer -Room $Room -PlayerId $PlayerId

    if ($null -eq $player) {
        throw "[$Scenario] Expected room '$($Room.roomId)' to contain player '$PlayerId'."
    }

    if ([bool]$player.isReady -ne $ExpectedReady) {
        throw "[$Scenario] Expected player '$PlayerId' ready=$ExpectedReady, got '$($player.isReady)'."
    }
}

function Assert-RoomSnapshot {
    param(
        [object]$Room,
        [string]$Scenario,
        [string]$RoomId,
        [string]$ExpectedOwnerPlayerId,
        [string]$ExpectedState,
        [int]$ExpectedPlayerCount,
        [string[]]$ExpectedPlayerIds = @()
    )

    if ($Room.roomId -ne $RoomId) {
        throw "[$Scenario] Expected roomId '$RoomId', got '$($Room.roomId)'."
    }

    if ($Room.ownerPlayerId -ne $ExpectedOwnerPlayerId) {
        throw "[$Scenario] Expected owner '$ExpectedOwnerPlayerId', got '$($Room.ownerPlayerId)'."
    }

    if ($Room.state -ne $ExpectedState) {
        throw "[$Scenario] Expected state '$ExpectedState', got '$($Room.state)'."
    }

    if ((Get-JsonArrayCount -Value $Room.players) -ne $ExpectedPlayerCount) {
        throw "[$Scenario] Expected $ExpectedPlayerCount players in room."
    }

    foreach ($playerId in $ExpectedPlayerIds) {
        Assert-RoomHasPlayer -Room $Room -PlayerId $playerId -Scenario $Scenario
    }
}

function Receive-RoomStateNotification {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket,
        [string]$Scenario,
        [string]$RoomId,
        [string]$ExpectedOwnerPlayerId,
        [string]$ExpectedState,
        [int]$ExpectedPlayerCount,
        [string[]]$ExpectedPlayerIds = @()
    )

    $messageJson = Receive-TextMessage -Socket $Socket -TimeoutMilliseconds 3000
    $message = $messageJson | ConvertFrom-Json

    if ($message.type -ne "RoomStateNtf") {
        throw "[$Scenario] Expected RoomStateNtf, got '$($message.type)': $messageJson"
    }

    Add-RoomStateNotificationRecord -Message $message -Json $messageJson -Scenario $Scenario
    $room = $message.payload.room

    Assert-RoomSnapshot `
        -Room $room `
        -Scenario $Scenario `
        -RoomId $RoomId `
        -ExpectedOwnerPlayerId $ExpectedOwnerPlayerId `
        -ExpectedState $ExpectedState `
        -ExpectedPlayerCount $ExpectedPlayerCount `
        -ExpectedPlayerIds $ExpectedPlayerIds

    return $message
}

function Register-And-Login {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket,
        [string]$Username,
        [string]$Nickname
    )

    $registerPayload = @{
        username = $Username
        password = $script:Password
        nickname = $Nickname
    }

    $register = Invoke-ServerRequest -Socket $Socket -MsgId 1001 -Type "RegisterReq" -Payload $registerPayload
    Assert-ServerResponse -Exchange $register -Scenario "register $Nickname" -ExpectedType "RegisterRes" -ExpectedCode 0
    Add-Scenario -Name "register $Nickname" -Exchange $register

    $loginPayload = @{
        username = $Username
        password = $script:Password
    }

    $login = Invoke-ServerRequest -Socket $Socket -MsgId 1003 -Type "LoginReq" -Payload $loginPayload
    Assert-ServerResponse -Exchange $login -Scenario "login $Nickname" -ExpectedType "LoginRes" -ExpectedCode 0
    Add-Scenario -Name "login $Nickname" -Exchange $login

    $token = [string]$login.Response.payload.token
    $playerId = [string]$login.Response.payload.playerId

    Assert-NotEmpty -Value $token -Message "[login $Nickname] Expected non-empty token: $($login.ResponseJson)"
    Assert-NotEmpty -Value $playerId -Message "[login $Nickname] Expected non-empty playerId: $($login.ResponseJson)"

    return [pscustomobject]@{
        Username = $Username
        Nickname = $Nickname
        PlayerId = $playerId
        Token = $token
    }
}

try {
    $ownerSocket = New-ConnectedSocket
    $memberSocket = New-ConnectedSocket

    $owner = Register-And-Login -Socket $ownerSocket -Username ($UsernamePrefix + "_owner") -Nickname "ReadyOwner"
    $member = Register-And-Login -Socket $memberSocket -Username ($UsernamePrefix + "_member") -Nickname "ReadyMember"

    $createRoom = Invoke-ServerRequest `
        -Socket $ownerSocket `
        -MsgId 3101 `
        -Type "CreateRoomReq" `
        -Payload @{ roomName = "Ready Start Room"; maxPlayers = 2 } `
        -Token $owner.Token

    Assert-ServerResponse -Exchange $createRoom -Scenario "owner create room" -ExpectedType "CreateRoomRes" -ExpectedCode 0
    Add-Scenario -Name "owner create room" -Exchange $createRoom

    $roomId = [string]$createRoom.Response.payload.room.roomId
    Assert-NotEmpty -Value $roomId -Message "[owner create room] Expected non-empty roomId: $($createRoom.ResponseJson)"

    Assert-RoomSnapshot `
        -Room $createRoom.Response.payload.room `
        -Scenario "owner create room" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Waiting" `
        -ExpectedPlayerCount 1 `
        -ExpectedPlayerIds @($owner.PlayerId)

    Assert-PlayerReadyState -Room $createRoom.Response.payload.room -PlayerId $owner.PlayerId -ExpectedReady $false -Scenario "owner create room"

    Receive-RoomStateNotification `
        -Socket $ownerSocket `
        -Scenario "create room notification" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Waiting" `
        -ExpectedPlayerCount 1 `
        -ExpectedPlayerIds @($owner.PlayerId) | Out-Null

    $startAlone = Invoke-ServerRequest `
        -Socket $ownerSocket `
        -MsgId 3109 `
        -Type "StartBattleReq" `
        -Payload @{ roomId = $roomId } `
        -Token $owner.Token

    Assert-ServerResponse -Exchange $startAlone -Scenario "owner start with one player" -ExpectedType "ErrorRes" -ExpectedCode 3003
    Add-Scenario -Name "owner start with one player" -Exchange $startAlone

    $joinRoom = Invoke-ServerRequest `
        -Socket $memberSocket `
        -MsgId 3103 `
        -Type "JoinRoomReq" `
        -Payload @{ roomId = $roomId } `
        -Token $member.Token

    Assert-ServerResponse -Exchange $joinRoom -Scenario "member join room" -ExpectedType "JoinRoomRes" -ExpectedCode 0
    Add-Scenario -Name "member join room" -Exchange $joinRoom

    Assert-RoomSnapshot `
        -Room $joinRoom.Response.payload.room `
        -Scenario "member join room" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Waiting" `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId)

    Assert-PlayerReadyState -Room $joinRoom.Response.payload.room -PlayerId $owner.PlayerId -ExpectedReady $false -Scenario "member join room"
    Assert-PlayerReadyState -Room $joinRoom.Response.payload.room -PlayerId $member.PlayerId -ExpectedReady $false -Scenario "member join room"

    Receive-RoomStateNotification `
        -Socket $ownerSocket `
        -Scenario "join notification to owner" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Waiting" `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId) | Out-Null

    Receive-RoomStateNotification `
        -Socket $memberSocket `
        -Scenario "join notification to member" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Waiting" `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId) | Out-Null

    $memberStart = Invoke-ServerRequest `
        -Socket $memberSocket `
        -MsgId 3109 `
        -Type "StartBattleReq" `
        -Payload @{ roomId = $roomId } `
        -Token $member.Token

    Assert-ServerResponse -Exchange $memberStart -Scenario "non-owner start battle" -ExpectedType "ErrorRes" -ExpectedCode 1003
    Add-Scenario -Name "non-owner start battle" -Exchange $memberStart

    $ownerStartBeforeReady = Invoke-ServerRequest `
        -Socket $ownerSocket `
        -MsgId 3109 `
        -Type "StartBattleReq" `
        -Payload @{ roomId = $roomId } `
        -Token $owner.Token

    Assert-ServerResponse -Exchange $ownerStartBeforeReady -Scenario "owner start before member ready" -ExpectedType "ErrorRes" -ExpectedCode 3003
    Add-Scenario -Name "owner start before member ready" -Exchange $ownerStartBeforeReady

    $memberReady = Invoke-ServerRequest `
        -Socket $memberSocket `
        -MsgId 3107 `
        -Type "ReadyReq" `
        -Payload @{ roomId = $roomId; isReady = $true } `
        -Token $member.Token

    Assert-ServerResponse -Exchange $memberReady -Scenario "member ready" -ExpectedType "ReadyRes" -ExpectedCode 0
    Add-Scenario -Name "member ready" -Exchange $memberReady

    Assert-RoomSnapshot `
        -Room $memberReady.Response.payload.room `
        -Scenario "member ready" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Waiting" `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId)

    Assert-PlayerReadyState -Room $memberReady.Response.payload.room -PlayerId $owner.PlayerId -ExpectedReady $false -Scenario "member ready"
    Assert-PlayerReadyState -Room $memberReady.Response.payload.room -PlayerId $member.PlayerId -ExpectedReady $true -Scenario "member ready"

    $ownerReadyNtf = Receive-RoomStateNotification `
        -Socket $ownerSocket `
        -Scenario "ready notification to owner" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Waiting" `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId)

    Assert-PlayerReadyState -Room $ownerReadyNtf.payload.room -PlayerId $member.PlayerId -ExpectedReady $true -Scenario "ready notification to owner"

    $memberReadyNtf = Receive-RoomStateNotification `
        -Socket $memberSocket `
        -Scenario "ready notification to member" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Waiting" `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId)

    Assert-PlayerReadyState -Room $memberReadyNtf.payload.room -PlayerId $member.PlayerId -ExpectedReady $true -Scenario "ready notification to member"

    $ownerStartAfterReady = Invoke-ServerRequest `
        -Socket $ownerSocket `
        -MsgId 3109 `
        -Type "StartBattleReq" `
        -Payload @{ roomId = $roomId } `
        -Token $owner.Token

    Assert-ServerResponse -Exchange $ownerStartAfterReady -Scenario "owner start after member ready" -ExpectedType "StartBattleRes" -ExpectedCode 0
    Add-Scenario -Name "owner start after member ready" -Exchange $ownerStartAfterReady

    Assert-RoomSnapshot `
        -Room $ownerStartAfterReady.Response.payload.room `
        -Scenario "owner start after member ready" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Loading" `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId)

    $ownerLoadingNtf = Receive-RoomStateNotification `
        -Socket $ownerSocket `
        -Scenario "loading notification to owner" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Loading" `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId)

    Assert-PlayerReadyState -Room $ownerLoadingNtf.payload.room -PlayerId $member.PlayerId -ExpectedReady $true -Scenario "loading notification to owner"

    $memberLoadingNtf = Receive-RoomStateNotification `
        -Socket $memberSocket `
        -Scenario "loading notification to member" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Loading" `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId)

    Assert-PlayerReadyState -Room $memberLoadingNtf.payload.room -PlayerId $member.PlayerId -ExpectedReady $true -Scenario "loading notification to member"

    $readyAfterLoading = Invoke-ServerRequest `
        -Socket $memberSocket `
        -MsgId 3107 `
        -Type "ReadyReq" `
        -Payload @{ roomId = $roomId; isReady = $false } `
        -Token $member.Token

    Assert-ServerResponse -Exchange $readyAfterLoading -Scenario "ready after loading" -ExpectedType "ErrorRes" -ExpectedCode 3003
    Add-Scenario -Name "ready after loading" -Exchange $readyAfterLoading

    $startAfterLoading = Invoke-ServerRequest `
        -Socket $ownerSocket `
        -MsgId 3109 `
        -Type "StartBattleReq" `
        -Payload @{ roomId = $roomId } `
        -Token $owner.Token

    Assert-ServerResponse -Exchange $startAfterLoading -Scenario "start after loading" -ExpectedType "ErrorRes" -ExpectedCode 3003
    Add-Scenario -Name "start after loading" -Exchange $startAfterLoading

    $expectedNotificationCount = 7
    if ((@($notifications).Count) -ne $expectedNotificationCount) {
        throw "Expected $expectedNotificationCount RoomStateNtf messages, got $(@($notifications).Count)."
    }

    $resultObject = [pscustomobject]@{
        ok = $true
        url = $Url
        usernamePrefix = $UsernamePrefix
        roomId = $roomId
        finalState = "Loading"
        notificationCount = @($notifications).Count
        scenarios = $scenarios
    }

    $resultObject | ConvertTo-Json -Compress -Depth 12
}
catch [System.AggregateException] {
    $inner = $_.Exception.InnerException
    $innerMessage = if ($inner -ne $null) { $inner.Message } else { $_.Exception.Message }
    throw "Room ready/start smoke test failed. Make sure the server is listening at $Url. Original error: $innerMessage"
}
catch {
    throw "Room ready/start smoke test failed. $($_.Exception.Message)"
}
finally {
    foreach ($socket in $sockets) {
        try {
            if ($socket.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
                $closeTask = $socket.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "room ready/start smoke test done", $ct)
                $closeTask.Wait()
            }
        }
        catch {
        }
        finally {
            $socket.Dispose()
        }
    }
}
