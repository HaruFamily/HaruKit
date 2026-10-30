"""Create a new offline installation probe from resolved package sources, never game Assets.

Usage: python prepare_clean_project.py --source-project PATH --output NEW_PATH
The output must not exist. Run the Unity CLI test command printed on completion.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-project', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--without-mcp', action='store_true')
    args = parser.parse_args()
    source, output = args.source_project.resolve(), args.output.resolve()
    if output.exists() or output == source or source in output.parents:
        parser.error('Output must be a new directory outside the source project.')
    cache = {}
    for manifest in (source / 'Library/PackageCache').glob('*/package.json'):
        data = json.loads(manifest.read_text(encoding='utf-8-sig'))
        cache[(data['name'], data['version'])] = manifest.parent
    lock = json.loads((source / 'Packages/packages-lock.json').read_text())['dependencies']
    roots = {
        'com.harufamily.dependencycore.graphkit': source / 'HaruKit/DependencyCore/GraphKit',
    }
    for name in (('com.unity.test-framework',) if args.without_mcp else ('com.coplaydev.unity-mcp', 'com.unity.test-framework', 'com.unity.nuget.newtonsoft-json')):
        candidates = [path for (package, version), path in cache.items() if package == name]
        if len(candidates) != 1:
            raise RuntimeError('Need exactly one resolved cache entry for ' + name)
        roots[name] = candidates[0]
    pending, packages, builtins = list(roots.items()), {}, {}
    while pending:
        name, path = pending.pop()
        if name in packages:
            continue
        data = json.loads((path / 'package.json').read_text(encoding='utf-8-sig'))
        packages[name] = (path, data)
        for dependency, minimum in data.get('dependencies', {}).items():
            resolved = lock.get(dependency, {})
            if resolved.get('source') == 'builtin' or dependency.startswith('com.unity.modules.'):
                builtins[dependency] = resolved.get('version', minimum)
            elif dependency not in packages:
                dependency_path = roots.get(dependency) or cache.get((dependency, resolved.get('version', minimum)))
                if dependency_path is None:
                    raise RuntimeError('Resolved dependency unavailable: ' + dependency)
                pending.append((dependency, dependency_path))
    # Resolve everything before creating the destination. No existing directory is overwritten.
    (output / 'Assets').mkdir(parents=True)
    (output / 'Packages').mkdir()
    (output / 'ProjectSettings').mkdir()
    shutil.copy2(source / 'ProjectSettings/ProjectVersion.txt', output / 'ProjectSettings/ProjectVersion.txt')
    evidence = []
    for name, (path, data) in sorted(packages.items()):
        destination = output / 'Packages' / name
        shutil.copytree(path, destination, ignore=shutil.ignore_patterns('.git', '__pycache__'))
        digest = hashlib.sha256()
        for item in sorted(destination.rglob('*')):
            if item.is_file():
                digest.update(item.relative_to(destination).as_posix().encode())
                digest.update(item.read_bytes())
        evidence.append({'name': name, 'version': data['version'], 'source': str(path), 'sha256': digest.hexdigest()})
    manifest = {'dependencies': {**builtins, **{name: 'file:' + name for name in sorted(packages)}},
                'testables': ['com.harufamily.dependencycore.graphkit']}
    (output / 'Packages/manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    (output / 'installation-sources.json').write_text(json.dumps(evidence, indent=2) + '\n')
    print(json.dumps({'project': str(output), 'copied_packages': list(sorted(packages)),
                      'assets_initially_empty': True, 'library_initially_absent': True}, indent=2))
    print('Run: unity test "' + str(output) + '" --mode EditMode --output "' + str(output / 'results.xml') + '" --timeout 600')


if __name__ == '__main__':
    main()
