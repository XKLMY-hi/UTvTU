# Windows x64 release: fresh staging -> verified payload -> single-file installer.
# Never closes applications or writes into an installed application's directory.
[CmdletBinding()]
param(
    [ValidateSet('Release')][string]$Configuration='Release',
    [ValidateSet('win-x64')][string]$Runtime='win-x64',
    [string]$OutDir='dist',
    [switch]$SkipPublish,
    [string]$PublishDirectory
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not $IsWindows) { throw '此打包入口仅支持 Windows x64。' }
$sdkLine=@(& dotnet --list-sdks | Where-Object { $_ -match '^9\.' }) | Select-Object -Last 1
if (-not $sdkLine) { throw '请先安装 .NET SDK 9。' }
$sdkVersion=($sdkLine -split ' ')[0]
$hostDir=Join-Path $repo '.dsh/packaging/sdk9'
New-Item -ItemType Directory -Path $hostDir -Force | Out-Null
@{sdk=@{version=$sdkVersion;rollForward='disable'}} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $hostDir 'global.json') -Encoding utf8
$work=Join-Path $repo ('.dsh/packaging/'+[Guid]::NewGuid().ToString('N'))
$stage=Join-Path $work 'application'
$uninstaller=Join-Path $work 'uninstaller'
$setup=Join-Path $work 'setup'
New-Item -ItemType Directory -Path $stage,$uninstaller,$setup -Force | Out-Null
$savedLocation=Get-Location
$savedEnvironment=@{}
foreach ($name in @('DOTNET_PROCESSOR_COUNT','DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER','MSBUILDDISABLENODEREUSE','DOTNET_GCHeapHardLimit')) { $savedEnvironment[$name]=[Environment]::GetEnvironmentVariable($name,'Process') }
$env:DOTNET_PROCESSOR_COUNT='2'
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER='1'
$env:MSBUILDDISABLENODEREUSE='1'
$env:DOTNET_GCHeapHardLimit='0x30000000'
function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet 操作失败：$($Arguments[0])，退出码 $LASTEXITCODE。日志与暂存：$work" }
}
try {
    Set-Location $repo
    $commit=(& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw '无法确认源代码提交。' }
    if (-not $SkipPublish -and (& git status --porcelain --untracked-files=no)) { throw '请先提交已完成的源码修改，再构建正式安装包。' }
    $stamp='UTvTU-'+[TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTime]::UtcNow,'China Standard Time').ToString('yy.M.d-HHmmss')
    Set-Location $hostDir
    $common=@('-m:1','-nodeReuse:false','-p:UseSharedCompilation=false','-p:UsedAvaloniaProducts=','-p:NuGetAudit=false')
    $packagerProject=Join-Path $repo 'installer/UTvTU.Setup.Packager/UTvTU.Setup.Packager.csproj'
    Invoke-Dotnet (@('build',$packagerProject,'-c','Release')+$common)
    $packager=Join-Path $repo 'installer/UTvTU.Setup.Packager/bin/Release/net8.0-windows/UTvTU.Setup.Packager.dll'
    if ($SkipPublish) {
        if (-not $PublishDirectory) { throw '-SkipPublish 必须明确提供带来源记录的 -PublishDirectory。' }
        $source=if ([IO.Path]::IsPathFullyQualified($PublishDirectory)) { $PublishDirectory } else { Join-Path $repo $PublishDirectory }
        Invoke-Dotnet @($packager,'validate',$source)
        $identity=Get-Content -LiteralPath (Join-Path $source 'utvtu-build.json') -Raw | ConvertFrom-Json
        $stamp=$identity.Version; $commit=$identity.SourceCommit
        foreach ($item in Get-ChildItem -LiteralPath $source -Force) { Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $stage $item.Name) -Recurse -Force }
    } else {
        Write-Host "发布 $stamp · $commit · $Runtime · SDK $sdkVersion"
        $mainProject=Join-Path $repo 'OpenUtau/OpenUtau.csproj'
        $probeProject=Join-Path $repo 'VstProbe/VstProbe.csproj'
        $installerProject=Join-Path $repo 'installer/UTvTU.Installer/UTvTU.Installer.csproj'
        Invoke-Dotnet (@('restore',$mainProject,'-r',$Runtime,'--disable-parallel',"-p:RuntimeIdentifiers=$Runtime")+$common)
        Invoke-Dotnet (@('restore',$probeProject,'-r',$Runtime,'--disable-parallel',"-p:RuntimeIdentifiers=$Runtime")+$common)
        Invoke-Dotnet (@('publish',$mainProject,'--no-restore','-c',$Configuration,'-r',$Runtime,'--self-contained','true','-o',$stage,"-p:RuntimeIdentifiers=$Runtime","-p:InformationalVersion=$stamp")+$common)
        Invoke-Dotnet (@('publish',$installerProject,'-c',$Configuration,'-r',$Runtime,'--self-contained','true','-o',$uninstaller,"-p:RuntimeIdentifiers=$Runtime","-p:InformationalVersion=$stamp",'-p:InstallerArtifact=Uninstaller','-p:IncludeInstallerPayload=false','-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:EnableCompressionInSingleFile=true')+$common)
        Copy-Item -LiteralPath (Join-Path $uninstaller 'UTvTU-Uninstall.exe') -Destination (Join-Path $stage 'UTvTU-Uninstall.exe')
        # App-local VC runtime comes from Microsoft's redistributable directory, never
        # from arbitrary System32 files. ONNX/native VST dependencies need this too.
        $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        if (-not (Test-Path -LiteralPath $vswhere)) { throw '请安装 Visual Studio C++ 工具及 VC Redistributable，才能收集可再分发的运行时。' }
        $visualStudio=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
        if (-not $visualStudio) { throw '找不到 Visual Studio C++ Redistributable。' }
        $crt=@(Get-ChildItem -LiteralPath (Join-Path $visualStudio 'VC/Redist/MSVC') -Directory | Sort-Object Name -Descending | ForEach-Object { Get-ChildItem -Path (Join-Path $_.FullName 'x64/Microsoft.VC*.CRT') -Directory -ErrorAction SilentlyContinue }) | Select-Object -First 1
        if (-not $crt) { throw '找不到 x64 VC CRT 可再分发目录。' }
        foreach ($dll in Get-ChildItem -LiteralPath $crt.FullName -Filter '*.dll' -File) { Copy-Item -LiteralPath $dll.FullName -Destination (Join-Path $stage $dll.Name) }
        Copy-Item -LiteralPath (Join-Path $repo 'LICENSE.txt') -Destination (Join-Path $stage 'LICENSE.txt')
        @{Version=$stamp;SourceCommit=$commit;Runtime=$Runtime} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'utvtu-build.json') -Encoding utf8
        if ((Get-FileHash -LiteralPath (Join-Path $stage 'vst_bridge.dll')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $repo 'runtimes/win-x64/native/vst_bridge.dll')).Hash) { throw '发布的 VST 桥与当前源码对应二进制不一致。' }
    }
    $payload=Join-Path $work 'payload.zip'
    & dotnet $packager create $stage $payload *> (Join-Path $work 'package-manifest.json')
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $work 'package-manifest.json') -Tail 8; throw '安装载荷验证失败。' }
    Invoke-Dotnet (@('publish',(Join-Path $repo 'installer/UTvTU.Installer/UTvTU.Installer.csproj'),'-c',$Configuration,'-r',$Runtime,'--self-contained','true','-o',$setup,"-p:RuntimeIdentifiers=$Runtime","-p:InformationalVersion=$stamp","-p:InstallerPayload=$payload",'-p:RequireInstallerPayload=true','-p:IncludeInstallerPayload=true','-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:EnableCompressionInSingleFile=true')+$common)
    $output=if ([IO.Path]::IsPathFullyQualified($OutDir)) { $OutDir } else { Join-Path $repo $OutDir }
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $final=Join-Path $output ($stamp+'.exe')
    if (Test-Path -LiteralPath $final) { throw '同版本安装包已存在，请使用另一个输出目录。' }
    Copy-Item -LiteralPath (Join-Path $setup 'UTvTU-Setup.exe') -Destination $final
    $hash=(Get-FileHash -LiteralPath $final -Algorithm SHA256).Hash
    "$hash  $stamp.exe" | Set-Content -LiteralPath (Join-Path $output ($stamp+'.sha256')) -Encoding utf8
    @{Version=$stamp;SourceCommit=$commit;Runtime=$Runtime;Sdk=$sdkVersion;InstallerSha256=$hash;Manifest=(Get-Content -LiteralPath (Join-Path $work 'package-manifest.json') -Raw | ConvertFrom-Json)} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output ($stamp+'.json')) -Encoding utf8
    Write-Host "完成：$final ($([Math]::Round((Get-Item -LiteralPath $final).Length/1MB,1)) MB)"
    Write-Host "SHA256：$hash"
    Write-Host "已验证的发布目录：$stage"
} finally {
    Set-Location $savedLocation
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name,$savedEnvironment[$name],'Process') }
}
