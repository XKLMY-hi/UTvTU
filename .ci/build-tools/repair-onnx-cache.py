"""Repair a truncated NuGet HTTP cache entry without changing the project."""
import base64
import hashlib
import pathlib
import shutil
import subprocess
import zipfile

root = pathlib.Path(__file__).resolve().parent
name = 'microsoft.ml.onnxruntime.1.23.2.nupkg'
package = root / 'downloads' / name
expected = 'sVAnmG5xfi4JW7YU70jY6z4kSWaCt39tnyRHnqy7QlLIHA4RZ+mBBnzKUKAjhJYyTyPDPy92Wui7MY/kFgs2mA=='

def valid(path):
    if not path.exists():
        return False
    digest = hashlib.sha512()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return base64.b64encode(digest.digest()).decode() == expected and zipfile.is_zipfile(path)

if not valid(package):
    temporary = package.with_suffix('.download')
    subprocess.run(['curl', '--fail', '--location', '--connect-timeout', '15', '--max-time', '300', '--silent', '--show-error', 'https://api.nuget.org/v3-flatcontainer/microsoft.ml.onnxruntime/1.23.2/' + name, '-o', str(temporary)], check=True)
    if not valid(temporary):
        raise SystemExit('ONNX package checksum mismatch.')
    temporary.replace(package)
for cache in (root / 'nuget-http').rglob('nupkg_microsoft.ml.onnxruntime.1.23.2.dat'):
    if not valid(cache):
        cache.rename(cache.with_suffix('.partial'))
        shutil.copyfile(package, cache)
print('ONNX package and matching cache entries verified.')
