#!/usr/bin/env python3
"""实际生成全部有效形态，验证原始输入映射、裁剪不变量及角色无效参数等价。"""
from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import importlib.util
import itertools
import json
from pathlib import Path
import posixpath
import re
import shutil
import subprocess
import tempfile
import threading
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location('scenario_coverage', ROOT / 'scripts/check-template-scenario-coverage.py')
model = importlib.util.module_from_spec(spec)
spec.loader.exec_module(model)


def cli_arguments(config, values):
    args = []
    for name, symbol in config['symbols'].items():
        if symbol.get('type') != 'parameter' or symbol.get('datatype') not in ('bool', 'choice'):
            continue
        args += ['--' + re.sub(r'(?<!^)(?=[A-Z])', '-', name).lower(), str(values[name]).lower() if isinstance(values[name], bool) else values[name]]
    return args


def normalize(content, config):
    # Only template-declared random GUID substitutions are ignored; business GUIDs remain evidence.
    for original in config.get('guids', []):
        content = re.sub(rb'(<UserSecretsId>)[^<]+(</UserSecretsId>)', rb'\1TEMPLATE-GUID\2', content)
    return content


def validate_relative_modules(output, files):
    for relative in files:
        if not relative.endswith('.ts'):
            continue
        source = (output / relative).read_text(encoding='utf-8')
        for module in re.findall(r"(?:\bfrom\s*|\bimport\s*\(\s*|\bimport\s*)['\"](\.[^'\"]+)['\"]", source):
            stem = posixpath.normpath(posixpath.join(posixpath.dirname(relative), module))
            candidates = [stem, stem + '.ts', stem + '.json', stem + '/index.ts']
            if stem.endswith('.js'):
                candidates.append(stem[:-3] + '.ts')
            assert any(candidate in files for candidate in candidates), f'Missing relative module: {relative} -> {module}'
        for asset in re.findall(r"\b(?:templateUrl|styleUrl)\s*:\s*['\"]([^'\"]+)['\"]", source):
            target = posixpath.normpath(posixpath.join(posixpath.dirname(relative), asset))
            assert target in files, f'Missing component asset: {relative} -> {asset}'


def translation_keys(node, prefix=''):
    keys = {}
    for key, value in node.items():
        name = f'{prefix}.{key}' if prefix else key
        if isinstance(value, dict):
            keys.update(translation_keys(value, name))
        else:
            keys[name] = value
    return keys


def validate(output, values, config):
    digests = {}
    for path in sorted(output.rglob('*')):
        if not path.is_file():
            continue
        content = path.read_bytes()
        relative = path.relative_to(output).as_posix()
        digests[relative] = hashlib.sha256(normalize(content, config)).hexdigest()
        if path.suffix == '.csproj':
            ElementTree.fromstring(content)
        if path.suffix in ('.cs', '.ts'):
            assert content.strip(), f'Empty source: {relative}'
        if path.name in ('package.json', 'package-lock.json', 'angular.json'):
            json.loads(content)
        if relative.startswith('frontend/public/i18n/') and path.suffix == '.json':
            json.loads(content)
        if path.suffix in ('.cs', '.ts', '.json', '.csproj', '.html', '.mjs', '.md', '.yml'):
            assert not re.search(rb'^\s*(?://|<!--|/\*)?\s*#(?:if|else|endif|elif)\b', content, re.M), f'Unprocessed condition: {relative}'
    validate_relative_modules(output, digests)
    for relative in digests:
        if relative.startswith('frontend/public/i18n/') and relative.endswith('/en.json'):
            chinese = posixpath.join(posixpath.dirname(relative), 'zh-CN.json')
            assert chinese in digests, f'Missing translation pair: {relative}'
            english_keys = translation_keys(json.loads((output / relative).read_text(encoding='utf-8')))
            chinese_keys = translation_keys(json.loads((output / chinese).read_text(encoding='utf-8')))
            assert english_keys.keys() == chinese_keys.keys(), f'Generated translation keys differ: {relative}'
            for key in english_keys:
                assert set(re.findall(r'\{\{(\w+)\}\}', english_keys[key])) == set(re.findall(r'\{\{(\w+)\}\}', chinese_keys[key])), f'Generated translation placeholders differ: {relative}/{key}'
    api = output / 'backend/src/Generation.Probe.Api'
    infra = output / 'backend/src/Generation.Probe.Infrastructure'
    frontend = output / 'frontend'
    assert frontend.exists() == values['SpaFrontend'], 'Frontend applicability'
    settings = json.loads(re.sub(r'^\s*//.*$', '', (api / 'appsettings.json').read_text(encoding='utf-8'), flags=re.M))
    assert ('Routing' in settings['Leistd'].get('MultiTenancy', {})) == (values['RemoteTokenAuth'] and values['IncludeMultiTenancy']), 'Tenant routing configuration applicability'
    readme = (output / 'README.md').read_text(encoding='utf-8')
    testing = (output / 'docs/standards/testing.md').read_text(encoding='utf-8')
    deployment = (output / 'docs/deploy/README.md').read_text(encoding='utf-8')
    invocation = (output / 'docs/standards/service-invocation.md').read_text(encoding='utf-8')
    assert ('前端使用 Angular 22。' in readme) == values['SpaFrontend'], 'Frontend introduction applicability'
    assert ('npm ' in testing) == values['SpaFrontend'], 'Frontend testing instructions applicability'
    assert ('npm start' in deployment) == values['SpaFrontend'], 'Frontend deployment instructions applicability'
    action_check = 'scripts/check-operation-action-i18n.py'
    assert (action_check in testing) == (action_check in digests), 'Testing instructions reference an excluded action checker'
    i18n_check = 'scripts/check-i18n.py'
    assert (i18n_check in digests) == values['IncludeLocalization'], 'i18n checker applicability'
    assert (i18n_check in testing) == (i18n_check in readme) == (i18n_check in digests), 'Instructions reference an excluded i18n checker'
    assert ('单实例配 `KeysPath`' in deployment) == values['SpaFrontend'], 'Browser session deployment prerequisite applicability'
    assert ('/api/v1/auth/signin' in invocation) == (values['OpenIddictServer'] or values['ResourceBrowserSession']), 'Browser relying-party instructions applicability'
    assert ('tenant-routing.read' in invocation) == values['IncludeMultiTenancy'], 'Tenant machine scope instructions applicability'
    examples = re.findall(r'```json\s*\n(.*?)\n```', invocation, re.S)
    assert examples, 'Missing service invocation configuration example'
    for example in examples:
        documented = json.loads(example)
        authentication = documented.get('Authentication')
        assert (authentication is not None) == values['RemoteTokenAuth'], 'Resource authentication example applicability'
        if authentication is not None:
            assert ('ClientId' in authentication) == values['ResourceBrowserSession'], 'Browser client credentials example applicability'
        assert ('Identity' in documented['Leistd']['ServiceClients']) == (values['RemoteTokenAuth'] and values['IncludeMultiTenancy']), 'Tenant routing client example applicability'
    backend_readme = (output / 'backend/README.md').read_text(encoding='utf-8')
    infrastructure_project = (infra / 'Generation.Probe.Infrastructure.csproj').read_text(encoding='utf-8')
    migrator_protection = values['LocalIdentity'] and values['IncludeMultiTenancy']
    assert ('API 与 `DbMigrator` 必须共用密钥环' in backend_readme) == migrator_protection, 'Migrator key-sharing instructions applicability'
    assert ('API 与 DbMigrator 必须共享密钥环' in infrastructure_project) == migrator_protection, 'Migrator key-sharing package comment applicability'
    development_compose = (output / 'deploy/docker-compose.dev.yml').read_text(encoding='utf-8')
    assert ('mailpit' in development_compose) == values['Email'], 'Development mail dependency applicability'
    assert (api / 'Controllers/TenantController.cs').exists() == values['Impersonation'], 'Impersonation controller'
    assert (infra / 'Persistence/Migrations/Control').exists() == (values['LocalIdentity'] and values['IncludeMultiTenancy']), 'Control migrations'
    assert (infra / 'Persistence/IdentityControlDbContext.cs').exists() == (values['LocalIdentity'] and values['IncludeMultiTenancy']), 'Control context'
    assert (api / 'Controllers/SettingController.cs').exists() == values['Email'], 'Email test controller'
    assert (output / 'backend/src/Generation.Probe.DbMigrator/ResourceAdminBootstrapRunner.cs').exists() == values['RemoteTokenAuth'], 'Resource bootstrap'
    migrations = '\n'.join(p.read_text(encoding='utf-8') for p in (infra / 'Persistence/Migrations').rglob('*.cs'))
    assert ('name: "OperationRecords"' in migrations) == values['IncludeOperationRecords'], 'Operation record tables'
    assert 'CreationTime' in migrations and 'TenantId' in migrations, 'Baseline entity audit/scope columns'
    if values['SpaFrontend']:
        package = json.loads((frontend / 'package.json').read_text(encoding='utf-8'))
        lock = json.loads((frontend / 'package-lock.json').read_text(encoding='utf-8'))
        assert ('@microsoft/signalr' in package['dependencies']) == (values['IncludeNotifications'] or values['IncludeRealTime']), 'SignalR dependency'
        assert ('qrcode' in package['dependencies']) == values['LocalIdentity'], 'QR dependency'
        assert package['dependencies'] == lock['packages']['']['dependencies'], 'Lock runtime roots'
        assert package['devDependencies'] == lock['packages']['']['devDependencies'], 'Lock build roots'
        assert (frontend / 'src/app/features/platform/components/operation-records').exists() == values['IncludeOperationRecords'], 'History UI'
        assert (frontend / 'src/app/core/services/tenant-context-service.ts').exists() == values['IncludeMultiTenancy'], 'Tenant frontend'
        assert ('POST /api/v1/settings/email/test' in (frontend / '_mock/api/setting.ts').read_text(encoding='utf-8')) == values['Email'], 'Email mock endpoint'
        if values['OpenIddictServer']:
            scopes = (frontend / '_mock/data/open-applications.ts').read_text(encoding='utf-8')
            for scope in ('tenant-routing.read', 'tenant-migration.read'):
                assert (scope in scopes) == values['IncludeMultiTenancy'], f'Mock machine scope: {scope}'
        tenants = frontend / 'src/app/features/platform/components/tenants'
        if tenants.exists():
            assert ('onImpersonate' in (tenants / 'tenants.ts').read_text(encoding='utf-8')) == values['Impersonation'], 'Impersonation page action'
            assert ('canImpersonate' in (tenants / 'widgets/tenant-table/tenant-table.ts').read_text(encoding='utf-8')) == values['Impersonation'], 'Impersonation table action'
    return digests


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--workers', type=int, default=4)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    assert 1 <= args.workers <= 8
    def source_digests():
        return {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                for p in sorted((ROOT / 'template').rglob('*')) if p.is_file()
                and not any(part in ('node_modules', 'bin', 'obj', '.cache') for part in p.relative_to(ROOT / 'template').parts)}
    candidate_sources = source_digests()
    config = json.loads(model.CONFIG_PATH.read_text(encoding='utf-8'))
    combinations = model.all_combinations(config)
    groups = {}
    for values in combinations:
        groups.setdefault(model.effective_shape(values), []).append(values)
    assert len(combinations) == 768 and len(groups) == 320, 'Public parameter model changed; update its verification contract.'
    root = args.output or Path(tempfile.mkdtemp(prefix='generation-', dir=ROOT / '.tmp'))
    root = root.resolve()
    assert root.is_relative_to((ROOT / '.tmp').resolve())
    root.mkdir(parents=True, exist_ok=True)
    hive = root / 'hive'
    subprocess.run(['dotnet', 'new', 'install', str(ROOT / 'template'), '--debug:custom-hive', str(hive), '--force'], check=True, capture_output=True)
    worker_state = threading.local()

    def initialize_worker():
        # dotnet new updates hive metadata during generation; concurrent processes must not share it.
        worker_hive = root / 'worker-hives' / threading.current_thread().name
        if worker_hive.exists():
            shutil.rmtree(worker_hive)
        shutil.copytree(hive, worker_hive)
        worker_state.hive = worker_hive

    def generate(item):
        index, values = item
        output = root / 'generated' / str(index)
        if output.exists():
            shutil.rmtree(output)
        result = subprocess.run(['dotnet', 'new', 'fullstack-app', '-n', 'Generation.Probe', '-o', str(output), *cli_arguments(config, values), '--debug:custom-hive', str(getattr(worker_state, 'hive', hive))], capture_output=True, text=True, encoding='utf-8', errors='replace')
        assert result.returncode == 0, f'{index}: {result.stdout}\n{result.stderr}'
        try:
            digest = validate(output, values, config)
        except Exception as exc:
            raise AssertionError(f'{index}: {dict(zip(model.EFFECTIVE_DIMENSIONS, model.effective_shape(values)))}: {exc}') from exc
        return index, digest

    representatives = [group[0] for group in groups.values()]
    with ThreadPoolExecutor(max_workers=args.workers, initializer=initialize_worker) as pool:
        digests = dict(pool.map(generate, enumerate(representatives)))
    comparisons = []
    # Both sparse and all-feature endpoints of each role exercise all ignored input values.
    for role in ('Identity', 'Standalone', 'Resource'):
        indexes = [i for i, v in enumerate(representatives) if v['ServiceRole'] == role]
        for index in (indexes[0], indexes[-1]):
            group = groups[model.effective_shape(representatives[index])]
            for variant in group[1:]:
                variant_id = len(representatives) + len(comparisons)
                _, digest = generate((variant_id, variant))
                assert digest == digests[index], f'Ignored role parameter changed actual bytes: {role}/{variant_id}'
                comparisons.append({'variant': variant_id, 'representative': index})
    assert candidate_sources == source_digests(), 'Template source changed during generation; evidence does not describe one candidate.'
    report = {'candidateSha': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True, encoding='utf-8', errors='replace').strip(),
              'templateSourceDigests': candidate_sources,
              'rawInputs': len(combinations), 'effectiveShapes': len(groups), 'equivalenceComparisons': comparisons,
              'inputMapping': [{'input': cli_arguments(config, v), 'effective': list(model.effective_shape(v))} for v in combinations],
              'digests': digests}
    (root / 'generation-results.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(f'PASS: {len(groups)} generated shapes, {len(combinations)} input mappings, {len(comparisons)} actual equivalence comparisons: {root}')


if __name__ == '__main__':
    main()
