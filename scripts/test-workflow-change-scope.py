#!/usr/bin/env python3
"""Exercise the actual workflow scope steps in isolated Git repositories.

Run when changing CI/release scope selection; this is not a per-feature gate.
Requires PowerShell and the same PyYAML dependency used by Skill validation.
"""

import os
from pathlib import Path
import subprocess
import tempfile

import yaml


ROOT = Path(__file__).resolve().parents[1]


def scope_step(workflow, job, step_id):
    data = yaml.safe_load((ROOT / '.github/workflows' / workflow).read_text(encoding='utf-8'))
    return next(s['run'] for s in data['jobs'][job]['steps'] if s.get('id') == step_id)


def git(repo, *args):
    return subprocess.check_output(['git', '-C', str(repo), *args], text=True).strip()


def evaluate(repo, script, base):
    output = repo / 'output.txt'
    output.unlink(missing_ok=True)
    script = script.replace("${{ github.event.pull_request.base.sha }}", base)
    script = script.replace("${{ github.event.before }}", base)
    script = script.replace("${{ github.event_name }}", 'push')
    script = script.replace("${{ github.ref }}", 'refs/heads/main')
    script_path = repo / 'scope.ps1'
    script_path.write_text(script, encoding='utf-8')
    result = subprocess.run(['pwsh', '-NoProfile', '-File', str(script_path)], cwd=repo,
                            env=dict(os.environ, GITHUB_OUTPUT=str(output)),
                            capture_output=True, text=True)
    return result.returncode, output.read_text(encoding='utf-8').strip() if output.exists() else '', result.stderr


def main():
    container = scope_step('ci.yml', 'template-slices', 'container_scope')
    release = scope_step('release.yml', 'candidate', 'scope')
    cases = [
        ('container moved out', 'template/Dockerfile', 'deploy/Dockerfile', True, False),
        ('deployment moved out', 'template/deploy/compose.yml', 'docs/compose.yml', True, False),
        ('source moved out', 'framework/components/Foo.cs', 'docs/Foo.cs', False, True),
        ('package docs moved out', 'framework/docs/foo.md', 'docs/foo.md', False, True),
        ('container deleted', 'template/Dockerfile', None, True, False),
        ('source deleted', 'framework/components/Foo.cs', None, False, True),
        ('same directory rename', 'framework/components/Foo.cs', 'framework/components/Bar.cs', False, True),
        ('unrelated docs', 'docs/foo.md', 'docs/bar.md', False, False),
    ]
    with tempfile.TemporaryDirectory(prefix='leistd-scope-') as directory:
        for number, (name, source, target, ci_needed, release_needed) in enumerate(cases):
            repo = Path(directory) / str(number)
            repo.mkdir()
            git(repo, 'init', '-q')
            git(repo, 'config', 'user.email', 'scope-test@example.invalid')
            git(repo, 'config', 'user.name', 'Scope test')
            # Force rename detection to exercise the original blind spot.
            git(repo, 'config', 'diff.renames', 'true')
            path = repo / source
            path.parent.mkdir(parents=True)
            path.write_text('scope fixture\n' * 20, encoding='utf-8')
            git(repo, 'add', source)
            git(repo, 'commit', '-qm', 'fixture base')
            base = git(repo, 'rev-parse', 'HEAD')
            git(repo, 'update-ref', 'refs/remotes/origin/main', base)
            if target:
                (repo / target).parent.mkdir(parents=True, exist_ok=True)
                git(repo, 'mv', source, target)
            else:
                git(repo, 'rm', source)
            git(repo, 'commit', '-qm', 'fixture change')
            # A later docs-only commit must not hide the first commit's impact.
            (repo / 'note.txt').write_text('later commit\n', encoding='utf-8')
            git(repo, 'add', 'note.txt')
            git(repo, 'commit', '-qm', 'second change')
            for label, script, expected in [('CI', container, ci_needed), ('Release', release, release_needed)]:
                code, output, error = evaluate(repo, script, base)
                assert code == 0 and output == f'needed={str(expected).lower()}', (name, label, code, output, error)
                if name in ('container moved out', 'source moved out', 'package docs moved out') and expected:
                    code, output, _ = evaluate(repo, script.replace('--no-renames ', ''), base)
                    assert code == 0 and output == 'needed=false', ('original blind spot not reproduced', name, label)
            # Dispatch/reusable CI has no PR base and must compare against merge-base.
            code, output, error = evaluate(repo, container, '')
            assert code == 0 and output == f'needed={str(ci_needed).lower()}', (name, 'merge-base fallback', code, output, error)
            # Missing commits: CI conservatively checks containers; Release fails closed.
            code, output, error = evaluate(repo, container, '1' * 40)
            assert code == 0 and output == 'needed=true', (name, 'unknown CI base', error)
            code, _, _ = evaluate(repo, release, '1' * 40)
            assert code != 0, (name, 'unknown release base')
            code, _, _ = evaluate(repo, release, '0' * 40)
            assert code != 0, (name, 'zero release base')
            # Equal merge-base/HEAD cannot establish a useful comparison: fail conservatively.
            git(repo, 'update-ref', 'refs/remotes/origin/main', git(repo, 'rev-parse', 'HEAD'))
            code, output, error = evaluate(repo, container, '')
            assert code == 0 and output == 'needed=true', (name, 'merge-base equals HEAD', code, output, error)
            print(f'PASS {name}: both workflows, multi-commit, merge-base and missing-base cases')


if __name__ == '__main__':
    main()
