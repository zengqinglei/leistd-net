#!/usr/bin/env python3
"""Inject defects into actual generated docs, frontend registry and quality proofs."""
from __future__ import annotations
import argparse
import importlib.util
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import zipfile

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--generation-root', type=Path, required=True)
    parser.add_argument('--frontend-tools', type=Path, required=True)
    parser.add_argument('--feed', type=Path)
    args = parser.parse_args()
    output = ROOT / '.tmp/documentation-mutations'
    output.mkdir(parents=True, exist_ok=True)
    project = output / 'project'
    source = args.generation_root.resolve() / 'generated/0'
    projects_file = output / 'projects.json'
    projects_file.write_text(json.dumps([str(project)]), encoding='utf-8')
    def reset():
        if project.exists(): shutil.rmtree(project)
        shutil.copytree(source, project)
    records = []
    def run(label, command, succeeds=True):
        result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, encoding='utf-8', errors='replace')
        (output / (label + '.log')).write_text(result.stdout + result.stderr, encoding='utf-8')
        assert (result.returncode == 0) == succeeds, (label, (result.stdout + result.stderr)[-1500:])
        records.append(dict(Label=label, ExitCode=result.returncode))
        print('PASS defect detection:', label, flush=True)
    checks = ['pwsh', 'scripts/test-generated-documentation.ps1', '-ProjectsPath', str(projects_file)]
    reset(); run('generated-documents-valid', checks)
    for name, path, content in [
        ('missing-link', 'README.md', '\n[missing](docs/missing.md)\n'),
        ('missing-anchor', 'README.md', '\n[missing](README.md#missing-anchor)\n'),
        ('template-residue', 'README.md', '\nCompanyName.ProjectName\n'),
        ('ai-entry', 'CLAUDE.md', '@missing.md\n')]:
        reset()
        target = project / path
        target.write_text(content if name == 'ai-entry' else target.read_text(encoding='utf-8') + content, encoding='utf-8')
        run(name, checks, False)
    reset()
    skill = project / '.agents/skills/leistd-project-workflow/SKILL.md'
    skill.write_text(skill.read_text(encoding='utf-8').replace('---\n', '---\nunknown-field: true\n', 1), encoding='utf-8')
    run('skill-metadata', [sys.executable, 'scripts/vendor/skill-creator/quick_validate.py', str(skill.parent)], False)
    reset()
    frontend = project / 'frontend/README.md'
    frontend.write_text(frontend.read_text(encoding='utf-8') + '\n\n\n', encoding='utf-8')
    pin = re.search(r'"node_modules/prettier"\s*:\s*\{\s*"version"\s*:\s*"([^"]+)"', (ROOT / 'template/frontend/package-lock.json').read_text(encoding='utf-8'))[1]
    formatter = ['node', 'scripts/check-generated-markdown.mjs', str(projects_file),
                 str(ROOT / '.tmp/documentation-tools/node_modules/prettier/index.mjs'), pin, str(output / 'format.json')]
    run('generated-frontend-format', formatter, False)
    reset(); run('generated-frontend-format-restored', formatter)
    spec = importlib.util.spec_from_file_location('using_guards', ROOT / 'scripts/check-using-guards.py')
    guards = importlib.util.module_from_spec(spec); spec.loader.exec_module(guards)
    fixture = output / 'empty-block'
    (fixture / 'template').mkdir(parents=True, exist_ok=True)
    (fixture / 'template/README.md').write_text('<!--#if (SpaFrontend) -->\n\n<!--#endif -->\n', encoding='utf-8')
    guards.ROOT = str(fixture)
    assert guards.check_no_empty_conditional_blocks() == 1
    (fixture / 'template/README.md').write_text('plain Markdown\n', encoding='utf-8')
    assert guards.check_no_empty_conditional_blocks() == 0
    records.append(dict(Label='markdown-empty-conditional-block', ExitCode=0))
    # Read actual pinned dependencies; mutate only a private copy of gate inputs.
    registry = output / 'registry'
    if registry.exists(): shutil.rmtree(registry)
    (registry / 'scripts').mkdir(parents=True)
    shutil.copy2(ROOT / 'scripts/check-spartan-customizations.mjs', registry / 'scripts/check-spartan-customizations.mjs')
    shutil.copytree(ROOT / 'template', registry / 'template', ignore=shutil.ignore_patterns('node_modules', 'bin', 'obj'))
    gate = ['node', str(registry / 'scripts/check-spartan-customizations.mjs'), str(args.frontend_tools.resolve())]
    run('spartan-registry-valid', gate)
    document = registry / 'template/docs/standards/frontend-spartan.md'
    text = document.read_text(encoding='utf-8')
    heading = text.index('已定制的 helm 组件')
    lines = text[heading:].splitlines(keepends=True)
    rows = [i for i, line in enumerate(lines) if line.lstrip().startswith('|')]
    assert len(rows) > 2
    del lines[rows[2]]
    document.write_text(text[:heading] + ''.join(lines), encoding='utf-8')
    run('markdown-only-spartan-registry-defect', gate, False)
    plan_file = output / 'quality-plan.json'
    run('proof-plan', [sys.executable, 'scripts/plan-quality-checks.py', '--tier', 'pr', '--output', str(plan_file)])
    baseline = json.loads(plan_file.read_text(encoding='utf-8'))
    documentation_plan = dict(baseline, Mode='documentation', FrameworkTests=False, FrameworkTestProjects=[],
                              FrameworkTestSelection='none', ConsumerProjects=[], Scenarios=[], Slices=[], ContainerSmoke=False,
                              GeneratedDocumentation=True, PackageDocumentation=False,
                              Jobs={name:name == 'template-generation' for name in baseline['Jobs']})
    proof = json.loads((args.generation_root / 'documentation-results.json').read_text(encoding='utf-8'))
    plan_file.write_text(json.dumps(documentation_plan), encoding='utf-8')
    proofs = output / 'proofs'; proofs.mkdir(exist_ok=True)
    proof_file = proofs / 'proof.json'
    checker = ['pwsh', 'scripts/check-quality-proof.ps1', '-ValidationPlanPath', str(plan_file), '-Kind', 'documentation', '-ResultsPath', str(proofs)]
    proof_file.write_text(json.dumps(proof), encoding='utf-8'); run('documentation-proof-valid', checker)
    for field, value in [('CandidateSha', 'a' * 40), ('Version', 2), ('Result', 'cancelled'), ('Result', 'failure'),
                         ('EffectiveShapes', 0), ('CiVariants', 0), ('Projects', 0), ('MarkdownFiles', 0), ('Skills', 0),
                         ('FrontendMarkdownFiles', 0), ('FormattedContents', 0)]:
        invalid = dict(proof); invalid[field] = value
        proof_file.write_text(json.dumps(invalid), encoding='utf-8')
        run('invalid-proof-' + field + '-' + str(value), checker, False)
    proof_file.unlink(); run('missing-proof', checker, False)
    proof_file.write_text(json.dumps(proof), encoding='utf-8')
    duplicate = proofs / 'duplicate.json'; duplicate.write_text(json.dumps(proof), encoding='utf-8')
    run('duplicate-proof', checker, False); duplicate.unlink()
    if args.feed:
        fixture = output / 'package-fixture'
        if fixture.exists(): shutil.rmtree(fixture)
        shutil.copytree(ROOT / 'framework', fixture / 'framework', ignore=shutil.ignore_patterns('bin', 'obj', 'artifacts'))
        (fixture / 'scripts').mkdir()
        for name in ['quality-validation-plan.ps1', 'template-matrix-scenarios.ps1', 'extract-doc-snippets.py']:
            shutil.copy2(ROOT / 'scripts' / name, fixture / 'scripts' / name)
        feed = fixture / '.tmp/feed'; shutil.copytree(args.feed, feed)
        document = fixture / 'framework/docs/components/email.md'
        text = document.read_text(encoding='utf-8')
        assert 'builder.Services.AddNullEmailSender();' in text
        document.write_text(text.replace('builder.Services.AddNullEmailSender();', 'builder.Services.MissingDocumentationFixture();', 1), encoding='utf-8')
        for package in feed.glob('*.nupkg'):
            with zipfile.ZipFile(package) as archive:
                if 'docs/email.md' not in archive.namelist(): continue
                entries = {name:archive.read(name) for name in archive.namelist()}
            entries['docs/email.md'] = document.read_bytes()
            with zipfile.ZipFile(package, 'w', zipfile.ZIP_DEFLATED) as archive:
                for name, data in entries.items(): archive.writestr(name, data)
        package_plan = dict(documentation_plan, GeneratedDocumentation=False, PackageDocumentation=True,
                            Jobs={name:name in ('framework-pack', 'package-consumption') for name in baseline['Jobs']})
        plan_file.write_text(json.dumps(package_plan), encoding='utf-8')
        run('uncompilable-packaged-markdown', ['pwsh', str(fixture / 'framework/build/test-package-consumption.ps1'),
                                             '-FeedPath', str(feed), '-ValidationPlanPath', str(plan_file)], False)
        log = (output / 'uncompilable-packaged-markdown.log').read_text(encoding='utf-8')
        assert 'Consumer build not applicable' in log and 'MissingDocumentationFixture' in log and 'error CS1061' in log
    (output / 'results.json').write_text(json.dumps(records, indent=2), encoding='utf-8')


if __name__ == '__main__':
    main()
