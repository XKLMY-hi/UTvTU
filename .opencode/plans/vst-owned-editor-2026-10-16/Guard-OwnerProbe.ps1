param([Parameter(Mandatory=$true)][string]$Executable, [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$exe=(Resolve-Path -LiteralPath $Executable).Path
$bin=Split-Path $exe
if (@(Get-Process OwnerProbe -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }).Count) { throw 'Owner probe already running.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$out=Join-Path $OutputDirectory 'owner-probe.log'
$err=Join-Path $OutputDirectory 'owner-probe-errors.log'
$proc=Start-Process -FilePath $exe -WorkingDirectory $bin -WindowStyle Hidden -RedirectStandardOutput $out -RedirectStandardError $err -PassThru
$watch=[Diagnostics.Stopwatch]::StartNew()
$peak=0
$stopped=$false
while (-not $proc.HasExited) {
    $peak=[Math]::Max($peak,$proc.WorkingSet64/1MB)
    if ($watch.Elapsed.TotalSeconds -gt 30 -or $proc.WorkingSet64 -gt 384MB) { $proc.Kill($true); $stopped=$true; break }
    Start-Sleep -Milliseconds 100
    $proc.Refresh()
}
$null=$proc.WaitForExit(3000)
[ordered]@{ Instances=1; Plugin='Persistent Q'; Seconds=[Math]::Round($watch.Elapsed.TotalSeconds,1); PeakMB=[Math]::Round($peak,1); Stopped=$stopped; ExitCode=$proc.ExitCode; CoreSha256=(Get-FileHash (Join-Path $bin 'OpenUtau.Core.dll')).Hash; NativeSha256=(Get-FileHash (Join-Path $bin 'vst_bridge.dll')).Hash } | ConvertTo-Json | Tee-Object -FilePath (Join-Path $OutputDirectory 'owner-probe-summary.json')
Get-Content -LiteralPath $out -Tail 22
Get-Content -LiteralPath $err -Tail 4
if ($stopped -or $proc.ExitCode -ne 0 -or -not (Select-String -LiteralPath $out -Pattern '^completed$' -Quiet)) { exit 1 }
