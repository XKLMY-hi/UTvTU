param([string]$Filter='FullyQualifiedName~Setup', [ValidateRange(5,60)][int]$Seconds=30, [ValidateRange(128,768)][int]$MemoryMb=384)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$testProject=Join-Path $repo 'installer/UTvTU.Setup.Tests/UTvTU.Setup.Tests.csproj'
$runner=Join-Path $repo 'installer/UTvTU.Setup.Tests/bin/Debug/net8.0-windows/UTvTU.Setup.Tests.exe'
if (-not (Test-Path -LiteralPath $runner)) { throw '先构建安装器测试项目。' }
if (@(Get-Process -Name UTvTU.Setup.Tests -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $runner }).Count) { throw '已有安装器测试在运行。' }
$results=Join-Path $repo '.dsh/setup-test-results'
New-Item -ItemType Directory -Path $results -Force | Out-Null
$id=[Guid]::NewGuid().ToString('N')
$stdout=Join-Path $results "$id.log"
$stderr=Join-Path $results "$id.err.log"
$trx=Join-Path $results "$id.trx"
$proc=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @('test',('"'+$testProject+'"'),'--no-build','--filter',$Filter,'--logger',"trx;LogFileName=$id.trx",'--results-directory',('"'+$results+'"'),'--','xUnit.ParallelizeTestCollections=false') -WorkingDirectory $repo -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
$started=$proc.StartTime
$timer=[Diagnostics.Stopwatch]::StartNew()
$peak=0
$aborted=$false
while (-not $proc.HasExited) {
    $children=@(Get-Process -Name UTvTU.Setup.Tests -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $runner -and $_.StartTime -ge $started })
    foreach ($child in $children) { $peak=[Math]::Max($peak,$child.WorkingSet64/1MB) }
    if ($timer.Elapsed.TotalSeconds -gt $Seconds -or @($children | Where-Object { $_.WorkingSet64 -gt $MemoryMb*1MB }).Count) {
        $aborted=$true
        foreach ($child in $children) { if (-not $child.HasExited) { $child.Kill() } }
        if (-not $proc.HasExited) { $proc.Kill($true) }
        break
    }
    Start-Sleep -Milliseconds 100
    $proc.Refresh()
}
$null=$proc.WaitForExit(3000)
Get-Content -LiteralPath $stdout -Tail 20
Get-Content -LiteralPath $stderr -Tail 10
if ($aborted -or $proc.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $trx)) { throw '安装器轻量检查失败或超过资源限制。' }
[xml]$result=Get-Content -LiteralPath $trx -Raw
$summary=$result.TestRun.ResultSummary
if ($summary.outcome -ne 'Completed' -or [int]$summary.Counters.executed -eq 0 -or [int]$summary.Counters.failed -gt 0 -or (Select-String -LiteralPath $stdout,$stderr -Pattern 'FATAL ERROR' -Quiet)) { throw '安装器检查未完整通过。' }
[ordered]@{Passed=[int]$summary.Counters.passed; Seconds=[Math]::Round($timer.Elapsed.TotalSeconds,1); PeakMB=[Math]::Round($peak,1); Trx=$trx} | ConvertTo-Json
