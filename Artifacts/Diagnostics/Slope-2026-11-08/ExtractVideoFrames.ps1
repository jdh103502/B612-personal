$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Media.Editing.MediaClip, Windows.Media.Editing, ContentType = WindowsRuntime]
$null = [Windows.Media.Editing.MediaComposition, Windows.Media.Editing, ContentType = WindowsRuntime]
$null = [Windows.Media.Editing.VideoFramePrecision, Windows.Media.Editing, ContentType = WindowsRuntime]
$null = [Windows.Graphics.Imaging.ImageStream, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
$script:taskAdapter = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.IsGenericMethodDefinition -and
    $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
} | Select-Object -First 1
function Wait-MediaOperation($operation, [Type]$resultType) {
    $task = $script:taskAdapter.MakeGenericMethod($resultType).Invoke($null, @($operation))
    try { if (-not $task.Wait(15000)) { throw 'Media operation timed out.' } }
    catch { if ($task.Exception) { throw $task.Exception.ToString() }; throw }
    return $task.Result
}
$outputRoot = 'C:\Dev\B612\Artifacts\Diagnostics\Slope-2026-11-08'
$sources = @(
    'C:\Users\jdh10\Videos\화면 녹화\화면 녹화 중 2026-11-08 030255.mp4',
    'C:\Users\jdh10\Videos\화면 녹화\화면 녹화 중 2026-11-08 030510.mp4'
)
$reports = @()
for ($videoIndex = 0; $videoIndex -lt $sources.Count; $videoIndex++) {
    $localSource = Join-Path $outputRoot ('video-{0}-source.mp4' -f ($videoIndex + 1))
    if (-not (Test-Path -LiteralPath $localSource)) { Copy-Item -LiteralPath $sources[$videoIndex] -Destination $localSource }
    $file = Wait-MediaOperation ([Windows.Storage.StorageFile]::GetFileFromPathAsync($localSource)) ([Windows.Storage.StorageFile])
    $clip = Wait-MediaOperation ([Windows.Media.Editing.MediaClip]::CreateFromFileAsync($file)) ([Windows.Media.Editing.MediaClip])
    $composition = New-Object Windows.Media.Editing.MediaComposition
    $clipCollectionType = [System.Collections.Generic.ICollection[Windows.Media.Editing.MediaClip]]
    $null = $clipCollectionType.GetMethod('Add').Invoke($composition.Clips, @($clip))
    $duration = [Math]::Min($clip.OriginalDuration.TotalSeconds, $composition.Duration.TotalSeconds)
    $properties = $clip.GetVideoEncodingProperties()
    $times = @()
    for ($frameIndex = 0; $frameIndex -lt 12; $frameIndex++) {
        $seconds = [Math]::Max(0, $duration - 0.1) * $frameIndex / 11
        try { $thumbnail = Wait-MediaOperation ($composition.GetThumbnailAsync(
            [TimeSpan]::FromSeconds($seconds), 1280, 720,
            [Windows.Media.Editing.VideoFramePrecision]::NearestFrame
        )) ([Windows.Graphics.Imaging.ImageStream]) }
        catch { Write-Warning ('Frame unavailable at {0}: {1}' -f $seconds, $_.Exception.Message); break }
        $inputStream = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($thumbnail)
        $targetPath = Join-Path $outputRoot ('video-{0}-frame-{1:00}.jpg' -f ($videoIndex + 1), $frameIndex)
        $outputStream = [System.IO.File]::Open($targetPath, [System.IO.FileMode]::Create)
        try { $inputStream.CopyTo($outputStream) }
        finally { $outputStream.Dispose(); $inputStream.Dispose() }
        $times += [Math]::Round($seconds, 3)
    }
    $reports += [pscustomobject]@{video=$videoIndex+1; source=$sources[$videoIndex]; duration=$duration; width=$properties.Width; height=$properties.Height; timestamps=$times}
}
$reports | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputRoot 'video-metadata.json') -Encoding UTF8
$reports | ConvertTo-Json -Depth 4
