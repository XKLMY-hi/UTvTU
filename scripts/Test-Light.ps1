# Run an explicitly selected small test group, with independent limits for the
# external xUnit v3 runner (VSTest's hang detector alone does not contain it).
param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Filter,
    [ValidateRange(5,60)][int]$Seconds=30,
    [ValidateRange(128,768)][int]$MemoryMb=384
)
$ErrorActionPreference='Stop'
if ([string]::IsNullOrWhiteSpace($Filter)) { throw 'A specific test filter is required.' }
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$runner=Join-Path $repo 'OpenUtau.Test\bin\Debug\net8.0-windows\OpenUtau.Test.exe'
if (-not (Test-Path -LiteralPath $runner)) { throw 'Build the test project before running.' }
$before=Get-Process -Name OpenUtau.Test -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $runner }
if ($before) { throw 'The same checkout already has a test runner; refusing concurrent runs.' }
$results=Join-Path $repo '.dsh\light-test-results'
New-Item -ItemType Directory -Path $results -Force | Out-Null
$log=Join-Path $results 'latest.log'
$err=Join-Path $results 'latest-errors.log'
$resultName=[Guid]::NewGuid().ToString('N')+'.trx'
$proc=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @(
    'test','OpenUtau.Test/OpenUtau.Test.csproj','--no-build','--filter',$Filter,
    '--logger','console;verbosity=normal','--logger',"trx;LogFileName=$resultName",
    '--results-directory','.dsh/light-test-results'
) -WorkingDirectory $repo -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError $err -PassThru
$started=$proc.StartTime
$watch=[Diagnostics.Stopwatch]::StartNew()
$aborted=$false
while (-not $proc.HasExited) {
    $tests=@(Get-Process -Name OpenUtau.Test -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $runner -and $_.StartTime -ge $started })
    if ($watch.Elapsed.TotalSeconds -gt $Seconds -or @($tests | Where-Object { $_.WorkingSet64 -gt $MemoryMb*1MB }).Count) {
        $aborted=$true
        Write-Output "Stopped selected tests: time limit=$Seconds s; memory limit=$MemoryMb MB."
        foreach ($test in $tests) { if (-not $test.HasExited) { $test.Kill() } }
        # Only the CLI started here and its children; application processes are untouched.
        if (-not $proc.HasExited) { $proc.Kill($true) }
        break
    }
    Start-Sleep -Milliseconds 250
    $proc.Refresh()
}
$null=$proc.WaitForExit(5000)
Get-Content -LiteralPath $log -Tail 30
Get-Content -LiteralPath $err -Tail 10
if ($aborted) { exit 124 }
if ($proc.ExitCode -ne 0) { exit 1 }
$resultPath=Join-Path $results $resultName
if (-not (Test-Path -LiteralPath $resultPath)) { throw 'No completed test result was produced.' }
[xml]$result=Get-Content -LiteralPath $resultPath -Raw
$summary=$result.TestRun.ResultSummary
# The adapter can return exit 0 after a fatal external-runner crash. Require a
# completed result with executed tests too; never accept partial passes as green.
if ($summary.outcome -ne 'Completed' -or [int]$summary.Counters.executed -eq 0 -or
    [int]$summary.Counters.failed -gt 0 -or (Select-String -LiteralPath $log,$err -Pattern 'FATAL ERROR' -Quiet)) {
    throw 'The test result was incomplete or failed.'
}
Write-Output "Selected tests passed: $($summary.Counters.passed); elapsed=$([math]::Round($watch.Elapsed.TotalSeconds,1)) s."
exit 0
