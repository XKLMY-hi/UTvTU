"""Recreate the Linux build and screenshot tooling alongside an existing UTvTU checkout."""
import argparse
import hashlib
import html
import os
import pathlib
import subprocess
import zipfile

root = pathlib.Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument('--repo', type=pathlib.Path, default=root.parent / 'UTvTU')
options = parser.parse_args()
repo = options.repo.resolve()
if not (repo / 'OpenUtau/OpenUtau.csproj').exists():
    raise SystemExit('Restore the UTvTU checkout before setting up the tools.')
for name in ['downloads', 'dotnet', 'tmp', 'nuget', 'nuget-http', 'nuget-scratch', 'dotnet-home', 'cache', 'headless', 'data/NuGet/Migrations']:
    (root / name).mkdir(parents=True, exist_ok=True)
# This is a fresh workspace, so there is no legacy NuGet state to migrate.
(root / 'data/NuGet/Migrations/1').write_text('1')

def digest(path):
    value = hashlib.sha512()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            value.update(block)
    return value.hexdigest()

def download(url, filename, checksum):
    target = root / 'downloads' / filename
    if target.exists() and digest(target) == checksum:
        return target
    temporary = target.with_suffix(target.suffix + '.download')
    subprocess.run(['curl', '--fail', '--location', '--connect-timeout', '15', '--max-time', '300', '--silent', '--show-error', url, '-o', str(temporary)], check=True)
    if digest(temporary) != checksum:
        raise RuntimeError('Downloaded file checksum mismatch: ' + filename)
    temporary.replace(target)
    return target

sdk = root / 'dotnet/sdk/9.0.316'
if not (sdk / 'MSBuild.dll').exists():
    archive = download('https://builds.dotnet.microsoft.com/dotnet/Sdk/9.0.316/dotnet-sdk-9.0.316-linux-x64.tar.gz', 'dotnet-sdk-9.0.316-linux-x64.tar.gz', '5a8558afd648c14a835e00ae08fa556083f50e3ada164d3e73293fcd4850b0519a27c11f2dae95a9bbe4af432be33bf14451ef11ba69527e34f9cf3077a1c2b5')
    subprocess.run(['tar', '--no-same-owner', '-xzf', str(archive), '-C', str(root / 'dotnet')], check=True)
package = download('https://api.nuget.org/v3-flatcontainer/avalonia.headless/12.1.0/avalonia.headless.12.1.0.nupkg', 'avalonia.headless.12.1.0.nupkg', '4314c0969976bbe69746d0b002c4e5a93a6a7042ca1dd8c3f047d39ef6afebb32c46ecc15befb1e7bacfc0e637708b217f9fe6dd798a94a66a647640d5c3b866')
with zipfile.ZipFile(package) as archive:
    (root / 'headless/Avalonia.Headless.dll').write_bytes(archive.read('lib/net8.0/Avalonia.Headless.dll'))
include = root / 'dotnet/packs/Microsoft.NETCore.App.Host.linux-x64/9.0.18/runtimes/linux-x64/native'
subprocess.run(['gcc', '-O2', '-pthread', '-I', str(include), str(root / 'dotnet-thread-host.c'), '-ldl', '-o', str(root / 'dotnet-thread-host')], check=True)
(root / 'csc-wrapper').chmod(0o755)
subprocess.run(['python', str(root / 'compile-driver.py')], check=True)
(root / 'fonts.conf').write_text('<?xml version="1.0"?><!DOCTYPE fontconfig SYSTEM "fonts.dtd"><fontconfig><dir>' + html.escape(str(repo / 'OpenUtau/Assets/Fonts')) + '</dir><cachedir>' + html.escape(str(root / 'cache/fontconfig')) + '</cachedir></fontconfig>')
print('Build tools restored. Run run-build.py with /t:Restore and then /t:Build.')
