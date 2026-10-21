import pathlib,os,json,subprocess
b=pathlib.Path(__file__).resolve().parent;r=b.parent/'UTvTU'
out=b/'capture';out.mkdir(exist_ok=True)
app=r/'OpenUtau/bin/Debug/net8.0/linux-x64'
for p in app.iterdir():
    target=out/p.name
    if not target.exists():target.symlink_to(p)
target=out/'Avalonia.Headless.dll'
if not target.exists():target.symlink_to(b/'headless/Avalonia.Headless.dll')
refs=list((b/'dotnet/packs/Microsoft.NETCore.App.Ref/9.0.18/ref/net9.0').glob('*.dll'))
refs+=[p for p in app.glob('*.dll') if '_Native' not in p.name]+[b/'headless/Avalonia.Headless.dll']
response=b/'CaptureAll.rsp'
response.write_text('\n'.join(['-nologo','-target:exe','-nullable:disable','-out:'+str(out/'OpenUtau.Test.dll'),'-r:'+','.join(str(p) for p in refs),str(b/'CapturePreferencesPolish.cs')]))
(out/'OpenUtau.Test.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net9.0','framework':{'name':'Microsoft.NETCore.App','version':'9.0.18'}}}))
env=os.environ.copy();env.update(TMPDIR=str(b/'tmp'),DOTNET_EnableDiagnostics='0',DOTNET_GCHeapHardLimit='30000000',DOTNET_PROCESSOR_COUNT='2')
raise SystemExit(subprocess.call([str(b/'dotnet-thread-host'),str(b/'dotnet/sdk/9.0.316/Roslyn/bincore/csc.dll'),'@'+str(response)],env=env))
