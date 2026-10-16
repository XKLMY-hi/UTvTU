param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$executablePath=(Resolve-Path -LiteralPath $Executable).Path
$bin=Split-Path $executablePath
$existing=@(Get-Process Diagnostic -ErrorAction SilentlyContinue|Where-Object {$_.Path -eq $executablePath})
if($existing.Count){throw 'This diagnostic is already running.'}
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$out=Join-Path $OutputDirectory 'production-manager24.log'
$err=Join-Path $OutputDirectory 'production-manager24-error.log'
$proc=Start-Process -FilePath $executablePath -WorkingDirectory $bin -WindowStyle Hidden -RedirectStandardOutput $out -RedirectStandardError $err -PassThru
$watch=[Diagnostics.Stopwatch]::StartNew()
$peak=0
$stopped=$false
while(-not $proc.HasExited){
    $peak=[Math]::Max($peak,$proc.WorkingSet64/1MB)
    if($watch.Elapsed.TotalSeconds -gt 30 -or $proc.WorkingSet64 -gt 384MB){
        $proc.Kill($true)
        $stopped=$true
        break
    }
    Start-Sleep -Milliseconds 100
    $proc.Refresh()
}
$null=$proc.WaitForExit(3000)
$summary=[ordered]@{
    Tracks=8;SlotsPerTrack=3;Instances=24;SimultaneousEditors=4;Cycles=2
    Stopped=$stopped;ExitCode=$proc.ExitCode
    PeakMB=[Math]::Round($peak,1);Seconds=[Math]::Round($watch.Elapsed.TotalSeconds,1)
    CoreSha256=(Get-FileHash -LiteralPath (Join-Path $bin 'OpenUtau.Core.dll')).Hash
    NativeSha256=(Get-FileHash -LiteralPath (Join-Path $bin 'vst_bridge.dll')).Hash
}
$summary|ConvertTo-Json|Tee-Object -FilePath (Join-Path $OutputDirectory 'production-manager24-summary.json')
Get-Content -LiteralPath $out -Tail 10
Get-Content -LiteralPath $err -Tail 4
if($stopped -or $proc.ExitCode -ne 0 -or -not(Select-String -LiteralPath $out -Pattern ' completed$' -Quiet)){exit 1}