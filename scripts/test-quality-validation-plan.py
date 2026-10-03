#!/usr/bin/env python3
"""Maintain selection/receipt contracts with real Git inputs and template generation.

Not a per-feature gate. Logs and generation evidence stay under .tmp.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import time
import sys

sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / '.tmp/quality-plan-evidence')
    args = parser.parse_args()
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=True)
    records = []

    def run(label, command, cwd, success=True):
        started = time.monotonic()
        result = subprocess.run(command, cwd=cwd, text=True, capture_output=True)
        (out / (label + '.log')).write_text(result.stdout + result.stderr, encoding='utf-8')
        records.append(dict(label=label, seconds=round(time.monotonic() - started, 3), exit_code=result.returncode))
        (out / 'results.json').write_text(json.dumps(records, indent=2))
        assert (result.returncode == 0) == success, (label, result.stdout[-1000:], result.stderr[-1500:])
        return result.stdout

    with tempfile.TemporaryDirectory(prefix='leistd-plan-') as directory:
        repo = Path(directory) / 'repo'
        repo.mkdir()
        paths = subprocess.check_output(['git', 'ls-files', '--cached', '--others', '--exclude-standard', '-z'], cwd=ROOT).decode().split('\0')
        for path in filter(None, paths):
            source = ROOT / path
            if source.is_file():
                target = repo / path
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(source, target)
        def git(*args):
            return subprocess.check_output(['git', *args], cwd=repo, text=True, stderr=subprocess.PIPE).strip()
        git('init', '-q'); git('config', 'user.name', 'Quality fixture'); git('config', 'user.email', 'fixture@example.invalid')
        git('add', '.'); git('commit', '-qm', 'fixture baseline')
        base = git('rev-parse', 'HEAD')
        spec = importlib.util.spec_from_file_location('plan', repo / 'scripts/plan-quality-checks.py')
        planner = importlib.util.module_from_spec(spec); spec.loader.exec_module(planner)
        scenarios = planner.coverage.load_scenarios()
        registered = {name for name, info in scenarios.items() if 'pr' in info['Slices']}
        front = 'template/frontend/src/app/layout/components/notifications/notification-service.ts'
        back = 'template/backend/src/CompanyName.ProjectName.Api/Controllers/AuthController.cs'
        fw = next(path for path in paths if path.startswith('framework/components/email/Leistd.Email.Smtp/') and path.endswith('.cs'))
        cases = [
            ('frontend-feature', [front], 'frontend'), ('backend-api', [back], 'backend'),
            ('cross-layer', [front, back], 'full'), ('frontend-lock', ['template/frontend/package-lock.json'], 'full'),
            ('template-parameters', ['template/.template.config/template.json'], 'full'),
            ('packaged-doc', ['framework/docs/components/email.md'], 'full'),
            ('generated-doc', ['template/docs/standards/testing.md'], 'full'),
            ('workflow', ['.github/workflows/ci.yml'], 'full'), ('unknown', ['unknown-input.cs'], 'full'),
            ('framework-source', [fw], 'full'),
            ('localized-frontend', ['template/.template.config/localization/frontend/src/app/app.spec.ts'], 'frontend'),
        ]
        for label, changed, expected_mode in cases:
            git('reset', '--hard', base)
            for path in changed:
                target = repo / path; target.parent.mkdir(parents=True, exist_ok=True)
                # No need for valid source in a classifier-only fixture; actual
                # generation uses the first two cases and their valid comments.
                target.write_text((target.read_text() if target.exists() else '') + '\n// Quality scope fixture\n')
            git('add', '.'); git('commit', '-qm', label)
            plan = planner.create_plan('pr', base, 'pull_request', '')
            assert plan['Mode'] == expected_mode, (label, plan)
            if expected_mode != 'full':
                assert plan['FrameworkTests'] is False and plan['ConsumerProjects'] == []
                assert {'identity', 'identity-all-features'} <= set(plan['Scenarios']) <= registered
            elif label == 'framework-source':
                assert plan['ConsumerProjects'] == ['Leistd.Email.Smtp'] and plan['FrameworkTests']
            else:
                assert plan['ConsumerProjects'] is None and plan['FrameworkTests'] and set(plan['Scenarios']) == registered
            for tier, event, baseline, candidate in [('full','pull_request',base,''), ('pr','workflow_dispatch',base,''),
                ('pr','pull_request','0'*40,''), ('pr','pull_request',base,'b'*40), ('pr','pull_request',git('rev-parse','HEAD'),'')]:
                conservative = planner.create_plan(tier, baseline, event, candidate)
                assert conservative['Mode'] == 'full' and conservative['FrameworkTests'] and conservative['ConsumerProjects'] is None
            (out / (label + '-plan.json')).write_text(json.dumps(plan, indent=2))
            print('PASS complete-input plan:', label, flush=True)
            if label in ('frontend-feature', 'backend-api'):
                prove_generation(repo, base, plan, scenarios, out, run, git)
                prove_receipts(repo, plan, scenarios, out, run)
        # Deletion, move across boundaries and later docs commit must keep the
        # original source impact. Evaluate old/new paths, never just HEAD^.
        git('reset', '--hard', base); git('rm', front); git('commit', '-qm', 'delete frontend input')
        assert planner.create_plan('pr',base,'pull_request','')['Mode'] == 'frontend'
        git('reset', '--hard', base); git('mv',front,'docs/moved-source.ts'); git('commit','-qm','cross boundary move')
        (repo / 'docs/later.md').write_text('later documentation')
        git('add','.'); git('commit','-qm','later doc change')
        assert planner.create_plan('pr',base,'pull_request','')['Mode'] == 'full'
        git('reset', '--hard', base)
        (repo / front).write_text((repo / front).read_text() + '\n// Uncommitted edit\n')
        dirty = planner.create_plan('pr', base, 'pull_request', '')
        assert dirty['Mode'] == 'full' and dirty['FrameworkTests'] and dirty['ConsumerProjects'] is None
        git('reset', '--hard', base)
        print('PASS deletion, cross-boundary rename and multi-commit conservative fallback', flush=True)
    print('Selection and receipt evidence:', out, flush=True)


def prove_generation(repo, base, plan, scenarios, out, run, git):
    """Actually generate every PR product on both sides and compare stage inputs.

    Only generated UserSecretsId values are normalized: the template explicitly
    randomizes that property at creation, while its XML structure stays checked.
    """
    import io
    import tarfile
    label = plan['Mode']
    old = out / (label + '-base-template')
    old.mkdir(exist_ok=True)
    data = subprocess.check_output(['git','archive',base,'template'],cwd=repo)
    with tarfile.open(fileobj=io.BytesIO(data)) as archive:
        # Locally produced git archive, supporting the repository's Python 3.9
        # machines as well as CI 3.12. Reject any unexpected archive path.
        assert all(member.name.startswith('template/') or member.name == 'template' for member in archive.getmembers())
        archive.extractall(old)
    digests = {}
    for version, template in [('base', old / 'template'), ('head', repo / 'template')]:
        hive = out / f'{label}-{version}-hive'
        run(f'{label}-{version}-install',['dotnet','new','--debug:custom-hive',str(hive),'install',str(template),'--force'],repo)
        for name, info in scenarios.items():
            if 'pr' not in info['Slices']: continue
            target = out / 'generated' / label / version / name
            run(f'{label}-{version}-{name}', ['dotnet','new','--debug:custom-hive',str(hive),'fullstack-app','-n','Quality.Scope','-o',str(target),'--force',*info['Arguments']],repo)
            files = {}
            for path in sorted(target.rglob('*')):
                if not path.is_file(): continue
                content = path.read_bytes()
                if path.suffix == '.csproj':
                    content = re.sub(rb'<UserSecretsId>[^<]+</UserSecretsId>',b'<UserSecretsId>GENERATED</UserSecretsId>',content)
                files[path.relative_to(target).as_posix()] = hashlib.sha256(content).hexdigest()
            digests[version,name] = files
    omitted = 'backend/' if label == 'frontend' else 'frontend/'
    selected = set(plan['Scenarios'])
    for name in {name for version,name in digests}:
        old_files, new_files = digests['base',name],digests['head',name]
        delta = {path for path in old_files.keys() | new_files.keys() if old_files.get(path) != new_files.get(path)}
        assert not any(path.startswith(omitted) for path in delta), (label,name,'omitted stage input changed',delta)
        assert name in selected or not delta, (label,name,'omitted scenario output changed',delta)
    (out / (label + '-generation-digests.json')).write_text(json.dumps({f'{version}/{name}': files for (version,name),files in digests.items()},indent=2))
    print('PASS actual generation: omitted inputs and unselected products unchanged:', label, flush=True)


def prove_receipts(repo, plan, scenarios, out, run):
    label = plan['Mode']
    expected_file = out / (label + '-expected.json')
    expected_file.write_text(json.dumps(plan))
    receipt_dir = out / (label + '-receipts'); receipt_dir.mkdir(exist_ok=True)
    receipts = {}
    for name in plan['Scenarios']:
        slice_name = scenarios[name]['Slices']['pr']
        receipt = receipts.setdefault(slice_name,dict(Tier='pr',Slice=slice_name,CandidateSha=plan['CandidateSha'],Mode=label,Results=[]))
        result = dict(Scenario=name, Container='skipped')
        for stage in ['Backend','Runtime','Lint','Frontend','Test']:
            omitted = (label == 'frontend' and stage in ['Backend','Runtime']) or (label == 'backend' and stage in ['Lint','Frontend','Test'])
            result[stage] = 'not-applicable' if omitted else 'pass'
        receipt['Results'].append(result)
    def write():
        for key, receipt in receipts.items(): (receipt_dir / f'matrix-{key}.json').write_text(json.dumps(receipt))
    write()
    command=['pwsh','-NoProfile','-File','scripts/check-template-matrix-results.ps1','-ResultsPath',str(receipt_dir),'-Tier','pr','-ValidationPlanPath',str(expected_file)]
    run(label+'-receipts-valid',command,repo)
    first = next(iter(receipts.values()))
    for field,bad in [('CandidateSha','b'*40),('Mode','full'),('Tier','full')]:
        saved=first[field];first[field]=bad;write();run(label+'-reject-'+field,command,repo,False);first[field]=saved
    result=first['Results'][0]
    for stage in ['Backend','Runtime','Lint','Frontend','Test']:
        saved=result[stage]
        for bad in ['skipped','failure','', 'pass' if saved=='not-applicable' else 'not-applicable']:
            result[stage]=bad;write();run(f'{label}-reject-{stage}-{bad or "missing"}',command,repo,False)
        result[stage]=saved
    saved=first['Results'];first['Results']=saved[:-1];write();run(label+'-reject-missing-scenario',command,repo,False);first['Results']=saved
    write();run(label+'-receipts-restored',command,repo)
    # Old full checker/entry must reject manual/claimed skips too.
    run(label+'-full-contract-rejects-narrowed',command[:-2],repo,False)


if __name__ == '__main__': main()
