"""Meaningful installer checks; no Unity, network or real project mutations."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('setup_graphmcp', Path(__file__).with_name('setup.py'))
setup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(setup)


class InstallationTests(unittest.TestCase):
    def fixture(self, root):
        project, bundle = root / 'project', root / 'bundle'
        (project / 'Assets').mkdir(parents=True)
        (project / 'ProjectSettings').mkdir()
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 6000.3.12f1\n')
        (project / 'Packages').mkdir()
        (project / 'Packages/manifest.json').write_text('{"dependencies": {"example.keep": "1.0.0"}, "testables": ["example.keep"]}\n')
        (bundle / 'skill').mkdir(parents=True)
        (bundle / 'skill/SKILL.md').write_text('---\nname: harugraph-mcp\ndescription: Test\n---\n')
        packages = {}
        for name in setup.NAMES:
            filename = name + '.tgz'
            (bundle / filename).write_bytes(b'archive fixture')
            packages[name] = {'file': filename, 'sha256': setup.sha(b'archive fixture')}
        (bundle / 'release.json').write_text(json.dumps({'packages': packages}))
        return project, bundle

    def test_preview_apply_preserve_and_repeat(self):
        with tempfile.TemporaryDirectory() as directory:
            project, bundle = self.fixture(Path(directory))
            manifest = project / 'Packages/manifest.json'
            original = manifest.read_bytes()
            setup.install(project, bundle)
            self.assertEqual(original, manifest.read_bytes())
            self.assertFalse((project / '.agents').exists())
            result = setup.install(project, bundle, apply=True)
            self.assertEqual(original, Path(result['backup']).read_bytes())
            current = json.loads(manifest.read_text())
            self.assertEqual('1.0.0', current['dependencies']['example.keep'])
            self.assertEqual(['example.keep'], current['testables'])
            self.assertTrue((project / '.agents/skills/harugraph-mcp/SKILL.md').is_file())
            repeat = setup.install(project, bundle, apply=True)
            self.assertFalse(repeat['manifest_changed'])
            self.assertEqual([], repeat['create'])

    def test_conflicts_and_bad_hash_leave_project_untouched(self):
        for problem in ('dependency', 'skill', 'archive'):
            with self.subTest(problem=problem), tempfile.TemporaryDirectory() as directory:
                project, bundle = self.fixture(Path(directory))
                manifest = project / 'Packages/manifest.json'
                if problem == 'dependency':
                    manifest.write_text(json.dumps({'dependencies': {'com.coplaydev.unity-mcp': 'different'}}))
                elif problem == 'skill':
                    path = project / '.agents/skills/harugraph-mcp/SKILL.md'
                    path.parent.mkdir(parents=True)
                    path.write_text('User customization')
                else:
                    next(bundle.glob('*.tgz')).write_bytes(b'corrupt')
                before = {p.relative_to(project): p.read_bytes() for p in project.rglob('*') if p.is_file()}
                with self.assertRaises(ValueError):
                    setup.install(project, bundle, apply=True)
                after = {p.relative_to(project): p.read_bytes() for p in project.rglob('*') if p.is_file()}
                self.assertEqual(before, after)


if __name__ == '__main__':
    unittest.main()
