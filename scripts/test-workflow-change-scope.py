#!/usr/bin/env python3
"""Exercise the actual workflow scope steps in isolated Git repositories.

Run when changing CI/release scope selection, framework test receipts or the release link policy
(PackageReleaseNotes and the upgrade-guide link); this is not a per-feature gate.
The release link check packs a fixture project, so it also requires the .NET SDK.
Requires PowerShell and the same PyYAML dependency used by Skill validation.
"""

import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
from xml.etree import ElementTree
import zipfile

import yaml


ROOT = Path(__file__).resolve().parents[1]


def scope_step(workflow, job, step_id):
    data = yaml.safe_load((ROOT / '.github/workflows' / workflow).read_text(encoding='utf-8'))
    return next(s['run'] for s in data['jobs'][job]['steps'] if s.get('id') == step_id)


def git(repo, *args):
    return subprocess.check_output(['git', '-C', str(repo), *args], text=True, encoding='utf-8', errors='replace').strip()


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
                            capture_output=True, text=True, encoding='utf-8', errors='replace')
    return result.returncode, output.read_text(encoding='utf-8').strip() if output.exists() else '', result.stderr


def check_existing_scopes():
    container = scope_step('ci.yml', 'framework-pack', 'container_scope')
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
                path.write_text(path.read_text(encoding='utf-8') + 'change\n', encoding='utf-8')
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
        actual_git = subprocess.check_output(['which', 'git'], text=True, encoding='utf-8', errors='replace').strip()
        shim = fake_bin / 'git'
        shim.write_text('#!/bin/sh\ncase " $* " in *" diff "*) exit 23;; esac\nexec "' + actual_git + '" "$@"\n', encoding='utf-8')
        shim.chmod(0o755)
        code, output, error = evaluate(repo, script, parent, 'pull_request', extra_env={'PATH': str(fake_bin) + os.pathsep + os.environ['PATH']})
        assert code == 0 and output == 'docs_only=false', ('failed diff', code, output, error)
        print('PASS empty and failed diff fall back to full')
        # Model GitHub's actual PR merge checkout: base is the first parent.
        merge = subprocess.check_output(['git', '-C', str(repo), 'commit-tree', 'HEAD^{tree}', '-p', base, '-p', 'HEAD'],
                                        input='PR merge fixture\n', text=True, encoding='utf-8', errors='replace').strip()
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
    dynamic = ['test', 'template-slices', 'template-generation', 'package-consumption', 'postgresql-e2e', 'oidc-e2e']
    required = ['framework-pack', 'docs-sync', 'docs-sync-windows', 'frontend-gates', *dynamic]
    assert set(quality['needs']) == set(required), 'aggregation must wait for every required result'
    assert quality['if'] == 'always()', 'aggregation must run after failure/skip/cancellation'
    assert 'needs' not in jobs['framework-pack'], 'packing must start without waiting for another runner'
    assert 'template-slice-plan' not in jobs, 'no redundant planning runner'
    for job in dynamic:
        assert jobs[job]['needs'] == 'framework-pack', ('scope dependency missing', job)
        assert "needs.framework-pack.outputs.docs_only == 'false'" in jobs[job]['if'], ('scope guard missing', job)
    assert 'FrameworkTests' in jobs['test']['if'], 'template-only must not run unchanged framework tests'
    pack = jobs['framework-pack']
    assert pack['steps'][0]['with']['fetch-depth'] == "${{ github.event_name == 'pull_request' && !inputs.candidate_sha && 2 || 0 }}", 'PR keeps two parents; dispatch needs complete main comparison'
    for step in pack['steps']:
        if step.get('uses', '').startswith(('actions/setup-dotnet@', 'actions/upload-artifact@')) or step.get('name') == '打包当前 Framework':
            assert step['if'] == "steps.scope.outputs.docs_only == 'false'", 'docs-only must not pack/upload'
    for job in jobs.values():
        for step in job['steps']:
            if step.get('uses', '').startswith('actions/checkout@'):
                assert step['with']['ref'] == '${{ inputs.candidate_sha || github.sha }}', 'candidate SHA differs across checks'
    for step in quality['steps'][1:]:
        assert step['if'] in ("steps.quality.outputs.dynamic == 'true'", "steps.quality.outputs.framework_tests == 'true'"), \
            'docs-only must not consume scene or framework test receipts'
    assert any(s.get('id') == 'framework_receipts' for s in quality['steps']), 'aggregation must verify framework test receipts'
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
            plan = dict(Version=2,ContainerSmoke=False, CandidateSha=candidate, Tier='pr', DocsOnly=docs_only == 'true',
                        Mode='frontend' if docs_only == 'false' and not framework_tests else 'full',
                        FrameworkTests=framework_tests, Scenarios=[] if docs_only == 'true' else ['identity'],
                        FrameworkTestProjects=[TEST_PROJECTS[0]] if framework_tests else [],
                        FrameworkTestSelection='affected' if framework_tests else 'none')
            normal['framework-pack']['outputs'] = dict(docs_only=docs_only, validation_plan=json.dumps(plan))
            for name in dynamic:
                normal[name]['result'] = 'skipped' if docs_only == 'true' or (name == 'test' and not framework_tests) else 'success'

            def check(state, success, variant=script):
                code, output, error = evaluate(repo, variant, '', extra_env={
                    'NEEDS_JSON': json.dumps(state), 'CANDIDATE_SHA': candidate, 'MATRIX_TIER': 'pr'})
                assert (code == 0) == success, (docs_only, framework_tests, state, code, output, error)
                if success:
                    assert output == (f"dynamic={'false' if docs_only == 'true' else 'true'}\n"
                                      f"framework_tests={str(framework_tests).lower()}"), output

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
            # The test list must agree with FrameworkTests and be a unique list of test projects.
            selected = 'affected' if framework_tests else 'none'
            lists = [('missing list', None, selected), ('string list', TEST_PROJECTS[0], selected),
                     ('unknown selection', plan['FrameworkTestProjects'], 'some'),
                     ('selection disagrees', plan['FrameworkTestProjects'], 'all' if not framework_tests else 'none'),
                     ('non-test project', ['framework/components/core/Leistd.Core/Leistd.Core.csproj'], selected),
                     ('duplicate project', [TEST_PROJECTS[0]] * 2, selected)]
            lists.append(('empty with responsibility', [], selected) if framework_tests else ('list without responsibility', [TEST_PROJECTS[0]], selected))
            for label, projects, selection in lists:
                invalid = dict(plan, FrameworkTestSelection=selection)
                if projects is None:
                    invalid.pop('FrameworkTestProjects')
                else:
                    invalid['FrameworkTestProjects'] = projects
                state = json.loads(json.dumps(normal)); state['framework-pack']['outputs']['validation_plan'] = json.dumps(invalid)
                check(state, False)
            if framework_tests:
                # A full-tier plan can never narrow framework tests.
                full = dict(plan, Tier='full')
                state = json.loads(json.dumps(normal)); state['framework-pack']['outputs']['validation_plan'] = json.dumps(full)
                code, _, error = evaluate(repo, script, '', extra_env={
                    'NEEDS_JSON': json.dumps(state), 'CANDIDATE_SHA': candidate, 'MATRIX_TIER': 'full'})
                assert code != 0 and '框架测试清单' in error, ('full tier accepted affected selection', error)
            # 变异：把 docs-sync 从必需清单里拿掉，失败的 docs-sync 就不再阻断——证明判据真的读这份清单
            mutated = script.replace("'framework-pack', 'docs-sync', ", "'framework-pack', ")
            assert mutated != script, 'mutation did not apply: required-job list literal changed'
            state = json.loads(json.dumps(normal)); state['docs-sync']['result'] = 'failure'
            check(state, True, mutated)
            print(f'PASS aggregation docs_only={docs_only}, framework_tests={framework_tests}: missing/failed/cancelled/wrong skip, wrong candidate/plan and static mutation')


TEST_PROJECTS = [
    'framework/tests/components/core/Leistd.Core.Tests/Leistd.Core.Tests.csproj',
    'framework/tests/components/email/Leistd.Email.Tests/Leistd.Email.Tests.csproj',
    'framework/tests/ddd-struct/Leistd.Ddd.Domain.Tests/Leistd.Ddd.Domain.Tests.csproj',
]


def check_framework_test_receipts():
    """Run the real test-list step with a fake dotnet, then the real aggregation check on its receipts."""
    run_step = next(s for s in yaml.safe_load((ROOT / '.github/workflows/ci.yml').read_text(encoding='utf-8'))['jobs']['test']['steps']
                    if s.get('id') == 'framework_tests')
    assert '${{ needs.framework-pack.outputs.validation_plan }}' == run_step['env']['VALIDATION_PLAN'], 'test list must come from the candidate plan'
    verify = scope_step('ci.yml', 'template-matrix', 'framework_receipts')
    with tempfile.TemporaryDirectory(prefix='leistd-framework-receipts-') as directory:
        repo = Path(directory) / 'repo'
        repo.mkdir()
        git(repo, 'init', '-q')
        git(repo, 'config', 'user.email', 'scope-test@example.invalid')
        git(repo, 'config', 'user.name', 'Scope test')
        for project in TEST_PROJECTS + ['framework/tests/shared/Leistd.TestBase/Leistd.TestBase.csproj']:
            (repo / project).parent.mkdir(parents=True)
            (repo / project).write_text('<Project />\n', encoding='utf-8')
        git(repo, 'add', '-A')
        git(repo, 'commit', '-qm', 'fixture')
        candidate = git(repo, 'rev-parse', 'HEAD')
        fake_bin = Path(directory) / 'fake-bin'
        fake_bin.mkdir()
        log = Path(directory) / 'dotnet.log'
        # Fake dotnet: record the call, fail exactly the project named by FAIL_PROJECT.
        shim = fake_bin / 'dotnet'
        shim.write_text('#!/bin/sh\necho "$*" >> "' + str(log) + '"\n'
                        '[ -n "$FAIL_PROJECT" ] && [ "$2" = "$FAIL_PROJECT" ] && exit 1\nexit 0\n', encoding='utf-8')
        shim.chmod(0o755)
        receipts = repo / '.tmp/framework-test-receipts'

        def plan(projects, selection='affected', candidate_sha=candidate):
            return dict(Version=2, CandidateSha=candidate_sha, BaseSha='b' * 40, FrameworkTests=True,
                        FrameworkTestProjects=projects, FrameworkTestSelection=selection, FrameworkTestReason='fixture')

        def run_tests(value, fail=''):
            for item in receipts.glob('*.json'):
                item.unlink()
            log.unlink(missing_ok=True)
            return evaluate(repo, run_step['run'], '', extra_env={
                'VALIDATION_PLAN': json.dumps(value), 'FAIL_PROJECT': fail,
                'PATH': str(fake_bin) + os.pathsep + os.environ['PATH']})

        def aggregate(value, success, reason=''):
            code, _, error = evaluate(repo, verify, '', extra_env={
                'VALIDATION_PLAN': json.dumps(value), 'CANDIDATE_SHA': candidate})
            assert (code == 0) == success, (value, code, error)
            assert success or reason in error, (reason, error)

        selected = TEST_PROJECTS[:2]
        code, _, error = run_tests(plan(selected))
        assert code == 0, error
        assert [line.split()[1] for line in log.read_text(encoding='utf-8').splitlines()] == selected, 'step must run exactly the list'
        assert sorted(item.name for item in receipts.glob('*.json')) == ['Leistd.Core.Tests.json', 'Leistd.Email.Tests.json']
        aggregate(plan(selected), True)
        print('PASS framework tests: the list runs exactly, one receipt per project, aggregation accepts')
        # One selected project not run (no receipt) is rejected, whichever one it is.
        for project in selected:
            name = Path(project).stem + '.json'
            saved = (receipts / name).read_text(encoding='utf-8')
            (receipts / name).unlink()
            aggregate(plan(selected), False, '缺少回执')
            (receipts / name).write_text(saved, encoding='utf-8')
        # A plan that grew after the run (receipts for only part of it) is rejected too.
        aggregate(plan(TEST_PROJECTS), False, '缺少回执')
        # A receipt outside the list, a duplicate, a failure claim or another candidate/base is illegal.
        original = json.loads((receipts / 'Leistd.Core.Tests.json').read_text(encoding='utf-8-sig'))
        for field, value in [('Project', TEST_PROJECTS[2]), ('Result', 'failure'), ('CandidateSha', 'c' * 40),
                             ('BaseSha', 'd' * 40), ('Version', 2)]:
            (receipts / 'extra.json').write_text(json.dumps(dict(original, **{field: value})), encoding='utf-8')
            aggregate(plan(selected), False, '非法框架测试回执')
        (receipts / 'extra.json').write_text(json.dumps(original), encoding='utf-8')
        aggregate(plan(selected), False, '非法框架测试回执')
        (receipts / 'extra.json').unlink()
        # A failing project leaves no receipt and fails the job; aggregation also rejects what remains.
        code, _, error = run_tests(plan(selected), fail=selected[1])
        assert code != 0 and '框架测试失败' in error, error
        assert sorted(item.name for item in receipts.glob('*.json')) == ['Leistd.Core.Tests.json']
        aggregate(plan(selected), False, '缺少回执')
        # The step refuses a list from another candidate or an empty list.
        for value in (plan(selected, candidate_sha='c' * 40), plan([])):
            code, _, error = run_tests(value)
            assert code != 0 and '测试清单与当前候选不符' in error and not log.exists(), error
        # A full selection must equal every registered test project, not a self-declared subset.
        code, _, error = run_tests(plan(TEST_PROJECTS, 'all'))
        assert code == 0, error
        aggregate(plan(TEST_PROJECTS, 'all'), True)
        code, _, error = run_tests(plan(selected, 'all'))
        assert code == 0, error
        aggregate(plan(selected, 'all'), False, '全集清单')
        print('PASS framework test receipts: missing, extra, duplicate, failed, foreign candidate/base and narrowed full set rejected')


def release_step(step_id):
    data = yaml.safe_load((ROOT / '.github/workflows/release.yml').read_text(encoding='utf-8'))
    return next(s for s in data['jobs']['release']['steps'] if s.get('id') == step_id)


def substitute(script, values):
    """Replace every ${{ expr }} with a fixture value; an unmodelled expression fails the test."""
    def replace(match):
        expression = match.group(1).strip()
        assert expression in values, ('workflow expression not modelled by the release fixture', expression)
        return values[expression]
    return re.sub(r'\$\{\{(.*?)\}\}', replace, script)


def run_pwsh(repo, script, env=None):
    output = repo / 'output.txt'
    output.unlink(missing_ok=True)
    path = repo.parent / f'{repo.name}-step.ps1'
    path.write_text(script, encoding='utf-8')
    result = subprocess.run(['pwsh', '-NoProfile', '-File', str(path)], cwd=repo,
                            env={**os.environ, 'GITHUB_OUTPUT': str(output), **(env or {})},
                            capture_output=True, text=True, encoding='utf-8', errors='replace')
    return result.returncode, output.read_text(encoding='utf-8') if output.exists() else '', result.stdout + result.stderr


def parse_outputs(text):
    outputs, lines, index = {}, text.splitlines(), 0
    while index < len(lines):
        line = lines[index]
        if '<<' in line and '=' not in line.split('<<', 1)[0]:
            name, marker = line.split('<<', 1)
            end = lines.index(marker, index + 1)
            outputs[name] = '\n'.join(lines[index + 1:end])
            index = end + 1
            continue
        name, _, value = line.partition('=')
        outputs[name] = value
        index += 1
    return outputs


def packed_release_notes(repo):
    packages = list((repo / 'framework/artifacts').glob('*.nupkg'))
    assert len(packages) == 1, ('expected exactly one fixture package', packages)
    with zipfile.ZipFile(packages[0]) as package:
        nuspec = next(name for name in package.namelist() if name.endswith('.nuspec'))
        root = ElementTree.fromstring(package.read(nuspec))
    notes = [element.text for element in root.iter() if element.tag.endswith('}releaseNotes') or element.tag == 'releaseNotes']
    return notes[0] if notes else None


def check_release_links():
    """Release link policy: every channel, with and without a guide, down to the packed .nuspec."""
    repository = 'zengqinglei/leistd-net'
    repo_url = f'https://github.com/{repository}'
    links, notes, pack = release_step('links'), release_step('notes'), release_step('pack')
    channels = {
        'stable': '0.13.0',
        'beta': '0.13.0-beta.7',
        'nightly': '0.13.0-preview.20261007.12',
    }
    with tempfile.TemporaryDirectory(prefix='leistd-release-links-') as directory:
        for channel, version in channels.items():
            for has_guide in (True, False):
                tag = f'v{version}'
                repo = Path(directory) / f'{channel}-{"guide" if has_guide else "none"}'
                repo.mkdir()
                git(repo, 'init', '-q')
                git(repo, 'config', 'user.email', 'release-test@example.invalid')
                git(repo, 'config', 'user.name', 'Release test')
                project = repo / 'framework/Fixture/Fixture.csproj'
                project.parent.mkdir(parents=True)
                project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                                   '<TargetFramework>net10.0</TargetFramework><PackageId>Leistd.ReleaseFixture</PackageId>'
                                   '<Authors>fixture</Authors><Description>fixture</Description>'
                                   '</PropertyGroup></Project>\n', encoding='utf-8')
                (project.parent / 'Fixture.cs').write_text('namespace Fixture;\npublic static class Marker;\n', encoding='utf-8')
                (repo / 'framework/Leistd.Framework.slnx').write_text(
                    '<Solution>\n  <Project Path="Fixture/Fixture.csproj" />\n</Solution>\n', encoding='utf-8')
                if has_guide:
                    guide = repo / 'docs/framework/upgrades/0.13.0.md'
                    guide.parent.mkdir(parents=True)
                    guide.write_text('# guide\n', encoding='utf-8')
                git(repo, 'add', '-A')
                git(repo, 'commit', '-qm', 'fix: fixture')
                values = {
                    'github.repository': repository,
                    'steps.ch.outputs.channel': channel,
                    'steps.ver.outputs.tag': tag,
                    'steps.ver.outputs.version': version,
                    'steps.ver.outputs.baseVersion': '0.13.0',
                    'steps.ver.outputs.lastStableTag': '',
                }
                guide_url = f'{repo_url}/blob/{tag}/docs/framework/upgrades/0.13.0.md'
                expected = guide_url if has_guide else {
                    'stable': f'{repo_url}/releases/tag/{tag}',
                    'beta': f'{repo_url}/releases/tag/{tag}',
                    'nightly': f'{repo_url}/commit/{tag}',
                }[channel]

                code, output, log = run_pwsh(repo, substitute(links['run'], values))
                assert code == 0, (channel, has_guide, log)
                outputs = parse_outputs(output)
                assert outputs.get('guideUrl') == (guide_url if has_guide else ''), (channel, has_guide, outputs)
                assert outputs.get('packageReleaseNotes') == expected, (channel, has_guide, outputs)
                # 变异：改成分支链接必须被拒，证明判据读的是 tag 固定的地址
                mutated = links['run'].replace('blob/$tag/', 'blob/develop/')
                assert mutated != links['run'], 'mutation did not apply: guide URL literal changed'
                code, output, _ = run_pwsh(repo, substitute(mutated, values))
                assert not has_guide or parse_outputs(output).get('packageReleaseNotes') != expected, 'branch link not detected'

                values['steps.links.outputs.guideUrl'] = outputs['guideUrl']
                code, output, log = run_pwsh(repo, substitute(notes['run'], values))
                assert code == 0, (channel, has_guide, 'notes', log)
                body = parse_outputs(output)['content']
                assert ('## 升级指南' in body) == has_guide and (guide_url in body) == has_guide, (channel, has_guide, body)

                values['steps.links.outputs.packageReleaseNotes'] = outputs['packageReleaseNotes']
                env = {name: substitute(value, values) for name, value in pack.get('env', {}).items()}
                code, _, log = run_pwsh(repo, substitute(pack['run'], values), env)
                assert code == 0, (channel, has_guide, 'pack', log)
                packed = packed_release_notes(repo)
                assert packed == expected, (channel, has_guide, 'nuspec', packed)
                assert f'/{tag}/' in packed + '/' and '/main/' not in packed and '/develop/' not in packed, packed
                print(f'PASS release links {channel} guide={has_guide}: {packed}')

        code, _, log = run_pwsh(repo, substitute(pack['run'], values), {'PACKAGE_RELEASE_NOTES': ''})
        assert code != 0, ('pack must refuse a missing release notes link', log)
        values['steps.ch.outputs.channel'] = 'unknown'
        code, _, _ = run_pwsh(repo, substitute(links['run'], values))
        assert code != 0, 'unknown channel must fail closed'
        print('PASS release links: missing link and unknown channel fail closed')


def main():
    check_existing_scopes()
    check_docs_scope()
    check_quality_aggregation()
    check_framework_test_receipts()
    check_release_links()


if __name__ == '__main__':
    main()
