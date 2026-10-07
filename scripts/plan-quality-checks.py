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
import posixpath
import re
import subprocess
import sys
from xml.etree import ElementTree

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


# Inputs of every framework project build: a change here can affect any test.
FRAMEWORK_SHARED_INPUT = re.compile(
    r'framework/[^/]+\.props|framework/build/.+|framework/tests/Directory\.Build\.props|framework/tests/shared/.+|'
    r'Directory\.Build\.(?:props|targets)|Directory\.Packages\.props|NuGet\.config|global\.json|VERSION')
# The only MSBuild files the graph models; any other props/targets is an unmodelled import.
FRAMEWORK_BUILD_FILES = {'framework/Directory.Build.props', 'framework/Directory.Packages.props',
                         'framework/common.props', 'framework/tests/Directory.Build.props', 'Directory.Build.targets'}
# Items that only name references; any other item reaching outside its project is a cross-project input.
REFERENCE_ITEMS = {'PackageReference', 'FrameworkReference', 'Using', 'InternalsVisibleTo'}


class FrameworkProofUnavailable(ValueError):
    """The candidate cannot be proven to touch only modelled framework projects."""


def tree_files(sha: str) -> list[str]:
    return git('-c', 'core.quotePath=false', 'ls-tree', '-r', '--name-only', '-z', sha).split('\0')[:-1]


def read_blobs(sha: str, paths: list[str]) -> dict[str, str]:
    """Read many files of one commit through a single git process."""
    request = ''.join(f'{sha}:{path}\n' for path in paths).encode('utf-8')
    data = subprocess.run(['git', '-C', str(ROOT), 'cat-file', '--batch'], input=request,
                          capture_output=True, check=True).stdout
    blobs, offset = {}, 0
    for path in paths:
        header_end = data.index(b'\n', offset)
        header = data[offset:header_end].split()
        if len(header) != 3 or header[1] != b'blob':
            raise FrameworkProofUnavailable(f'cannot read {path} at {sha}')
        size = int(header[2])
        blobs[path] = data[header_end + 1:header_end + 1 + size].decode('utf-8')
        offset = header_end + 2 + size
    return blobs


def framework_test_project(path: str) -> bool:
    return path.startswith('framework/tests/') and path.endswith('.Tests.csproj')


def all_framework_tests(sha: str) -> list[str]:
    return sorted(path for path in tree_files(sha) if framework_test_project(path))


def framework_graph(sha: str) -> dict[str, tuple[str, ...]]:
    """ProjectReference graph of one commit; anything the graph cannot model raises."""
    files = tree_files(sha)
    projects = sorted(path for path in files if path.startswith('framework/') and path.endswith('.csproj'))
    unknown = sorted(path for path in files if path.endswith(('.props', '.targets')) and path not in FRAMEWORK_BUILD_FILES
                     and (path.startswith('framework/') or '/' not in path))
    if unknown:
        raise FrameworkProofUnavailable(f'unmodelled MSBuild import {unknown[0]}')
    directories = [posixpath.dirname(path) for path in projects]
    if len(set(directories)) != len(directories) or any(
            other.startswith(directory + '/') for directory in directories for other in directories):
        raise FrameworkProofUnavailable('nested or shared project directories')
    blobs = read_blobs(sha, projects + sorted(FRAMEWORK_BUILD_FILES & set(files)))
    for path in FRAMEWORK_BUILD_FILES & set(files):
        if re.search(r'<Compile\b[^>]*\bInclude\s*=|<AdditionalFiles\b|<EnableDefault(?:Compile)?Items>\s*false', blobs[path]):
            raise FrameworkProofUnavailable(f'explicit compile input in {path}')
    known = set(projects)
    graph = {}
    for project in projects:
        directory = posixpath.dirname(project)
        try:
            root = ElementTree.fromstring(blobs[project])
        except ElementTree.ParseError as error:
            raise FrameworkProofUnavailable(f'unparseable {project}') from error
        references = set()
        for element in root.iter():
            if element.tag.startswith('{'):
                raise FrameworkProofUnavailable(f'namespaced MSBuild element in {project}')
            values = [element.text or '', *element.attrib.values()]
            if element.tag == 'Import' or any(marker in value for value in values for marker in ('$(', '@(', '%(')):
                raise FrameworkProofUnavailable(f'unresolvable property or Import in {project}')
            if element.tag in ('Compile', 'AdditionalFiles') and ('Include' in element.attrib or 'Update' in element.attrib) \
                    or element.tag in ('EnableDefaultItems', 'EnableDefaultCompileItems', 'Link') or 'Link' in element.attrib:
                raise FrameworkProofUnavailable(f'unmodelled compile input in {project}')
            include = element.attrib.get('Include')
            if element.tag == 'ProjectReference':
                if not include or any(marker in include for marker in '*?;'):
                    raise FrameworkProofUnavailable(f'unresolvable ProjectReference in {project}')
                target = posixpath.normpath(posixpath.join(directory, include.replace('\\', '/')))
                if target not in known:
                    raise FrameworkProofUnavailable(f'{project} references missing project {target}')
                references.add(target)
            elif include and element.tag not in REFERENCE_ITEMS:
                target = posixpath.normpath(posixpath.join(directory, include.replace('\\', '/')))
                if include.startswith(('/', '\\')) or not target.startswith(directory + '/'):
                    raise FrameworkProofUnavailable(f'cross-project input in {project}')
        graph[project] = tuple(sorted(references))
    return graph


def select_framework_tests(base: str, head: str, paths: list[str]) -> tuple[list[str], str]:
    """Changed files -> owning projects -> reverse dependency closure -> test projects.

    Only edits inside unchanged projects narrow; the empty result is not trusted.
    """
    for path in paths:
        if FRAMEWORK_SHARED_INPUT.fullmatch(path):
            raise FrameworkProofUnavailable(f'shared build or test input {path}')
    graph = framework_graph(head)
    if framework_graph(base) != graph:
        raise FrameworkProofUnavailable('project or ProjectReference added, removed or renamed')
    owners = {posixpath.dirname(project): project for project in graph}
    seeds = set()
    for path in paths:
        parent = posixpath.dirname(path)
        while parent and parent not in owners:
            parent = posixpath.dirname(parent)
        if not parent:
            raise FrameworkProofUnavailable(f'{path} belongs to no framework project')
        owner = owners[parent]
        if owner.startswith('framework/tests/') and not framework_test_project(owner):
            raise FrameworkProofUnavailable(f'shared test base {owner}')
        seeds.add(owner)
    dependents = {project: set() for project in graph}
    for project, references in graph.items():
        for reference in references:
            dependents[reference].add(project)
    affected, pending = set(seeds), list(seeds)
    while pending:
        for dependent in dependents[pending.pop()] - affected:
            affected.add(dependent)
            pending.append(dependent)
    selected = sorted(project for project in affected if framework_test_project(project))
    if not selected:
        raise FrameworkProofUnavailable('no test project depends on the changed projects')
    return selected, 'Affected projects: ' + ', '.join(sorted(posixpath.basename(seed)[:-7] for seed in seeds))


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


def execution_slices(scenarios: dict, selected: list[str], tier: str, mode: str, container_smoke: bool) -> list[dict]:
    """Bind the registered logical allocation to the candidate's selected work."""
    if not selected:
        return []
    sample = next(iter(scenarios.values()))
    selected_names = set(selected)
    containers = set(sample['Containers']) if container_smoke else set()
    bins = []
    for logical in sample['Titles'][tier]:
        members = [name for name, info in scenarios.items()
                   if name in selected_names and info['Slices'].get(tier) == logical]
        if members:
            bins.append(dict(key=f'execution-{len(bins)+1:02d}', title='', Scenarios=members,
                             Containers=[name for name in members if name in containers]))
    for group in bins:
        preview = '、'.join(sorted(group['Scenarios'], key=lambda name: (len(name), name))[:2])
        stages = {'full':'完整阶段','frontend':'前端阶段','backend':'后端阶段'}[mode]
        container = '；含容器' if group['Containers'] else ''
        group['title'] = f"{stages} · {preview}（共{len(group['Scenarios'])}场景{container}）"
    return bins


def create_plan(tier: str, base: str, event: str, candidate_input: str, docs_only: bool = False,
                container_smoke: bool = False) -> dict:
    head = git('rev-parse', 'HEAD')
    scenarios = coverage.load_scenarios()
    registered = [name for name, info in scenarios.items() if tier in info['Slices']]
    all_tests = all_framework_tests(head)
    def finish(value):
        if container_smoke and (value['DocsOnly'] or value['Mode'] != 'full'):
            raise ValueError('Container scope requires a dynamic full-mode plan')
        # The list is bound to BaseSha/CandidateSha of this same plan; no list means no job.
        if not value['FrameworkTests']:
            value.update(FrameworkTestProjects=[], FrameworkTestSelection='none', FrameworkTestReason='No framework test responsibility')
        elif value.get('FrameworkTestSelection') != 'affected':
            value.update(FrameworkTestProjects=all_tests, FrameworkTestSelection='all',
                         FrameworkTestReason=value.get('FrameworkTestReason') or value['Reason'])
        value['ContainerSmoke'] = container_smoke
        value['Slices'] = execution_slices(scenarios, value['Scenarios'], tier, value['Mode'], value['ContainerSmoke'])
        return value
    plan = dict(Version=2, CandidateSha=head, BaseSha=base, Tier=tier, DocsOnly=False,
                Mode='full', Scenarios=registered, FrameworkTests=True, ConsumerProjects=None,
                Reason='Full: non-PR, shared/unknown input or uncertain baseline',
                FrameworkTestProjects=all_tests, FrameworkTestSelection='all', FrameworkTestReason='')
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
        else:
            # Independent of template scope: a mixed change keeps every scenario
            # but may still need only the affected framework test projects.
            try:
                selected_tests, reason = select_framework_tests(base, head, paths)
                plan.update(FrameworkTestProjects=selected_tests, FrameworkTestSelection='affected', FrameworkTestReason=reason)
            except FrameworkProofUnavailable as error:
                plan.update(FrameworkTestReason=f'All: {error}')
        if not (frontend or backend) and all(owners):
            # Linked/generated cross-project compilation inputs are outside this
            # proof. A future explicit Compile/AdditionalFiles rule disables it.
            build_inputs = (list((ROOT / 'framework').rglob('*.csproj')) +
                            [item for suffix in ('*.props', '*.targets') for item in (ROOT / 'framework').rglob(suffix)
                             if 'obj' not in item.parts and 'bin' not in item.parts] +
                            [ROOT / name for name in ('Directory.Build.props', 'Directory.Build.targets') if (ROOT / name).exists()])
            if any(re.search(r'<Compile\b[^>]*\bInclude\s*=|<AdditionalFiles\b|<EnableDefaultCompileItems>\s*false', item.read_text(encoding='utf-8')) for item in build_inputs):
                return finish(dict(plan, FrameworkTestSelection='all', FrameworkTestReason='All: explicit compile input'))
            # This narrows only empty NuGet restore/build consumers. Runtime
            # tests and PG/OIDC remain full; nuspec reverse closure is evaluated
            # against the candidate packages by test-package-consumption.ps1.
            plan.update(ConsumerProjects=sorted(set(owners)), Reason='Framework source: consumer dependency closure only')
    except (subprocess.CalledProcessError, ValueError, KeyError, OSError, IndexError):
        # A known-but-unparseable input is never grounds for fewer checks.
        return finish(dict(plan, Mode='full', Scenarios=registered, FrameworkTests=True, ConsumerProjects=None,
                    Reason='Full: input/dependency proof unavailable', FrameworkTestSelection='all', FrameworkTestReason=''))
    return finish(plan)


def local_scenarios(base: str) -> dict:
    """Select full local products from file producers, independently of CI stages."""
    plan = create_plan('pr', base, 'pull_request', '')
    scenarios = coverage.load_scenarios()
    registered = {name: info for name, info in scenarios.items() if 'pr' in info['Slices']}
    head = plan['CandidateSha']
    result = dict(Kind='local-template-scenarios', HeadSha=head, BaseSha=base,
                  Scope='pr', Scenarios=list(registered), Selection='complete-pr',
                  Reason=plan['Reason'])
    if not re.fullmatch(r'[0-9a-f]{40}', base) or base == '0' * 40 or base == head:
        return result
    try:
        if git('status', '--porcelain', '--untracked-files=normal'):
            return result
        paths = git('-c', 'core.quotePath=false', 'diff', '--no-renames', '--name-only', '-z',
                    base, head).split('\0')[:-1]
        if not paths or not all(frontend_source(path) or backend_source(path) for path in paths):
            return result
        config_path = 'template/.template.config/template.json'
        old = json.loads(git('show', f'{base}:{config_path}'))
        current = json.loads(git('show', f'{head}:{config_path}'))
        if old != current:
            return result
        selected = {'identity', 'identity-all-features'}
        for path in paths:
            selected |= source_producers(old, path, registered) | source_producers(current, path, registered)
        if git('rev-parse', 'HEAD') != head or git('status', '--porcelain', '--untracked-files=normal'):
            raise ValueError('working tree or HEAD changed during selection')
        result.update(Scenarios=[name for name in registered if name in selected],
                      Selection='source-products', Reason='All changed source products and default/all-feature products')
    except (subprocess.CalledProcessError, ValueError, KeyError, OSError, IndexError, UnicodeError) as error:
        result['Reason'] = f'Complete PR: local product proof unavailable ({error})'
    return result


def local_framework_tests(base: str) -> dict:
    """Local L1 list: the same selection as a PR plan from base to the committed HEAD."""
    plan = create_plan('pr', base, 'pull_request', '')
    result = dict(Kind='local-framework-tests', HeadSha=plan['CandidateSha'], BaseSha=base,
                  Projects=plan['FrameworkTestProjects'], Selection=plan['FrameworkTestSelection'],
                  Reason=plan['FrameworkTestReason'])
    if git('rev-parse', 'HEAD') != plan['CandidateSha'] or git('status', '--porcelain', '--untracked-files=normal'):
        result.update(Projects=all_framework_tests(plan['CandidateSha']), Selection='all',
                      Reason='All: working tree or HEAD changed during selection')
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tier', choices=['pr', 'full'], default='full')
    parser.add_argument('--base', default=os.environ.get('PR_BASE_SHA', ''))
    parser.add_argument('--event', default=os.environ.get('EVENT_NAME', ''))
    parser.add_argument('--candidate-input', default=os.environ.get('CANDIDATE_SHA', ''))
    parser.add_argument('--docs-only', choices=['true', 'false'], default='false')
    parser.add_argument('--container-smoke', choices=['true', 'false'], default='false',
                        help='Bind the independent container-scope decision into this candidate plan')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--github-output', action='store_true')
    parser.add_argument('--local-scenarios', action='store_true',
                        help='Output local L1 scenario names for -Scenarios; no CI stage reduction')
    parser.add_argument('--local-framework-tests', action='store_true',
                        help='Output local L1 framework test projects (Projects) affected since --base')
    args = parser.parse_args()
    local = args.local_scenarios or args.local_framework_tests
    if local and (args.local_scenarios and args.local_framework_tests or args.tier != 'pr' or args.github_output or
                  args.docs_only != 'false' or args.candidate_input or args.container_smoke != 'false'):
        parser.error('--local-scenarios/--local-framework-tests: choose one, with --tier pr and without CI output, docs-only or candidate-input')
    if args.local_scenarios:
        plan = local_scenarios(args.base)
    elif args.local_framework_tests:
        plan = local_framework_tests(args.base)
    else:
        plan = create_plan(args.tier, args.base, args.event, args.candidate_input, args.docs_only == 'true', args.container_smoke == 'true')
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
