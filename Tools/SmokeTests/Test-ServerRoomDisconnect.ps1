param(
    [string]$Url = "ws://localhost:5050/ws",
    [string]$UsernamePrefix = "",
    [string]$Password = "123456"
)

# Smoke Test: verifies room cleanup when a connected player exits without sending LeaveRoomReq.
# Start the server first: dotnet run --project Server\OnlineRpgServer\OnlineRpgServer.csproj
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($UsernamePrefix)) {
    $UsernamePrefix = "room_disconnect_" + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
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

function Stop-SocketAbruptly {
    param([System.Net.WebSockets.ClientWebSocket]$Socket)

    $Socket.Abort()
    $Socket.Dispose()
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

function Get-JsonArrayCount {
    param([object]$Value)

    if ($null -eq $Value) {
        return 0
    }

    return @($Value).Count
}

function Find-RoomById {
    param(
        [object]$Rooms,
        [string]$RoomId
    )

    foreach ($room in @($Rooms)) {
        if ($null -ne $room -and $room.roomId -eq $RoomId) {
            return $room
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

    foreach ($player in @($Room.players)) {
        if ($null -ne $player -and $player.playerId -eq $PlayerId) {
            return
        }
    }

    throw "[$Scenario] Expected room '$($Room.roomId)' to contain player '$PlayerId'."
}

function Receive-RoomStateNotification {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket,
        [string]$Scenario,
        [string]$RoomId,
        [int]$ExpectedPlayerCount,
        [string[]]$ExpectedPlayerIds = @(),
        [string]$ExpectedOwnerPlayerId = ""
    )

    $messageJson = Receive-TextMessage -Socket $Socket -TimeoutMilliseconds 3000
    $message = $messageJson | ConvertFrom-Json

    if ($message.type -ne "RoomStateNtf") {
        throw "[$Scenario] Expected RoomStateNtf, got '$($message.type)': $messageJson"
    }

    Add-RoomStateNotificationRecord -Message $message -Json $messageJson -Scenario $Scenario

    $room = $message.payload.room

    if ($room.roomId -ne $RoomId) {
        throw "[$Scenario] Expected roomId '$RoomId', got '$($room.roomId)': $messageJson"
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedOwnerPlayerId) -and $room.ownerPlayerId -ne $ExpectedOwnerPlayerId) {
        throw "[$Scenario] Expected owner '$ExpectedOwnerPlayerId', got '$($room.ownerPlayerId)': $messageJson"
    }

    if ((Get-JsonArrayCount -Value $room.players) -ne $ExpectedPlayerCount) {
        throw "[$Scenario] Expected $ExpectedPlayerCount players in notification: $messageJson"
    }

    foreach ($playerId in $ExpectedPlayerIds) {
        Assert-RoomHasPlayer -Room $room -PlayerId $playerId -Scenario $Scenario
    }

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

function Wait-UntilRoomMissingFromLobby {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket,
        [string]$Token,
        [string]$RoomId
    )

    for ($i = 0; $i -lt 20; $i++) {
        $lobby = Invoke-ServerRequest -Socket $Socket -MsgId 2001 -Type "EnterLobbyReq" -Payload @{} -Token $Token
        Assert-ServerResponse -Exchange $lobby -Scenario "enter lobby after owner disconnect" -ExpectedType "EnterLobbyRes" -ExpectedCode 0

        if ($null -eq (Find-RoomById -Rooms $lobby.Response.payload.rooms -RoomId $RoomId)) {
            return $lobby
        }

        Start-Sleep -Milliseconds 100
    }

    throw "[owner disconnect cleanup] Expected room '$RoomId' to disappear from lobby list."
}

try {
    $soloOwnerSocket = New-ConnectedSocket
    $soloObserverSocket = New-ConnectedSocket

    $soloOwner = Register-And-Login -Socket $soloOwnerSocket -Username ($UsernamePrefix + "_solo_owner") -Nickname "SoloOwner"
    $soloObserver = Register-And-Login -Socket $soloObserverSocket -Username ($UsernamePrefix + "_solo_observer") -Nickname "SoloObserver"

    $createSoloRoom = Invoke-ServerRequest `
        -Socket $soloOwnerSocket `
        -MsgId 3101 `
        -Type "CreateRoomReq" `
        -Payload @{ roomName = "Disconnect Solo Room"; maxPlayers = 2 } `
        -Token $soloOwner.Token

    Assert-ServerResponse -Exchange $createSoloRoom -Scenario "solo owner create room" -ExpectedType "CreateRoomRes" -ExpectedCode 0
    Add-Scenario -Name "solo owner create room" -Exchange $createSoloRoom

    $soloRoomId = [string]$createSoloRoom.Response.payload.room.roomId
    Assert-NotEmpty -Value $soloRoomId -Message "[solo owner create room] Expected non-empty roomId: $($createSoloRoom.ResponseJson)"

    Receive-RoomStateNotification `
        -Socket $soloOwnerSocket `
        -Scenario "solo owner create notification" `
        -RoomId $soloRoomId `
        -ExpectedPlayerCount 1 `
        -ExpectedPlayerIds @($soloOwner.PlayerId) `
        -ExpectedOwnerPlayerId $soloOwner.PlayerId | Out-Null

    Stop-SocketAbruptly -Socket $soloOwnerSocket


    $lobbyAfterSoloOwnerDisconnect = Wait-UntilRoomMissingFromLobby `
        -Socket $soloObserverSocket `
        -Token $soloObserver.Token `
        -RoomId $soloRoomId

    Add-Scenario -Name "solo owner disconnect removes room from lobby" -Exchange $lobbyAfterSoloOwnerDisconnect

    $joinDestroyedRoom = Invoke-ServerRequest `
        -Socket $soloObserverSocket `
        -MsgId 3103 `
        -Type "JoinRoomReq" `
        -Payload @{ roomId = $soloRoomId } `
        -Token $soloObserver.Token

    Assert-ServerResponse -Exchange $joinDestroyedRoom -Scenario "join destroyed room after disconnect" -ExpectedType "ErrorRes" -ExpectedCode 3001
    Add-Scenario -Name "join destroyed room after disconnect" -Exchange $joinDestroyedRoom

    $ownerSocket = New-ConnectedSocket
    $memberSocket = New-ConnectedSocket

    $owner = Register-And-Login -Socket $ownerSocket -Username ($UsernamePrefix + "_owner") -Nickname "Owner"
    $member = Register-And-Login -Socket $memberSocket -Username ($UsernamePrefix + "_member") -Nickname "Member"

    $createRoom = Invoke-ServerRequest `
        -Socket $ownerSocket `
        -MsgId 3101 `
        -Type "CreateRoomReq" `
        -Payload @{ roomName = "Disconnect Transfer Room"; maxPlayers = 3 } `
        -Token $owner.Token

    Assert-ServerResponse -Exchange $createRoom -Scenario "owner create room" -ExpectedType "CreateRoomRes" -ExpectedCode 0
    Add-Scenario -Name "owner create room" -Exchange $createRoom

    $roomId = [string]$createRoom.Response.payload.room.roomId
    Assert-NotEmpty -Value $roomId -Message "[owner create room] Expected non-empty roomId: $($createRoom.ResponseJson)"

    Receive-RoomStateNotification `
        -Socket $ownerSocket `
        -Scenario "owner create notification" `
        -RoomId $roomId `
        -ExpectedPlayerCount 1 `
        -ExpectedPlayerIds @($owner.PlayerId) `
        -ExpectedOwnerPlayerId $owner.PlayerId | Out-Null

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
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId) `
        -ExpectedOwnerPlayerId $owner.PlayerId | Out-Null

    Receive-RoomStateNotification `
        -Socket $memberSocket `
        -Scenario "join notification to member" `
        -RoomId $roomId `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($owner.PlayerId, $member.PlayerId) `
        -ExpectedOwnerPlayerId $owner.PlayerId | Out-Null

    Stop-SocketAbruptly -Socket $ownerSocket

    Receive-RoomStateNotification `
        -Socket $memberSocket `
        -Scenario "owner disconnect transfers owner" `
        -RoomId $roomId `
        -ExpectedPlayerCount 1 `
        -ExpectedPlayerIds @($member.PlayerId) `
        -ExpectedOwnerPlayerId $member.PlayerId | Out-Null

    $leaveLast = Invoke-ServerRequest `
        -Socket $memberSocket `
        -MsgId 3105 `
        -Type "LeaveRoomReq" `
        -Payload @{ roomId = $roomId } `
        -Token $member.Token


    Assert-ServerResponse -Exchange $leaveLast -Scenario "member leave after owner disconnect" -ExpectedType "LeaveRoomRes" -ExpectedCode 0
    Add-Scenario -Name "member leave after owner disconnect" -Exchange $leaveLast

    if ($null -ne $leaveLast.Response.payload.room) {
        throw "[member leave after owner disconnect] Expected room to be destroyed: $($leaveLast.ResponseJson)"
    }

    $resultObject = [pscustomobject]@{
        ok = $true
        url = $Url
        usernamePrefix = $UsernamePrefix
        soloRoomId = $soloRoomId
        transferRoomId = $roomId
        ownerAfterDisconnect = $member.PlayerId
        notificationCount = @($notifications).Count
        scenarios = $scenarios
    }

    $resultObject | ConvertTo-Json -Compress -Depth 12
}
catch [System.AggregateException] {
    $inner = $_.Exception.InnerException
    $innerMessage = if ($inner -ne $null) { $inner.Message } else { $_.Exception.Message }
    throw "Room disconnect smoke test failed. Make sure the server is listening at $Url. Original error: $innerMessage"
}
catch {
    throw "Room disconnect smoke test failed. $($_.Exception.Message)"
}
finally {
    foreach ($socket in $sockets) {
        try {
            if ($socket.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
                $closeTask = $socket.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "room disconnect smoke test done", $ct)
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
