"""Fetch a pinned official sing-box core for isolated CI checks, with SHA-256 verification."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import urllib.request
import zipfile

VERSION = '1.14.3'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    name = f'sing-box-{VERSION}-windows-amd64.zip'
    api = f'https://api.github.com/repos/SagerNet/sing-box/releases/tags/v{VERSION}'
    request = urllib.request.Request(api, headers={'User-Agent': 'S2N-Isolated-CI-Core-Verification'})
    with urllib.request.urlopen(request, timeout=60) as response:
        release = json.load(response)
    asset = next(item for item in release['assets'] if item['name'] == name)
    digest = asset.get('digest', '')
    if not re.fullmatch(r'sha256:[0-9a-f]{64}', digest):
        raise ValueError('The official release has no SHA-256 digest for the CI core')
    if asset['browser_download_url'] != f'https://github.com/SagerNet/sing-box/releases/download/v{VERSION}/{name}':
        raise ValueError('Unexpected upstream download URL')
    directory = args.output.resolve()
    directory.mkdir(parents=True, exist_ok=True)
    archive = directory / name
    try:
        hasher = hashlib.sha256()
        count = 0
        with urllib.request.urlopen(asset['browser_download_url'], timeout=90) as source, archive.open('wb') as target:
            while chunk := source.read(1024 * 1024):
                count += len(chunk)
                if count > min(asset['size'], 150 * 1024 * 1024):
                    raise ValueError('Unexpected upstream core size')
                target.write(chunk)
                hasher.update(chunk)
        if count != asset['size'] or 'sha256:' + hasher.hexdigest() != digest:
            raise ValueError('Official core checksum verification failed')
        prefix = f'sing-box-{VERSION}-windows-amd64/'
        with zipfile.ZipFile(archive) as package:
            for member in package.infolist():
                if member.filename == prefix + 'sing-box.exe' or (
                    member.filename.startswith(prefix) and '/' not in member.filename[len(prefix):]
                    and member.filename.lower().endswith('.dll')):
                    if member.file_size > 256 * 1024 * 1024 or member.flag_bits & 1:
                        raise ValueError('Unexpected executable archive member')
                    (directory / Path(member.filename).name).write_bytes(package.read(member))
        if not (directory / 'sing-box.exe').is_file():
            raise ValueError('Official archive is missing sing-box.exe')
        print(json.dumps({'version': VERSION, 'sha256': hasher.hexdigest(), 'verified': True,
                          'executable': str(directory / 'sing-box.exe')}))
    finally:
        if archive.exists():
            archive.unlink()


if __name__ == '__main__':
    main()
