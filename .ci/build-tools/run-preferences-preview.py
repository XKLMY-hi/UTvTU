import os
import pathlib
import subprocess
import sys

root = pathlib.Path(__file__).resolve().parent
subprocess.run([sys.executable, str(root / 'prepare-preferences-polish.py')], check=True)
env = os.environ.copy()
env.update(TMPDIR=str(root/'tmp'), FONTCONFIG_FILE=str(root/'fonts.conf'), XDG_DATA_HOME=str(root/'preferences-data'), XDG_CACHE_HOME=str(root/'cache'), DOTNET_EnableDiagnostics='0', DOTNET_GCHeapHardLimit='18000000', DOTNET_PROCESSOR_COUNT='2')
output = pathlib.Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else root.parent / 'preferences-polish-preview'
raise SystemExit(subprocess.call([str(root/'dotnet-thread-host'), str(root/'capture/OpenUtau.Test.dll'), str(output)], env=env))
