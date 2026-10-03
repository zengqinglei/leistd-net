#!/usr/bin/env python3
"""Plan PR validation from complete input changes; unknown inputs retain all checks.

The internal-document allowlist stays in ci.yml. Scenario/source definitions stay
in their existing files. This entry also works locally; omit valid PR inputs for full.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys

sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('coverage', ROOT / 'scripts/check-template-scenario-coverage.py')
coverage = importlib.util.module_from_spec(spec)
spec.loader.exec_module(coverage)


def git(*arguments: str) -> str:
    return subprocess.check_output(['git', '-C', str(ROOT), *arguments], text=True, encoding='utf-8', stderr=subprocess.PIPE).rstrip('\n')


def frontend_source(path: str) -> bool:
    return bool(re.fullmatch(r'template/(?:frontend|\.template\.config/localization/frontend)/(?:src|public|_mock)/.+\.(?:ts|html|css|scss|json|svg|png|jpg|jpeg|webp|ico)', path))


def backend_source(path: str) -> bool:
    return bool(re.fullmatch(r'template/backend/(?:src|tests)/.+\.cs', path))


def project_owner(path: str) -> str | None:
    # Only source files inside one real package project can seed consumer selection.
    # A missing/deleted project, project configuration or shared input means full.
    if not re.fullmatch(r'framework/(?:components/[^/]+|ddd-struct)/Leistd\.[^/]+/.+\.cs', path):
        return None
    parent = ROOT / path
    while parent != ROOT:
        projects = list(parent.glob('Leistd.*.csproj')) if parent.is_dir() else []
        if projects:
            return projects[0].stem if len(projects) == 1 else None
        parent = parent.parent
    return None


def source_producers(config: dict, path: str, scenarios: dict) -> set[str]:
    """File-level producers, conservatively including every conditional line.

    Reuse the existing template engine semantics; no feature-directory map.
    An excluded file still gets the default/all-feature sentinels below.
    """
    # Narrow only the source semantics this proof models. Future engine rules
    # must be reviewed before they can reduce validation responsibility.
    for source in config.get('sources', []):
        if not isinstance(source, dict) or set(source) - {'source', 'target', 'condition', 'exclude', 'modifiers'}:
            raise ValueError('Unmodelled template source rule')
        for modifier in source.get('modifiers', []):
            if not isinstance(modifier, dict) or set(modifier) - {'condition', 'exclude'}:
                raise ValueError('Unmodelled template modifier rule')
    producers: set[str] = set()
    sources = list(coverage.iter_sources(config, [path]))
    if not sources:
        raise ValueError(f'No known template source for {path}')
    for relative, _, condition, modifiers in sources:
        excludes = [item['condition'] for item in modifiers
                    if any(coverage.glob_match(relative, pattern) for pattern in item.get('exclude', []))]
        for name, info in scenarios.items():
            values = coverage.symbol_values(config, coverage.parse_cli_arguments(config, info['Arguments']))
            if (not condition or coverage.evaluate(condition, values)) and not any(coverage.evaluate(expr, values) for expr in excludes):
                producers.add(name)
    return producers


def create_plan(tier: str, base: str, event: str, candidate_input: str, docs_only: bool = False) -> dict:
    head = git('rev-parse', 'HEAD')
    scenarios = coverage.load_scenarios()
    registered = [name for name, info in scenarios.items() if tier in info['Slices']]
    def finish(value):
        selected = set(value['Scenarios'])
        titles = next(iter(scenarios.values()))['Titles'][tier]
        value['Slices'] = [dict(key=key, title=title) for key, title in titles.items()
                           if any(name in selected and info['Slices'].get(tier) == key for name, info in scenarios.items())]
        return value
    plan = dict(Version=1, CandidateSha=head, BaseSha=base, Tier=tier, DocsOnly=False,
                Mode='full', Scenarios=registered, FrameworkTests=True, ConsumerProjects=None,
                Reason='Full: non-PR, shared/unknown input or uncertain baseline')
    if docs_only:
        if event != 'pull_request' or candidate_input or tier != 'pr':
            raise ValueError('Internal-document exemption is only for a direct PR')
        plan.update(DocsOnly=True, Scenarios=[], FrameworkTests=False, ConsumerProjects=[], Reason='Internal documentation only')
        return finish(plan)
    if tier != 'pr' or event != 'pull_request' or candidate_input or not re.fullmatch(r'[0-9a-f]{40}', base) or base == '0' * 40 or base == head:
        return finish(plan)
    try:
        # Local plans must describe a committed snapshot, just like CI. A dirty
        # tree cannot be narrowed by a diff that only describes committed HEAD.
        if git('status', '--porcelain', '--untracked-files=normal'):
            return finish(dict(plan, Reason='Full: working tree differs from candidate HEAD'))
        git('cat-file', '-e', f'{base}^{{commit}}')
        paths = git('-c', 'core.quotePath=false', 'diff', '--no-renames', '--name-only', '-z', base, head).split('\0')[:-1]
        if not paths:
            return finish(plan)
        # Strip only inputs already covered by the workflow's internal-doc rule.
        # Mixed documentation deliberately remains full: avoid a second allowlist.
        frontend = all(frontend_source(path) for path in paths)
        backend = all(backend_source(path) for path in paths)
        owners = [project_owner(path) for path in paths]
        if frontend or backend:
            config_path = 'template/.template.config/template.json'
            old = json.loads(git('show', f'{base}:{config_path}'))
            current = json.loads((ROOT / config_path).read_text(encoding='utf-8'))
            if old != current:
                return finish(plan)
            affected = set()
            for path in paths:
                affected |= source_producers(old, path, scenarios) | source_producers(current, path, scenarios)
            # Always retain opening/closing representatives, without assuming a
            # feature can never leak into a product that excludes its files.
            affected |= {'identity', 'identity-all-features'}
            selected = [name for name in registered if name in affected]
            if not selected:
                return finish(plan)
            plan.update(Mode='frontend' if frontend else 'backend', Scenarios=selected,
                        FrameworkTests=False, ConsumerProjects=[], Reason='Only template frontend inputs' if frontend else 'Only template backend C# inputs')
        elif all(owners):
            # Linked/generated cross-project compilation inputs are outside this
            # proof. A future explicit Compile/AdditionalFiles rule disables it.
            build_inputs = (list((ROOT / 'framework').rglob('*.csproj')) +
                            [item for suffix in ('*.props', '*.targets') for item in (ROOT / 'framework').rglob(suffix)
                             if 'obj' not in item.parts and 'bin' not in item.parts] +
                            [ROOT / name for name in ('Directory.Build.props', 'Directory.Build.targets') if (ROOT / name).exists()])
            if any(re.search(r'<Compile\b[^>]*\bInclude\s*=|<AdditionalFiles\b|<EnableDefaultCompileItems>\s*false', item.read_text(encoding='utf-8')) for item in build_inputs):
                return finish(plan)
            # This narrows only empty NuGet restore/build consumers. Runtime
            # tests and PG/OIDC remain full; nuspec reverse closure is evaluated
            # against the candidate packages by test-package-consumption.ps1.
            plan.update(ConsumerProjects=sorted(set(owners)), Reason='Framework source: consumer dependency closure only')
    except (subprocess.CalledProcessError, ValueError, KeyError, OSError, IndexError):
        # A known-but-unparseable input is never grounds for fewer checks.
        return finish(dict(plan, Mode='full', Scenarios=registered, FrameworkTests=True, ConsumerProjects=None,
                    Reason='Full: input/dependency proof unavailable'))
    return finish(plan)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tier', choices=['pr', 'full'], default='full')
    parser.add_argument('--base', default=os.environ.get('PR_BASE_SHA', ''))
    parser.add_argument('--event', default=os.environ.get('EVENT_NAME', ''))
    parser.add_argument('--candidate-input', default=os.environ.get('CANDIDATE_SHA', ''))
    parser.add_argument('--docs-only', choices=['true', 'false'], default='false')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--github-output', action='store_true')
    args = parser.parse_args()
    plan = create_plan(args.tier, args.base, args.event, args.candidate_input, args.docs_only == 'true')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    text = json.dumps(plan, ensure_ascii=False, separators=(',', ':'))
    args.output.write_text(text, encoding='utf-8')
    if args.github_output:
        with open(os.environ['GITHUB_OUTPUT'], 'a', encoding='utf-8') as output:
            output.write('validation_plan=' + text + '\n')
            output.write('slices=' + json.dumps(plan['Slices'], ensure_ascii=False, separators=(',', ':')) + '\n')
        with open(os.environ['GITHUB_STEP_SUMMARY'], 'a', encoding='utf-8') as summary:
            if plan['DocsOnly']:
                summary.write('本次仅修改内部文档：此作业只判定范围和生成计划；未打包 Framework，动态验证不适用。\n\n')
            summary.write('验证计划：' + text + '\n')
    print(text)


if __name__ == '__main__':
    main()
