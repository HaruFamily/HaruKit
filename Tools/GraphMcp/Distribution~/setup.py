"""Prepare project-local UPM dependencies and skill. Preview by default; --apply writes.
Python 3.9+, standard library only. Close Unity before applying dependency changes.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import tempfile

MCP = 'https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#81542a7e86292b5c4c21b1782c5af23b567f45ce'
NAMES = {'com.harufamily.dependencycore.graphkit': 'DependencyCore/GraphKit',
         'com.harufamily.tools.graphmcp': 'Tools/GraphMcp'}


def encode(data):
    return (json.dumps(data, ensure_ascii=False, indent=2) + '\n').encode('utf-8')


def sha(data):
    return hashlib.sha256(data).hexdigest()


def contained(root, path):
    path = path.resolve()
    if root not in path.parents:
        raise ValueError('Path leaves project: ' + str(path))
    return path


def write_new(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('xb') as stream:
        stream.write(data)


def install(project, location, revision=None, skill_root='.agents/skills', skill_only=False, apply=False):
    project, location = Path(project).resolve(), Path(location).resolve()
    manifest = contained(project, project / 'Packages/manifest.json')
    if not (project / 'Assets').is_dir() or not (project / 'ProjectSettings/ProjectVersion.txt').is_file():
        raise ValueError('Select an existing Unity project with Assets and ProjectVersion.txt.')
    original = manifest.read_bytes()
    data = json.loads(original.decode('utf-8-sig'))
    expected, files = {}, {}
    release_path = location / 'release.json'
    bundle = json.loads(release_path.read_text(encoding='utf-8')) if release_path.exists() else None
    package = location.parent
    skill_source = location / 'skill' if bundle else package / 'AgentSkill~/harugraph-mcp'
    if not skill_only:
        expected = {'com.coplaydev.unity-mcp': MCP, 'com.unity.nuget.newtonsoft-json': '3.2.2'}
        if bundle:
            if set(bundle['packages']) != set(NAMES):
                raise ValueError('Release must contain exactly GraphKit and GraphMcp.')
            for name, item in bundle['packages'].items():
                filename = item['file']
                if Path(filename).name != filename or not filename.endswith('.tgz'):
                    raise ValueError('Invalid release archive path.')
                content = (location / filename).read_bytes()
                if sha(content) != item['sha256']:
                    raise ValueError('Archive hash mismatch: ' + filename)
                relative = 'HaruGraphDistribution~/' + filename
                files[contained(project, project / 'Packages' / relative)] = content
                expected[name] = 'file:' + relative
        else:
            if not revision or not re.fullmatch('[0-9a-fA-F]{40}', revision):
                raise ValueError('Git installation requires --harukit-revision FULL_40_CHARACTER_PUBLISHED_COMMIT.')
            for name, path in NAMES.items():
                expected[name] = 'https://github.com/HaruFamily/HaruKit.git?path=/' + path + '#' + revision
        dependencies = data.setdefault('dependencies', {})
        for name, value in expected.items():
            if name in dependencies and dependencies[name] != value:
                raise ValueError('Dependency conflict: ' + name + ' = ' + str(dependencies[name]) + '; requested ' + value)
            dependencies[name] = value
    if skill_root not in ('.agents/skills', '.claude/skills'):
        raise ValueError('Choose .agents/skills or .claude/skills.')
    skill = skill_source / 'SKILL.md'
    if not skill.is_file():
        raise ValueError('Bundled skill missing: ' + str(skill))
    files[contained(project, project / skill_root / 'harugraph-mcp/SKILL.md')] = skill.read_bytes()
    for path, content in files.items():
        if path.exists() and (not path.is_file() or path.read_bytes() != content):
            raise ValueError('Existing file differs; preserve/reconcile it before installation: ' + str(path))
    updated = original if skill_only or data == json.loads(original.decode('utf-8-sig')) else encode(data)
    additions = [path for path in files if not path.exists()]
    plan = {'project': str(project), 'dependencies': expected,
            'create': [str(p.relative_to(project)) for p in additions], 'manifest_changed': updated != original,
            'applied': False, 'connection_configured': False}
    if not apply:
        return plan
    backup = contained(project, project / 'Packages' / ('manifest.harugraph-' + sha(original)[:16] + '.backup~'))
    if updated != original:
        if backup.exists() and backup.read_bytes() != original:
            raise ValueError('Backup collision: ' + str(backup))
        if not backup.exists():
            write_new(backup, original)
    # Dependencies are activated last, atomically. A failed preparation leaves only identical,
    # unreferenced new files and the backup; rerunning can safely complete the operation.
    for path in additions:
        write_new(path, files[path])
    if manifest.read_bytes() != original:
        raise ValueError('Manifest changed during preparation; no manifest replacement attempted.')
    if updated != original:
        fd, temporary = tempfile.mkstemp(prefix='harugraph-', suffix='.tmp~', dir=manifest.parent)
        try:
            with os.fdopen(fd, 'wb') as stream:
                stream.write(updated)
            os.replace(temporary, manifest)
        finally:
            if os.path.exists(temporary):
                os.unlink(temporary)
        plan['backup'] = str(backup)
    plan['applied'] = True
    return plan


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', required=True, type=Path)
    parser.add_argument('--harukit-revision')
    parser.add_argument('--skill-root', choices=['.agents/skills', '.claude/skills'], default='.agents/skills')
    parser.add_argument('--skill-only', action='store_true')
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    try:
        result = install(args.project, Path(__file__).parent, args.harukit_revision, args.skill_root, args.skill_only, args.apply)
    except (ValueError, OSError, KeyError) as error:
        parser.exit(1, 'Installation stopped: ' + str(error) + '\n')
    print(json.dumps(result, indent=2, ensure_ascii=False))


if __name__ == '__main__':
    main()
