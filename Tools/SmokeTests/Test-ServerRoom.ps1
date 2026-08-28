param(
    [string]$Url = "ws://localhost:5050/ws",
    [string]$UsernamePrefix = "",
    [string]$Password = "123456",
    [string]$RoomName = "Smoke Room",
    [int]$MaxPlayers = 2
)

# Smoke Test: verifies server-side lobby/room request-response flow.
# Start the server first: dotnet run --project Server\OnlineRpgServer\OnlineRpgServer.csproj
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($UsernamePrefix)) {
    $UsernamePrefix = "room_" + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
}

if ($MaxPlayers -lt 2) {
    throw "MaxPlayers must be at least 2 for this smoke test."
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

try {
    $wsA = New-ConnectedSocket
    $wsB = New-ConnectedSocket
    $wsC = New-ConnectedSocket

    $unauthorizedLobby = Invoke-ServerRequest -Socket $wsA -MsgId 2001 -Type "EnterLobbyReq" -Payload @{}
    Assert-ServerResponse -Exchange $unauthorizedLobby -Scenario "enter lobby without token" -ExpectedType "ErrorRes" -ExpectedCode 1002
    Add-Scenario -Name "enter lobby without token" -Exchange $unauthorizedLobby

    $playerA = Register-And-Login -Socket $wsA -Username ($UsernamePrefix + "_a") -Nickname "SmokeA"
    $playerB = Register-And-Login -Socket $wsB -Username ($UsernamePrefix + "_b") -Nickname "SmokeB"
    $playerC = Register-And-Login -Socket $wsC -Username ($UsernamePrefix + "_c") -Nickname "SmokeC"

    $enterLobby = Invoke-ServerRequest -Socket $wsA -MsgId 2001 -Type "EnterLobbyReq" -Payload @{} -Token $playerA.Token
    Assert-ServerResponse -Exchange $enterLobby -Scenario "enter lobby" -ExpectedType "EnterLobbyRes" -ExpectedCode 0
    Add-Scenario -Name "enter lobby" -Exchange $enterLobby

    if ($enterLobby.Response.payload.playerInfo.playerId -ne $playerA.PlayerId) {
        throw "[enter lobby] Expected playerInfo '$($playerA.PlayerId)', got '$($enterLobby.Response.payload.playerInfo.playerId)'."
    }

    $createPayload = @{
        roomName = $RoomName
        maxPlayers = $MaxPlayers
    }

    $createRoom = Invoke-ServerRequest -Socket $wsA -MsgId 3101 -Type "CreateRoomReq" -Payload $createPayload -Token $playerA.Token
    Assert-ServerResponse -Exchange $createRoom -Scenario "create room" -ExpectedType "CreateRoomRes" -ExpectedCode 0
    Add-Scenario -Name "create room" -Exchange $createRoom

    $room = $createRoom.Response.payload.room
    $roomId = [string]$room.roomId
    Assert-NotEmpty -Value $roomId -Message "[create room] Expected non-empty roomId: $($createRoom.ResponseJson)"

    if ($room.ownerPlayerId -ne $playerA.PlayerId) {
        throw "[create room] Expected owner '$($playerA.PlayerId)', got '$($room.ownerPlayerId)'."
    }

    if ((Get-JsonArrayCount -Value $room.players) -ne 1) {
        throw "[create room] Expected 1 player in room: $($createRoom.ResponseJson)"
    }

    Assert-RoomHasPlayer -Room $room -PlayerId $playerA.PlayerId -Scenario "create room"

    Receive-RoomStateNotification `
        -Socket $wsA `
        -Scenario "create room notification to owner" `
        -RoomId $roomId `
        -ExpectedPlayerCount 1 `
        -ExpectedPlayerIds @($playerA.PlayerId) `
        -ExpectedOwnerPlayerId $playerA.PlayerId | Out-Null

    $lobbyWithRoom = Invoke-ServerRequest -Socket $wsA -MsgId 2001 -Type "EnterLobbyReq" -Payload @{} -Token $playerA.Token
    Assert-ServerResponse -Exchange $lobbyWithRoom -Scenario "enter lobby with created room" -ExpectedType "EnterLobbyRes" -ExpectedCode 0
    Add-Scenario -Name "enter lobby with created room" -Exchange $lobbyWithRoom

    $listedRoom = Find-RoomById -Rooms $lobbyWithRoom.Response.payload.rooms -RoomId $roomId
    if ($null -eq $listedRoom) {
        throw "[enter lobby with created room] Expected room '$roomId' in lobby list: $($lobbyWithRoom.ResponseJson)"
    }

    $joinMissingPayload = @{ roomId = "r_missing_" + $UsernamePrefix }
    $joinMissing = Invoke-ServerRequest -Socket $wsC -MsgId 3103 -Type "JoinRoomReq" -Payload $joinMissingPayload -Token $playerC.Token
    Assert-ServerResponse -Exchange $joinMissing -Scenario "join missing room" -ExpectedType "ErrorRes" -ExpectedCode 3001
    Add-Scenario -Name "join missing room" -Exchange $joinMissing

    $joinPayload = @{ roomId = $roomId }
    $joinRoom = Invoke-ServerRequest -Socket $wsB -MsgId 3103 -Type "JoinRoomReq" -Payload $joinPayload -Token $playerB.Token
    Assert-ServerResponse -Exchange $joinRoom -Scenario "join room" -ExpectedType "JoinRoomRes" -ExpectedCode 0
    Add-Scenario -Name "join room" -Exchange $joinRoom

    $joinedRoom = $joinRoom.Response.payload.room
    if ((Get-JsonArrayCount -Value $joinedRoom.players) -ne 2) {
        throw "[join room] Expected 2 players in room: $($joinRoom.ResponseJson)"
    }

    Assert-RoomHasPlayer -Room $joinedRoom -PlayerId $playerA.PlayerId -Scenario "join room"
    Assert-RoomHasPlayer -Room $joinedRoom -PlayerId $playerB.PlayerId -Scenario "join room"

    Receive-RoomStateNotification `
        -Socket $wsA `
        -Scenario "join room notification to owner" `
        -RoomId $roomId `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($playerA.PlayerId, $playerB.PlayerId) `
        -ExpectedOwnerPlayerId $playerA.PlayerId | Out-Null

    Receive-RoomStateNotification `
        -Socket $wsB `
        -Scenario "join room notification to joiner" `
        -RoomId $roomId `
        -ExpectedPlayerCount 2 `
        -ExpectedPlayerIds @($playerA.PlayerId, $playerB.PlayerId) `
        -ExpectedOwnerPlayerId $playerA.PlayerId | Out-Null

    $joinFull = Invoke-ServerRequest -Socket $wsC -MsgId 3103 -Type "JoinRoomReq" -Payload $joinPayload -Token $playerC.Token
    Assert-ServerResponse -Exchange $joinFull -Scenario "join full room" -ExpectedType "ErrorRes" -ExpectedCode 3002
    Add-Scenario -Name "join full room" -Exchange $joinFull

    $leaveByNonMember = Invoke-ServerRequest -Socket $wsC -MsgId 3105 -Type "LeaveRoomReq" -Payload $joinPayload -Token $playerC.Token
    Assert-ServerResponse -Exchange $leaveByNonMember -Scenario "leave by non-member" -ExpectedType "ErrorRes" -ExpectedCode 3003
    Add-Scenario -Name "leave by non-member" -Exchange $leaveByNonMember

    $leaveOwner = Invoke-ServerRequest -Socket $wsA -MsgId 3105 -Type "LeaveRoomReq" -Payload $joinPayload -Token $playerA.Token
    Assert-ServerResponse -Exchange $leaveOwner -Scenario "owner leave transfers owner" -ExpectedType "LeaveRoomRes" -ExpectedCode 0
    Add-Scenario -Name "owner leave transfers owner" -Exchange $leaveOwner

    $roomAfterOwnerLeave = $leaveOwner.Response.payload.room
    if ($null -eq $roomAfterOwnerLeave) {
        throw "[owner leave transfers owner] Expected room to remain after owner leaves: $($leaveOwner.ResponseJson)"
    }

    if ($roomAfterOwnerLeave.ownerPlayerId -ne $playerB.PlayerId) {
        throw "[owner leave transfers owner] Expected new owner '$($playerB.PlayerId)', got '$($roomAfterOwnerLeave.ownerPlayerId)'."
    }

    if ((Get-JsonArrayCount -Value $roomAfterOwnerLeave.players) -ne 1) {
        throw "[owner leave transfers owner] Expected 1 player after owner leaves: $($leaveOwner.ResponseJson)"
    }

    Assert-RoomHasPlayer -Room $roomAfterOwnerLeave -PlayerId $playerB.PlayerId -Scenario "owner leave transfers owner"

    Receive-RoomStateNotification `
        -Socket $wsB `
        -Scenario "owner leave notification to remaining player" `
        -RoomId $roomId `
        -ExpectedPlayerCount 1 `
        -ExpectedPlayerIds @($playerB.PlayerId) `
        -ExpectedOwnerPlayerId $playerB.PlayerId | Out-Null

    $leaveLast = Invoke-ServerRequest -Socket $wsB -MsgId 3105 -Type "LeaveRoomReq" -Payload $joinPayload -Token $playerB.Token
    Assert-ServerResponse -Exchange $leaveLast -Scenario "last player leave destroys room" -ExpectedType "LeaveRoomRes" -ExpectedCode 0
    Add-Scenario -Name "last player leave destroys room" -Exchange $leaveLast

    if ($null -ne $leaveLast.Response.payload.room) {
        throw "[last player leave destroys room] Expected null room after last player leaves: $($leaveLast.ResponseJson)"
    }

    $lobbyAfterCleanup = Invoke-ServerRequest -Socket $wsA -MsgId 2001 -Type "EnterLobbyReq" -Payload @{} -Token $playerA.Token
    Assert-ServerResponse -Exchange $lobbyAfterCleanup -Scenario "enter lobby after cleanup" -ExpectedType "EnterLobbyRes" -ExpectedCode 0
    Add-Scenario -Name "enter lobby after cleanup" -Exchange $lobbyAfterCleanup

    $deletedRoom = Find-RoomById -Rooms $lobbyAfterCleanup.Response.payload.rooms -RoomId $roomId
    if ($null -ne $deletedRoom) {
        throw "[enter lobby after cleanup] Expected room '$roomId' to be removed: $($lobbyAfterCleanup.ResponseJson)"
    }

    $expectedNotificationCount = 4
    if ((@($notifications).Count) -ne $expectedNotificationCount) {
        throw "Expected $expectedNotificationCount RoomStateNtf messages, got $(@($notifications).Count)."
    }

    $resultObject = [pscustomobject]@{
        ok = $true
        url = $Url
        usernamePrefix = $UsernamePrefix
        roomId = $roomId
        ownerAfterTransfer = $playerB.PlayerId
        notificationCount = @($notifications).Count
        scenarios = $scenarios
    }

    $resultObject | ConvertTo-Json -Compress -Depth 12
}
catch [System.AggregateException] {
    $inner = $_.Exception.InnerException
    $innerMessage = if ($inner -ne $null) { $inner.Message } else { $_.Exception.Message }
    throw "Room smoke test failed. Make sure the server is listening at $Url. Original error: $innerMessage"
}
catch {
    throw "Room smoke test failed. $($_.Exception.Message)"
}
finally {
    foreach ($socket in $sockets) {
        if ($socket.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
            $closeTask = $socket.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "room smoke test done", $ct)
            $closeTask.Wait()
        }

        $socket.Dispose()
    }
}
