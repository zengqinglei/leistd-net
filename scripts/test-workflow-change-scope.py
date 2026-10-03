#!/usr/bin/env python3
"""Exercise the actual workflow scope steps in isolated Git repositories.

Run when changing CI/release scope selection; this is not a per-feature gate.
Requires PowerShell and the same PyYAML dependency used by Skill validation.
"""

import json
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


def evaluate(repo, script, base, event='push', candidate='', extra_env=None):
    output = repo / 'output.txt'
    output.unlink(missing_ok=True)
    script = script.replace("${{ github.event.pull_request.base.sha }}", base)
    script = script.replace("${{ github.event.before }}", base)
    script = script.replace("${{ github.event_name }}", event)
    script = script.replace("${{ github.ref }}", 'refs/heads/main')
    bash = script.startswith(("python - <<", "python3 - <<"))
    script_path = repo / ('scope.sh' if bash else 'scope.ps1')
    script_path.write_text(script, encoding='utf-8')
    command = ['bash', '-e', '-o', 'pipefail', str(script_path)] if bash else ['pwsh', '-NoProfile', '-File', str(script_path)]
    result = subprocess.run(command, cwd=repo,
                            env={**dict(os.environ, GITHUB_OUTPUT=str(output),
                                     GITHUB_STEP_SUMMARY=str(repo / 'summary.txt'),
                                     PR_BASE_SHA=base, EVENT_NAME=event, CANDIDATE_SHA=candidate),
                                 **(extra_env or {})},
                            capture_output=True, text=True)
    return result.returncode, output.read_text(encoding='utf-8').strip() if output.exists() else '', result.stderr


def check_existing_scopes():
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
            path.parent.mkdir(parents=True, exist_ok=True)
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


def check_docs_scope():
    script = scope_step('ci.yml', 'framework-pack', 'scope')
    cases = [
        ('root readme', 'README.md', 'README.md', True),
        ('internal docs', 'docs/foo.md', 'docs/bar.md', True),
        ('repository skill', '.agents/skills/foo/SKILL.md', '.agents/skills/bar/SKILL.md', True),
        ('distributed skill', 'skills/foo/SKILL.md', 'skills/bar/SKILL.md', True),
        ('package docs', 'framework/docs/foo.md', 'framework/docs/bar.md', False),
        ('generated docs', 'template/docs/foo.md', 'template/docs/bar.md', False),
        ('generated skill', 'template/.agents/foo.md', 'template/.agents/bar.md', False),
        ('package docs moved out', 'framework/docs/foo.md', 'docs/foo.md', False),
        ('docs moved into package', 'docs/foo.md', 'framework/docs/foo.md', False),
        ('deleted code', 'framework/components/Foo.cs', None, False),
        ('workflow input', '.github/workflows/foo.yml', '.github/workflows/bar.yml', False),
        ('script input', 'scripts/foo.py', 'scripts/bar.py', False),
        ('root build input', 'Directory.Build.targets', 'Directory.Build.targets', False),
        ('unknown path', 'unknown.md', 'unknown.md', False),
        ('case sensitive readme', 'readme.md', 'readme.md', False),
        ('case sensitive directory', 'Docs/foo.md', 'Docs/bar.md', False),
        ('leading whitespace boundary', ' docs/foo.md', ' docs/bar.md', False),
        ('leading whitespace single file', ' README.md', ' README.md', False),
        ('unicode documentation', 'docs/说明.md', 'docs/规范.md', True),
    ]
    with tempfile.TemporaryDirectory(prefix='leistd-docs-scope-') as directory:
        for number, (name, source, target, expected) in enumerate(cases):
            repo = Path(directory) / str(number)
            repo.mkdir()
            git(repo, 'init', '-q')
            git(repo, 'config', 'user.email', 'scope-test@example.invalid')
            git(repo, 'config', 'user.name', 'Scope test')
            git(repo, 'config', 'diff.renames', 'true')
            path = repo / source
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('scope fixture\n' * 20, encoding='utf-8')
            git(repo, 'add', source)
            git(repo, 'commit', '-qm', 'base')
            base = git(repo, 'rev-parse', 'HEAD')
            if target == source:
                path.write_text(path.read_text() + 'change\n', encoding='utf-8')
                git(repo, 'add', source)
            elif target:
                (repo / target).parent.mkdir(parents=True, exist_ok=True)
                git(repo, 'mv', source, target)
            else:
                git(repo, 'rm', source)
            git(repo, 'commit', '-qm', 'first change')
            (repo / 'docs').mkdir(exist_ok=True)
            (repo / 'docs/note.md').write_text('later docs-only change\n', encoding='utf-8')
            git(repo, 'add', 'docs/note.md')
            git(repo, 'commit', '-qm', 'second change')

            def expect(base_value=base, event='pull_request', candidate='', wanted=expected, variant=script, extra_env=None):
                code, output, error = evaluate(repo, variant, base_value, event, candidate, extra_env)
                assert code == 0 and output == f'docs_only={str(wanted).lower()}', (name, event, base_value, code, output, error)

            expect()
            for bad_base in ('', '0' * 40, '1' * 40, git(repo, 'rev-parse', 'HEAD')):
                expect(bad_base, wanted=False)
            for event in ('workflow_dispatch', 'workflow_call', 'push', 'schedule'):
                expect(event=event, wanted=False)
            # A called workflow inherits its caller's event; explicit candidate still forces full.
            expect(candidate=git(repo, 'rev-parse', 'HEAD'), wanted=False)
            if name == 'package docs moved out':
                expect(wanted=True, variant=script.replace("'--no-renames', ", ''))
            print(f'PASS docs scope {name}: complete diff, unknown base, non-PR and reusable caller')

        # A different commit with the same tree has no changed paths: remain conservative.
        git(repo, 'commit', '--allow-empty', '-qm', 'empty change')
        parent = git(repo, 'rev-parse', 'HEAD^')
        code, output, error = evaluate(repo, script, parent, 'pull_request')
        assert code == 0 and output == 'docs_only=false', ('empty diff', code, output, error)
        # Force git diff to fail after a valid base lookup, rather than merely use an unknown SHA.
        fake_bin = repo / 'fake-bin'
        fake_bin.mkdir()
        actual_git = subprocess.check_output(['which', 'git'], text=True).strip()
        shim = fake_bin / 'git'
        shim.write_text('#!/bin/sh\ncase " $* " in *" diff "*) exit 23;; esac\nexec "' + actual_git + '" "$@"\n')
        shim.chmod(0o755)
        code, output, error = evaluate(repo, script, parent, 'pull_request', extra_env={'PATH': str(fake_bin) + os.pathsep + os.environ['PATH']})
        assert code == 0 and output == 'docs_only=false', ('failed diff', code, output, error)
        print('PASS empty and failed diff fall back to full')
        # Model GitHub's actual PR merge checkout: base is the first parent.
        merge = subprocess.check_output(['git', '-C', str(repo), 'commit-tree', 'HEAD^{tree}', '-p', base, '-p', 'HEAD'],
                                        input='PR merge fixture\n', text=True).strip()
        git(repo, 'update-ref', 'refs/heads/pr-merge', merge)
        git(repo, 'symbolic-ref', 'HEAD', 'refs/heads/pr-merge')
        for depth in (1, 2):
            clone = Path(directory) / f'shallow-{depth}'
            subprocess.run(['git', 'clone', '-q', '--depth', str(depth), repo.as_uri(), str(clone)], check=True)
            present = subprocess.run(['git', '-C', str(clone), 'cat-file', '-e', f'{base}^{{commit}}'],
                                     capture_output=True).returncode == 0
            assert present == (depth == 2), ('unexpected shallow baseline availability', depth)
            code, output, error = evaluate(clone, script, base, 'pull_request')
            assert code == 0 and output == 'docs_only=true', ('shallow PR baseline', depth, code, output, error)
            assert subprocess.run(['git', '-C', str(clone), 'cat-file', '-e', f'{base}^{{commit}}'], capture_output=True).returncode == 0
            print(f'PASS shallow PR checkout depth={depth}: baseline {"available directly" if present else "fetched conservatively"}')


def check_quality_aggregation():
    workflow = yaml.safe_load((ROOT / '.github/workflows/ci.yml').read_text(encoding='utf-8'))
    jobs = workflow['jobs']
    quality = jobs['template-matrix']
    dynamic = ['test', 'template-slices', 'package-consumption', 'postgresql-e2e', 'oidc-e2e']
    required = ['framework-pack', 'docs-sync', *dynamic]
    assert set(quality['needs']) == set(required), 'aggregation must wait for every required result'
    assert quality['if'] == 'always()', 'aggregation must run after failure/skip/cancellation'
    assert 'needs' not in jobs['framework-pack'], 'packing must start without waiting for another runner'
    assert 'template-slice-plan' not in jobs, 'no redundant planning runner'
    for job in dynamic:
        assert jobs[job]['needs'] == 'framework-pack', ('scope dependency missing', job)
        assert "needs.framework-pack.outputs.docs_only == 'false'" in jobs[job]['if'], ('scope guard missing', job)
    assert 'FrameworkTests' in jobs['test']['if'], 'template-only must not run unchanged framework tests'
    pack = jobs['framework-pack']
    assert pack['steps'][0]['with']['fetch-depth'] == 2, 'PR merge baseline should be locally available'
    for step in pack['steps']:
        if step.get('uses', '').startswith(('actions/setup-dotnet@', 'actions/upload-artifact@')) or step.get('name') == '打包当前 Framework':
            assert step['if'] == "steps.scope.outputs.docs_only == 'false'", 'docs-only must not pack/upload'
    for job in jobs.values():
        for step in job['steps']:
            if step.get('uses', '').startswith('actions/checkout@'):
                assert step['with']['ref'] == '${{ inputs.candidate_sha || github.sha }}', 'candidate SHA differs across checks'
    for step in quality['steps'][1:]:
        assert step['if'] == "steps.quality.outputs.dynamic == 'true'", 'docs-only must not consume scene receipts'
    matrix_step = next(s for s in jobs['template-slices']['steps'] if s.get('id') == 'matrix')
    assert '-SkipSourcePreflight' in matrix_step['run'], 'same-candidate preflight dedup missing'
    static_step = next(s for s in jobs['docs-sync']['steps'] if 'run' in s and 'check-all' in s['run'])
    assert static_step['run'] == 'pwsh scripts/check-all.ps1', 'replacement must execute the complete static entry'
    script = scope_step('ci.yml', 'template-matrix', 'quality')
    with tempfile.TemporaryDirectory(prefix='leistd-quality-') as directory:
        repo = Path(directory)
        candidate = 'a' * 40
        for docs_only, framework_tests in [('true', False), ('false', True), ('false', False)]:
            normal = {name: {'result': 'success', 'outputs': {}} for name in required}
            plan = dict(Version=1, CandidateSha=candidate, Tier='pr', DocsOnly=docs_only == 'true',
                        Mode='frontend' if docs_only == 'false' and not framework_tests else 'full',
                        FrameworkTests=framework_tests, Scenarios=[] if docs_only == 'true' else ['identity'])
            normal['framework-pack']['outputs'] = dict(docs_only=docs_only, validation_plan=json.dumps(plan))
            for name in dynamic:
                normal[name]['result'] = 'skipped' if docs_only == 'true' or (name == 'test' and not framework_tests) else 'success'

            def check(state, success, variant=script):
                code, output, error = evaluate(repo, variant, '', extra_env={
                    'NEEDS_JSON': json.dumps(state), 'CANDIDATE_SHA': candidate, 'MATRIX_TIER': 'pr'})
                assert (code == 0) == success, (docs_only, framework_tests, state, code, output, error)
                if success:
                    assert output == f"dynamic={'false' if docs_only == 'true' else 'true'}", output

            check(normal, True)
            for name in required:
                expected = normal[name]['result']
                for bad in ('failure', 'cancelled', '', 'success' if expected == 'skipped' else 'skipped'):
                    state = json.loads(json.dumps(normal)); state[name]['result'] = bad
                    check(state, False)
                state = json.loads(json.dumps(normal)); del state[name]
                check(state, False)
            for value in ('', 'TRUE', None, 'unknown'):
                state = json.loads(json.dumps(normal)); state['framework-pack']['outputs']['docs_only'] = value
                check(state, False)
            for field, value in [('CandidateSha', 'b' * 40), ('Tier', 'full'), ('FrameworkTests', 'false'),
                                 ('Mode', 'unknown'), ('Version', 9), ('DocsOnly', not plan['DocsOnly'])]:
                invalid = dict(plan); invalid[field] = value
                state = json.loads(json.dumps(normal)); state['framework-pack']['outputs']['validation_plan'] = json.dumps(invalid)
                check(state, False)
            state = json.loads(json.dumps(normal)); state['framework-pack']['outputs'].pop('validation_plan')
            check(state, False)
            state = json.loads(json.dumps(normal)); state['docs-sync']['result'] = 'failure'
            check(state, True, script.replace("@('framework-pack', 'docs-sync')", "@('framework-pack')"))
            print(f'PASS aggregation docs_only={docs_only}, framework_tests={framework_tests}: missing/failed/cancelled/wrong skip, wrong candidate/plan and static mutation')


def main():
    check_existing_scopes()
    check_docs_scope()
    check_quality_aggregation()


if __name__ == '__main__':
    main()
