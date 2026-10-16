# Exercise the published executables in a new synthetic directory. Worker mode
# performs only manifest-controlled file operations, never HKCU or shortcuts.
param([Parameter(Mandatory=$true)][string]$Installer, [ValidateRange(10,60)][int]$Seconds=30, [ValidateRange(128,768)][int]$MemoryMb=384)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$installerPath=(Resolve-Path -LiteralPath $Installer).Path
$scratch=Join-Path ([IO.Path]::GetTempPath()) ('UTvTU-Package-Smoke-'+[Guid]::NewGuid().ToString('N'))
$target=[IO.Path]::GetFullPath((Join-Path $scratch 'Application'))
$data=[IO.Path]::GetFullPath((Join-Path $scratch 'Documents/OpenUtau Plus'))
$tools=Join-Path $scratch 'Tools'
New-Item -ItemType Directory -Path $scratch,$tools -Force | Out-Null
if (-not ([IO.Path]::GetFullPath($target)).StartsWith(([IO.Path]::GetFullPath($scratch).TrimEnd('\')+'\'),[StringComparison]::OrdinalIgnoreCase)) { throw '隔离测试路径无效。' }
$ownerSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$results=Join-Path $repo '.dsh/setup-package-results'
New-Item -ItemType Directory -Path $results -Force | Out-Null
$runs=@()
function Invoke-Worker([string]$Executable,[bool]$Uninstall) {
    $job=Join-Path ([IO.Path]::GetTempPath()) ('UTvTU-Setup-Jobs/'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $job -Force | Out-Null
    @{Options=@{Directory=$target;UserSid=$ownerSid;DataDirectory=$data;Uninstall=$Uninstall;Silent=$true;RemoveData=$false;CleanLegacyMachineRegistration=$false}} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $job 'request.json') -Encoding utf8
    $proc=Start-Process -FilePath $Executable -ArgumentList @('--worker-job',('"'+$job+'"')) -WindowStyle Hidden -PassThru
    $timer=[Diagnostics.Stopwatch]::StartNew()
    $peak=0
    $aborted=$false
    while (-not $proc.HasExited) {
        $peak=[Math]::Max($peak,$proc.WorkingSet64/1MB)
        if ($timer.Elapsed.TotalSeconds -gt $Seconds -or $proc.WorkingSet64 -gt $MemoryMb*1MB) {
            Set-Content -LiteralPath (Join-Path $job 'cancel') -Value 'cancel'
            $null=$proc.WaitForExit(3000)
            if (-not $proc.HasExited) { $proc.Kill($true) }
            $aborted=$true; break
        }
        Start-Sleep -Milliseconds 100
        $proc.Refresh()
    }
    $null=$proc.WaitForExit(3000)
    $record=[ordered]@{Mode=$(if($Uninstall){'file-uninstall'}else{'file-install'});Seconds=[Math]::Round($timer.Elapsed.TotalSeconds,1);PeakMB=[Math]::Round($peak,1);ExitCode=$proc.ExitCode;Stopped=$aborted;Job=$job}
    $script:runs+=,$record
    if ($aborted -or $proc.ExitCode -ne 0) { throw "实包文件流程失败：$($record.Mode)，结果 $job" }
    $result=Get-Content -LiteralPath (Join-Path $job 'result.json') -Raw | ConvertFrom-Json
    if (-not $result.Success) { throw $result.Message }
}
try {
    Invoke-Worker $installerPath $false
    $receipt=Get-Content -LiteralPath (Join-Path $target '.utvtu-install.json') -Raw | ConvertFrom-Json
    if ($receipt.Product -ne 'UTvTU' -or $receipt.Directory -ne $target -or $receipt.DataDirectory -ne $data -or -not (Test-Path -LiteralPath (Join-Path $target 'installed.txt'))) { throw '实际安装记录/数据模式不一致。' }
    $uninstaller=Join-Path $tools 'UTvTU-Uninstall.exe'
    Copy-Item -LiteralPath (Join-Path $target 'UTvTU-Uninstall.exe') -Destination $uninstaller
    foreach ($relative in @('Backups/song.ustxp','Singers/test/character.txt','prefs.json','user-song.ustxp','unknown.txt')) {
        $path=Join-Path $target $relative
        New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
        Set-Content -LiteralPath $path -Value 'synthetic user content'
    }
    New-Item -ItemType Directory -Path $data -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $data 'prefs.json') -Value 'synthetic settings'
    Invoke-Worker $uninstaller $true
    foreach ($relative in @('Backups/song.ustxp','Singers/test/character.txt','prefs.json','user-song.ustxp','unknown.txt')) { if (-not (Test-Path -LiteralPath (Join-Path $target $relative))) { throw "用户合成文件误删：$relative" } }
    if (Test-Path -LiteralPath (Join-Path $target 'OpenUtau.exe')) { throw '程序文件未移除。' }
    if (-not (Test-Path -LiteralPath (Join-Path $data 'prefs.json'))) { throw '用户数据未保留。' }
    [ordered]@{Success=$true;Installer=$installerPath;InstallerSha256=(Get-FileHash -LiteralPath $installerPath).Hash;Version=$receipt.Version;SourceCommit=$receipt.SourceCommit;Scratch=$scratch;Runs=$runs;RegistryTouched=$false;ShortcutsTouched=$false;RealProjects=$false} | ConvertTo-Json -Depth 7 | Tee-Object -FilePath (Join-Path $results ([Guid]::NewGuid().ToString('N')+'.json'))
} catch {
    [ordered]@{Success=$false;Error=$_.Exception.Message;Scratch=$scratch;Runs=$runs} | ConvertTo-Json -Depth 7 | Tee-Object -FilePath (Join-Path $results ([Guid]::NewGuid().ToString('N')+'.json'))
    throw
}
