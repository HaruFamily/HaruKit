"""Build reproducible UPM tarballs and an installation bundle; never publishes remotely."""
import argparse
import gzip
import hashlib
import io
import json
from pathlib import Path
import shutil
import subprocess
import tarfile


def archive(source):
    raw = io.BytesIO()
    with tarfile.open(fileobj=raw, mode='w', format=tarfile.PAX_FORMAT) as tar:
        for path in sorted(source.rglob('*')):
            if path.is_symlink():
                raise ValueError('Do not distribute symlinks: ' + str(path))
            if not path.is_file() or '.git' in path.parts or '__pycache__' in path.parts or path.suffix == '.pyc':
                continue
            content = path.read_bytes()
            info = tarfile.TarInfo('package/' + path.relative_to(source).as_posix())
            info.size, info.mode, info.mtime = len(content), 0o644, 0
            tar.addfile(info, io.BytesIO(content))
    return gzip.compress(raw.getvalue(), mtime=0)


def build(output):
    package = Path(__file__).resolve().parent.parent
    repository = package.parents[1]
    output = Path(output).resolve()
    if output.exists() or output == repository or repository in output.parents:
        raise ValueError('Output must be new and outside the HaruKit repository.')
    sources = [package]
    files, packages = {}, {}
    for source in sources:
        data = json.loads((source / 'package.json').read_text(encoding='utf-8-sig'))
        filename = data['name'] + '-' + data['version'] + '.tgz'
        content = archive(source)
        files[filename] = content
        packages[data['name']] = {'version': data['version'], 'file': filename, 'sha256': hashlib.sha256(content).hexdigest()}
    revision = subprocess.check_output(['git', '-C', str(repository), 'rev-parse', 'HEAD'], text=True).strip()
    dirty = bool(subprocess.check_output(['git', '-C', str(repository), 'status', '--porcelain'], text=True).strip())
    release = {'format': 1, 'source_revision': revision, 'source_dirty': dirty, 'packages': packages,
               'tested_editor': '6000.3.12f1', 'transport': 'Optional MCP for Unity 10.3.0; --with-mcp opts in'}
    output.mkdir(parents=True)
    for name, content in files.items():
        (output / name).write_bytes(content)
    (output / 'release.json').write_text(json.dumps(release, indent=2) + '\n', encoding='utf-8')
    shutil.copy2(package / 'Distribution~/setup.py', output / 'setup.py')
    shutil.copytree(package / 'AgentSkill~/harugraph-mcp', output / 'skill')
    shutil.copy2(package / 'Documentation~/Mcp/Installation.md', output / 'INSTALL.md')
    print(json.dumps({'bundle': str(output), **release}, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    build(parser.parse_args().output)
