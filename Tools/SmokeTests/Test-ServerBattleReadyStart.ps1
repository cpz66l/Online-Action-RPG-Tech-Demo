param(
    [string]$Url = "ws://localhost:5050/ws",
    [string]$UsernamePrefix = "",
    [string]$Password = "123456"
)

# Smoke Test: verifies ClientBattleReadyReq gates BattleStartNtf until every room member is battle-ready.
# Start the server first: dotnet run --project Server\OnlineRpgServer\OnlineRpgServer.csproj
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($UsernamePrefix)) {
    $UsernamePrefix = "battle_ready_" + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
}

$ct = [System.Threading.CancellationToken]::None
$sockets = @()
$scenarios = @()
$roomNotifications = @()
$loadingNotifications = @()
$battleStartNotifications = @()

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

function Assert-NotEmpty {
    param(
        [string]$Value,
        [string]$Message
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw $Message
    }
}

function Add-RoomStateNotificationRecord {
    param(
        [object]$Message,
        [string]$Json,
        [string]$Scenario
    )

    $room = $Message.payload.room
    $script:roomNotifications += [pscustomobject]@{
        scenario = $Scenario
        roomId = $room.roomId
        ownerPlayerId = $room.ownerPlayerId
        state = $room.state
        playerCount = Get-JsonArrayCount -Value $room.players
        json = $Json
    }
}

function Add-LoadingNotificationRecord {
    param(
        [object]$Message,
        [string]$Json,
        [string]$Scenario
    )

    $payload = $Message.payload
    $script:loadingNotifications += [pscustomobject]@{
        scenario = $Scenario
        battleId = $payload.battleId
        roomId = $payload.roomId
        sceneKey = $payload.sceneKey
        requiredAssetCount = Get-JsonArrayCount -Value $payload.requiredAssets
        json = $Json
    }
}

function Add-BattleStartNotificationRecord {
    param(
        [object]$Message,
        [string]$Json,
        [string]$Scenario
    )

    $payload = $Message.payload
    $script:battleStartNotifications += [pscustomobject]@{
        scenario = $Scenario
        battleId = $payload.battleId
        roomId = $payload.roomId
        serverStartTime = $payload.serverStartTime
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

        if ($response.type -eq "LoadBattleSceneNtf") {
            Add-LoadingNotificationRecord -Message $response -Json $responseJson -Scenario "while waiting for $Type"
            continue
        }

        if ($response.type -eq "BattleStartNtf") {
            Add-BattleStartNotificationRecord -Message $response -Json $responseJson -Scenario "while waiting for $Type"
            throw "[$Type] Unexpected BattleStartNtf before request response: $responseJson"
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

function Assert-PlayerBattleReadyState {
    param(
        [object]$Room,
        [string]$PlayerId,
        [bool]$ExpectedBattleReady,
        [string]$Scenario
    )

    $player = Get-RoomPlayer -Room $Room -PlayerId $PlayerId
    if ($null -eq $player) {
        throw "[$Scenario] Expected room '$($Room.roomId)' to contain player '$PlayerId'."
    }

    if ([bool]$player.isBattleReady -ne $ExpectedBattleReady) {
        throw "[$Scenario] Expected player '$PlayerId' isBattleReady=$ExpectedBattleReady, got '$($player.isBattleReady)'."
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

function Receive-LoadBattleSceneNotification {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket,
        [string]$Scenario,
        [string]$ExpectedRoomId,
        [string]$ExpectedSceneKey
    )

    $messageJson = Receive-TextMessage -Socket $Socket -TimeoutMilliseconds 3000
    $message = $messageJson | ConvertFrom-Json

    if ($message.type -ne "LoadBattleSceneNtf") {
        throw "[$Scenario] Expected LoadBattleSceneNtf, got '$($message.type)': $messageJson"
    }

    if ($message.msgId -ne 4001) {
        throw "[$Scenario] Expected msgId 4001, got '$($message.msgId)': $messageJson"
    }

    $payload = $message.payload
    Assert-NotEmpty -Value ([string]$payload.battleId) -Message "[$Scenario] Expected non-empty battleId: $messageJson"

    if ($payload.roomId -ne $ExpectedRoomId) {
        throw "[$Scenario] Expected roomId '$ExpectedRoomId', got '$($payload.roomId)': $messageJson"
    }

    if ($payload.sceneKey -ne $ExpectedSceneKey) {
        throw "[$Scenario] Expected sceneKey '$ExpectedSceneKey', got '$($payload.sceneKey)': $messageJson"
    }

    Add-LoadingNotificationRecord -Message $message -Json $messageJson -Scenario $Scenario
    return $message
}

function Receive-BattleStartNotification {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket,
        [string]$Scenario,
        [string]$ExpectedBattleId,
        [string]$ExpectedRoomId
    )

    $messageJson = Receive-TextMessage -Socket $Socket -TimeoutMilliseconds 3000
    $message = $messageJson | ConvertFrom-Json

    if ($message.type -ne "BattleStartNtf") {
        throw "[$Scenario] Expected BattleStartNtf, got '$($message.type)': $messageJson"
    }

    if ($message.msgId -ne 4005) {
        throw "[$Scenario] Expected msgId 4005, got '$($message.msgId)': $messageJson"
    }

    $payload = $message.payload
    if ($payload.battleId -ne $ExpectedBattleId) {
        throw "[$Scenario] Expected battleId '$ExpectedBattleId', got '$($payload.battleId)': $messageJson"
    }

    if ($payload.roomId -ne $ExpectedRoomId) {
        throw "[$Scenario] Expected roomId '$ExpectedRoomId', got '$($payload.roomId)': $messageJson"
    }

    if ([long]$payload.serverStartTime -le 0) {
        throw "[$Scenario] Expected positive serverStartTime: $messageJson"
    }

    Add-BattleStartNotificationRecord -Message $message -Json $messageJson -Scenario $Scenario
    return $message
}

function Register-And-Login {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket,
        [string]$Username,
        [string]$Nickname
    )

    $register = Invoke-ServerRequest `
        -Socket $Socket `
        -MsgId 1001 `
        -Type "RegisterReq" `
        -Payload @{ username = $Username; password = $script:Password; nickname = $Nickname }
    Assert-ServerResponse -Exchange $register -Scenario "register $Nickname" -ExpectedType "RegisterRes" -ExpectedCode 0
    Add-Scenario -Name "register $Nickname" -Exchange $register

    $login = Invoke-ServerRequest `
        -Socket $Socket `
        -MsgId 1003 `
        -Type "LoginReq" `
        -Payload @{ username = $Username; password = $script:Password }
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

    $owner = Register-And-Login -Socket $ownerSocket -Username ($UsernamePrefix + "_owner") -Nickname "BattleReadyOwner"
    $member = Register-And-Login -Socket $memberSocket -Username ($UsernamePrefix + "_member") -Nickname "BattleReadyMember"

    $createRoom = Invoke-ServerRequest `
        -Socket $ownerSocket `
        -MsgId 3101 `
        -Type "CreateRoomReq" `
        -Payload @{ roomName = "Battle Ready Room"; maxPlayers = 2 } `
        -Token $owner.Token
    Assert-ServerResponse -Exchange $createRoom -Scenario "owner create room" -ExpectedType "CreateRoomRes" -ExpectedCode 0
    Add-Scenario -Name "owner create room" -Exchange $createRoom

    $roomId = [string]$createRoom.Response.payload.room.roomId
    Assert-NotEmpty -Value $roomId -Message "[owner create room] Expected non-empty roomId: $($createRoom.ResponseJson)"

    Receive-RoomStateNotification `
        -Socket $ownerSocket `
        -Scenario "create room notification" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Waiting" `
        -ExpectedPlayerCount 1 `
        -ExpectedPlayerIds @($owner.PlayerId) | Out-Null

    $joinRoom = Invoke-ServerRequest `
        -Socket $memberSocket `
        -MsgId 3103 `
        -Type "JoinRoomReq" `
        -Payload @{ roomId = $roomId } `
        -Token $member.Token
    Assert-ServerResponse -Exchange $joinRoom -Scenario "member join room" -ExpectedType "JoinRoomRes" -ExpectedCode 0
    Add-Scenario -Name "member join room" -Exchange $joinRoom

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

    $memberReady = Invoke-ServerRequest `
        -Socket $memberSocket `
        -MsgId 3107 `
        -Type "ReadyReq" `
        -Payload @{ roomId = $roomId; isReady = $true } `
        -Token $member.Token
    Assert-ServerResponse -Exchange $memberReady -Scenario "member lobby ready" -ExpectedType "ReadyRes" -ExpectedCode 0
    Add-Scenario -Name "member lobby ready" -Exchange $memberReady
    Assert-PlayerReadyState -Room $memberReady.Response.payload.room -PlayerId $member.PlayerId -ExpectedReady $true -Scenario "member lobby ready"

    Receive-RoomStateNotification `
        -Socket $ownerSocket `
        -Scenario "ready notification to owner" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Waiting" `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId) | Out-Null

    Receive-RoomStateNotification `
        -Socket $memberSocket `
        -Scenario "ready notification to member" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Waiting" `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId) | Out-Null

    $startBattle = Invoke-ServerRequest `
        -Socket $ownerSocket `
        -MsgId 3109 `
        -Type "StartBattleReq" `
        -Payload @{ roomId = $roomId } `
        -Token $owner.Token
    Assert-ServerResponse -Exchange $startBattle -Scenario "owner start loading" -ExpectedType "StartBattleRes" -ExpectedCode 0
    Add-Scenario -Name "owner start loading" -Exchange $startBattle
    Assert-RoomSnapshot `
        -Room $startBattle.Response.payload.room `
        -Scenario "owner start loading" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Loading" `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId)

    Receive-RoomStateNotification `
        -Socket $ownerSocket `
        -Scenario "loading room notification to owner" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Loading" `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId) | Out-Null

    Receive-RoomStateNotification `
        -Socket $memberSocket `
        -Scenario "loading room notification to member" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Loading" `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId) | Out-Null

    $ownerLoadingTask = Receive-LoadBattleSceneNotification `
        -Socket $ownerSocket `
        -Scenario "load battle scene notification to owner" `
        -ExpectedRoomId $roomId `
        -ExpectedSceneKey "BattleArena_Training"

    $memberLoadingTask = Receive-LoadBattleSceneNotification `
        -Socket $memberSocket `
        -Scenario "load battle scene notification to member" `
        -ExpectedRoomId $roomId `
        -ExpectedSceneKey "BattleArena_Training"

    $battleId = [string]$ownerLoadingTask.payload.battleId
    if ($memberLoadingTask.payload.battleId -ne $battleId) {
        throw "Expected both clients to receive the same battleId. Owner='$battleId', member='$($memberLoadingTask.payload.battleId)'."
    }

    $ownerBattleReady = Invoke-ServerRequest `
        -Socket $ownerSocket `
        -MsgId 4003 `
        -Type "ClientBattleReadyReq" `
        -Payload @{ battleId = $battleId; roomId = $roomId } `
        -Token $owner.Token
    Assert-ServerResponse -Exchange $ownerBattleReady -Scenario "owner battle ready only" -ExpectedType "ClientBattleReadyRes" -ExpectedCode 0
    Add-Scenario -Name "owner battle ready only" -Exchange $ownerBattleReady
    Assert-RoomSnapshot `
        -Room $ownerBattleReady.Response.payload.room `
        -Scenario "owner battle ready only" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Loading" `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId)
    Assert-PlayerBattleReadyState -Room $ownerBattleReady.Response.payload.room -PlayerId $owner.PlayerId -ExpectedBattleReady $true -Scenario "owner battle ready only"
    Assert-PlayerBattleReadyState -Room $ownerBattleReady.Response.payload.room -PlayerId $member.PlayerId -ExpectedBattleReady $false -Scenario "owner battle ready only"

    $ownerReadyNtf = Receive-RoomStateNotification `
        -Socket $ownerSocket `
        -Scenario "owner battle ready notification to owner" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Loading" `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId)
    Assert-PlayerBattleReadyState -Room $ownerReadyNtf.payload.room -PlayerId $owner.PlayerId -ExpectedBattleReady $true -Scenario "owner battle ready notification to owner"
    Assert-PlayerBattleReadyState -Room $ownerReadyNtf.payload.room -PlayerId $member.PlayerId -ExpectedBattleReady $false -Scenario "owner battle ready notification to owner"

    $memberSeesOwnerReadyNtf = Receive-RoomStateNotification `
        -Socket $memberSocket `
        -Scenario "owner battle ready notification to member" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Loading" `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId)
    Assert-PlayerBattleReadyState -Room $memberSeesOwnerReadyNtf.payload.room -PlayerId $owner.PlayerId -ExpectedBattleReady $true -Scenario "owner battle ready notification to member"
    Assert-PlayerBattleReadyState -Room $memberSeesOwnerReadyNtf.payload.room -PlayerId $member.PlayerId -ExpectedBattleReady $false -Scenario "owner battle ready notification to member"

    # A normal request after the first battle-ready confirms no early BattleStartNtf is queued.
    $ownerPingBeforeAllReady = Invoke-ServerRequest `
        -Socket $ownerSocket `
        -MsgId 1 `
        -Type "PingReq" `
        -Payload @{ clientTime = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() }
    Assert-ServerResponse -Exchange $ownerPingBeforeAllReady -Scenario "owner ping before all battle ready" -ExpectedType "PingRes" -ExpectedCode 0
    Add-Scenario -Name "owner ping before all battle ready" -Exchange $ownerPingBeforeAllReady

    $memberPingBeforeAllReady = Invoke-ServerRequest `
        -Socket $memberSocket `
        -MsgId 1 `
        -Type "PingReq" `
        -Payload @{ clientTime = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() }
    Assert-ServerResponse -Exchange $memberPingBeforeAllReady -Scenario "member ping before all battle ready" -ExpectedType "PingRes" -ExpectedCode 0
    Add-Scenario -Name "member ping before all battle ready" -Exchange $memberPingBeforeAllReady

    if (@($battleStartNotifications).Count -ne 0) {
        throw "Expected no BattleStartNtf before every player is battle-ready."
    }

    $memberBattleReady = Invoke-ServerRequest `
        -Socket $memberSocket `
        -MsgId 4003 `
        -Type "ClientBattleReadyReq" `
        -Payload @{ battleId = $battleId; roomId = $roomId } `
        -Token $member.Token
    Assert-ServerResponse -Exchange $memberBattleReady -Scenario "member battle ready completes room" -ExpectedType "ClientBattleReadyRes" -ExpectedCode 0
    Add-Scenario -Name "member battle ready completes room" -Exchange $memberBattleReady
    Assert-RoomSnapshot `
        -Room $memberBattleReady.Response.payload.room `
        -Scenario "member battle ready completes room" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Battle" `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId)
    Assert-PlayerBattleReadyState -Room $memberBattleReady.Response.payload.room -PlayerId $owner.PlayerId -ExpectedBattleReady $true -Scenario "member battle ready completes room"
    Assert-PlayerBattleReadyState -Room $memberBattleReady.Response.payload.room -PlayerId $member.PlayerId -ExpectedBattleReady $true -Scenario "member battle ready completes room"

    $ownerBattleRoomNtf = Receive-RoomStateNotification `
        -Socket $ownerSocket `
        -Scenario "battle room notification to owner" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Battle" `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId)
    Assert-PlayerBattleReadyState -Room $ownerBattleRoomNtf.payload.room -PlayerId $owner.PlayerId -ExpectedBattleReady $true -Scenario "battle room notification to owner"
    Assert-PlayerBattleReadyState -Room $ownerBattleRoomNtf.payload.room -PlayerId $member.PlayerId -ExpectedBattleReady $true -Scenario "battle room notification to owner"

    $memberBattleRoomNtf = Receive-RoomStateNotification `
        -Socket $memberSocket `
        -Scenario "battle room notification to member" `
        -RoomId $roomId `
        -ExpectedOwnerPlayerId $owner.PlayerId `
        -ExpectedState "Battle" `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId)
    Assert-PlayerBattleReadyState -Room $memberBattleRoomNtf.payload.room -PlayerId $owner.PlayerId -ExpectedBattleReady $true -Scenario "battle room notification to member"
    Assert-PlayerBattleReadyState -Room $memberBattleRoomNtf.payload.room -PlayerId $member.PlayerId -ExpectedBattleReady $true -Scenario "battle room notification to member"

    Receive-BattleStartNotification `
        -Socket $ownerSocket `
        -Scenario "battle start notification to owner" `
        -ExpectedBattleId $battleId `
        -ExpectedRoomId $roomId | Out-Null

    Receive-BattleStartNotification `
        -Socket $memberSocket `
        -Scenario "battle start notification to member" `
        -ExpectedBattleId $battleId `
        -ExpectedRoomId $roomId | Out-Null

    $expectedRoomNotificationCount = 11
    if (@($roomNotifications).Count -ne $expectedRoomNotificationCount) {
        throw "Expected $expectedRoomNotificationCount RoomStateNtf messages, got $(@($roomNotifications).Count)."
    }

    $expectedLoadingNotificationCount = 2
    if (@($loadingNotifications).Count -ne $expectedLoadingNotificationCount) {
        throw "Expected $expectedLoadingNotificationCount LoadBattleSceneNtf messages, got $(@($loadingNotifications).Count)."
    }

    $expectedBattleStartNotificationCount = 2
    if (@($battleStartNotifications).Count -ne $expectedBattleStartNotificationCount) {
        throw "Expected $expectedBattleStartNotificationCount BattleStartNtf messages, got $(@($battleStartNotifications).Count)."
    }

    $resultObject = [pscustomobject]@{
        ok = $true
        url = $Url
        usernamePrefix = $UsernamePrefix
        roomId = $roomId
        battleId = $battleId
        finalRoomState = [string]$memberBattleReady.Response.payload.room.state
        roomStateNotificationCount = @($roomNotifications).Count
        loadBattleSceneNotificationCount = @($loadingNotifications).Count
        battleStartNotificationCount = @($battleStartNotifications).Count
        scenarios = $scenarios
    }

    $resultObject | ConvertTo-Json -Compress -Depth 12
}
catch [System.AggregateException] {
    $inner = $_.Exception.InnerException
    $innerMessage = if ($inner -ne $null) { $inner.Message } else { $_.Exception.Message }
    throw "Battle ready smoke test failed. Make sure the server is listening at $Url. Original error: $innerMessage"
}
catch {
    throw "Battle ready smoke test failed. $($_.Exception.Message)"
}
finally {
    foreach ($socket in $sockets) {
        try {
            if ($socket.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
                $closeTask = $socket.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "battle ready smoke test done", $ct)
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
