import json, os, pathlib, subprocess
root = pathlib.Path(__file__).resolve().parent
references = (root / 'dotnet/packs/Microsoft.NETCore.App.Ref/9.0.18/ref/net9.0').glob('*.dll')
response = root / 'BuildDriver.rsp'
response.write_text('\n'.join(['-nologo', '-target:exe', '-out:' + str(root / 'BuildDriver.dll'), '-r:' + ','.join(map(str, references)), str(root / 'BuildDriver.cs')]))
(root / 'BuildDriver.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {'tfm':'net9.0','framework':{'name':'Microsoft.NETCore.App','version':'9.0.18'}}}))
env = os.environ.copy()
env.update(TMPDIR=str(root/'tmp'), DOTNET_EnableDiagnostics='0', DOTNET_PROCESSOR_COUNT='2', DOTNET_GCHeapHardLimit='30000000')
raise SystemExit(subprocess.call([str(root/'dotnet-thread-host'), str(root/'dotnet/sdk/9.0.316/Roslyn/bincore/csc.dll'), '@'+str(response)],env=env))
