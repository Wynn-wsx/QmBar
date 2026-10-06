$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime

# WinRT async -> .NET Task helper (works on Windows PowerShell 5.1)
$asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and
    $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
})[0]

function Await($op, $resultType) {
    $m = $asTaskGeneric.MakeGenericMethod($resultType)
    $t = $m.Invoke($null, @($op))
    $t.Wait(-1) | Out-Null
    return $t.Result
}

$null = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType = WindowsRuntime]
$null = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus, Windows.Media.Control, ContentType = WindowsRuntime]

$mgr = Await ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager])
Write-Output "MANAGER_OK"

$sessions = $mgr.GetSessions()
Write-Output "SESSION_COUNT=$($sessions.Count)"
Write-Output "-----"

foreach ($s in $sessions) {
    $cur = $s.SourceAppUserModelId
    Write-Output "APP=$cur"
    try {
        $props = Await ($s.TryGetMediaPropertiesAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties])
        if ($props) {
            Write-Output "  TITLE=$($props.Title)"
            Write-Output "  ARTIST=$($props.Artist)"
            Write-Output "  ALBUM=$($props.AlbumTitle)"
            Write-Output "  GENRE=$($props.Genres -join ',')"
            Write-Output "  TRACK#=$($props.TrackNumber)"
            Write-Output "  HAS_THUMB=$($null -ne $props.Thumbnail)"
        } else {
            Write-Output "  PROPS=null"
        }
    } catch {
        Write-Output "  PROPS_ERROR=$($_.Exception.Message)"
    }
    $pb = $s.GetPlaybackInfo()
    Write-Output "  STATUS=$($pb.PlaybackStatus)"
    Write-Output "  RATE=$($pb.PlaybackRate)"
    $tl = $s.GetTimelineProperties()
    if ($tl) {
        Write-Output "  POS=$($tl.Position) END=$($tl.EndTime)"
    }
    Write-Output "  CONTROLS: play=$($pb.Controls.IsPlayEnabled) pause=$($pb.Controls.IsPauseEnabled) next=$($pb.Controls.IsNextEnabled) prev=$($pb.Controls.IsPreviousEnabled)"
    Write-Output "-----"
}
