"""Check the shipped directory, then exercise its own executable on native runners."""
import argparse
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def run_check(command, label, suite):
    result = subprocess.run(command, capture_output=True, text=True, encoding='utf-8',
                            errors='replace', timeout=120)
    require(result.returncode == 0,
            f'{label} failed ({result.returncode}): {result.stdout[-3000:]} {result.stderr[-3000:]}')
    report = json.loads(result.stdout.strip().splitlines()[-1])
    require(report.get('result') == 'PASS' and report.get('suite') == suite and report.get('checks', 0) > 0,
            label + ' did not report completed checks')
    print(result.stdout.strip(), flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--directory', type=Path, required=True)
    parser.add_argument('--rid', required=True,
                        choices=('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64'))
    args = parser.parse_args()
    directory = args.directory.resolve(strict=True)
    files = [path for path in directory.rglob('*') if path.is_file()]
    unpacked_bytes = sum(path.stat().st_size for path in files)
    if args.rid.startswith('win-'):
        require(unpacked_bytes < 80_000_000, 'Windows package exceeded the 80 MB unpacked size budget')
    require(not any(path.suffix.lower() in ('.pdb', '.dbg', '.debug', '.mdb') for path in files),
            'Debug symbols must not be included in public desktop packages')
    require(not any(path.is_dir() and path.name.lower().endswith('.dsym') for path in directory.rglob('*')),
            'Native debug symbol bundles must not be included in public desktop packages')
    executable = directory / ('S2N.exe' if args.rid.startswith('win-') else 'S2N')
    for name in (executable.name, 'S2N.dll', 'S2N.deps.json', 'S2N.runtimeconfig.json',
                 'LICENSE', 'README.md', 'docs/licenses/manifest.json'):
        require((directory / name).is_file(), 'Missing packaged file: ' + name)
    runtime = json.loads((directory / 'S2N.runtimeconfig.json').read_text(encoding='utf-8-sig'))['runtimeOptions']
    require(any(item['name'] == 'Microsoft.NETCore.App' for item in runtime.get('includedFrameworks', [])),
            'Public package must include the .NET runtime')
    require(runtime.get('configProperties', {}).get('System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault', True),
            'Production reflection-based JSON serialization must remain available')
    host_os = {'Windows': 'win', 'Linux': 'linux', 'Darwin': 'osx'}.get(platform.system())
    host_arch = {'amd64': 'x64', 'x86_64': 'x64', 'arm64': 'arm64', 'aarch64': 'arm64'}.get(platform.machine().lower())
    native = args.rid == f'{host_os}-{host_arch}'
    if native:
        run_check([str(executable), '--check-package'], 'Published service self-check', 'package')
        command = [str(executable), '--check-ui']
        if host_os == 'linux' and not os.environ.get('DISPLAY'):
            xvfb = shutil.which('xvfb-run')
            require(xvfb, 'Native Linux UI package checks require xvfb-run or DISPLAY')
            command = [xvfb, '-a', *command]
        run_check(command, 'Published UI construction check', 'ui')
    print(json.dumps({'rid': args.rid, 'files': len(files),
                      'unpacked_bytes': unpacked_bytes,
                      'debug_symbols': 0, 'self_contained': True,
                      'native_executable_checks': native,
                      'host_rid': f'{host_os}-{host_arch}', 'result': 'PASS'}), flush=True)


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(1)
