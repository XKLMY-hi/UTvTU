import os, pathlib, subprocess, sys
root = pathlib.Path(__file__).resolve().parent
env = os.environ.copy()
env.update(TMPDIR=str(root/'tmp'), XDG_DATA_HOME=str(root/'data'), XDG_CACHE_HOME=str(root/'cache'), NUGET_SCRATCH=str(root/'nuget-scratch'), DOTNET_EnableDiagnostics='0', DOTNET_PROCESSOR_COUNT='2', DOTNET_GCHeapHardLimit='30000000', DOTNET_CLI_HOME=str(root/'dotnet-home'), DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', NUGET_PACKAGES=str(root/'nuget'), NUGET_HTTP_CACHE_PATH=str(root/'nuget-http'), MSBUILDDISABLENODEREUSE='1', DOTNET_HOST_PATH=str(root/'dotnet/dotnet'))
args = sys.argv[1:]
if not any('csctoolpath=' in arg.lower() for arg in args): args.append('/p:CscToolPath=' + str(root))
if not any('csctoolexe=' in arg.lower() for arg in args): args.append('/p:CscToolExe=csc-wrapper')
raise SystemExit(subprocess.call([str(root/'dotnet-thread-host'), str(root/'BuildDriver.dll'), str(root/'dotnet/sdk/9.0.316'), *args], env=env))
