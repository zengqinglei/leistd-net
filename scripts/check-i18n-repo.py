#!/usr/bin/env python3
"""只在本仓库成立的多语言判据；项目自身的判据在随模板分发的 template/scripts/check-i18n.py。

  1. 框架组件随包译文（自动枚举 framework/components/**/Resources）：culture、键集合、`{Name}` 占位符一致。
     这些键是公共契约——宿主按键覆盖组件译文，错误码按键找句子，抛异常那侧按占位符名传参。
  2. 框架错误码形如 `<所有者>:<名称>`，在框架与模板之间全局唯一，带资源的组件每个码都有句子。
  3. 宿主资源不复制组件译文：同名键会覆盖组件自带的句子，组件改文案时旧句子被静默钉死。
  4. 不启用多语言时组件内联的英文表（ENGLISH / ENGLISH_VALIDATION）与英文词条逐键相同，覆盖模板用到的键，
     校验提示表覆盖全部 `validation.*`。两个分支只在模板源码里同时存在，生成项目里无从比对。

解析与比对逻辑全部复用分发脚本，这里不另写一份。
自检：`python3 scripts/check-i18n-repo.py --self-test`。
"""
import importlib.util
import json
from pathlib import Path
import re
import shutil
import sys
import tempfile

REPO = Path(__file__).resolve().parents[1]
_spec = importlib.util.spec_from_file_location('check_i18n', REPO / 'template/scripts/check-i18n.py')
project = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(project)

# 宿主确要改写组件文案的键，逐条写明原因。
HOST_OVERRIDE_ALLOWED = {
    # 补充模板自带的迁移入口（ConnectionStrings:MigrationTarget、DbMigrator --apply）：组件不知道宿主用什么迁移工具
    'Tenant:DedicatedDatabaseMissing',
    'Tenant:DedicatedDatabaseNotMigrated',
    'Tenant:DedicatedDatabaseUnreachable',
}
FRAMEWORK_CODE = re.compile(r'[A-Z][A-Za-z0-9]*:[A-Z][A-Za-z0-9]*')
ENTRY = re.compile(r'''(?:['"])?([\w.]+)(?:['"])?:\s*('(?:[^'\\]|\\.)*'|"(?:[^"\\]|\\.)*")\s*,''')


def check_framework(repo):
    errors = []
    components = repo / 'framework/components'
    resource_dirs = sorted(d for d in components.rglob('Resources')
                           if d.is_dir() and all((d / f'{lang}.json').is_file() for lang in project.LOCALES)
                           and not {'bin', 'obj'} & set(d.relative_to(components).parts))
    if not resource_dirs:
        return ['framework: no component resource directory found; the enumeration is probably wrong']

    template_src = repo / 'template/backend/src'
    owners = {code: project.relative(path, repo)
              for layer in ('*.Domain', '*.Application') for p in sorted(template_src.glob(layer))
              for path, _, code in project.error_code_constants(p)}
    for path, _, code in project.error_code_constants(components):
        name = project.relative(path, repo)
        if not FRAMEWORK_CODE.fullmatch(code):
            errors.append(f'{name}: framework error code {code} must look like Owner:Name')
        if code in owners:
            errors.append(f'{name}: duplicate error code {code} (also {owners[code]})')
        owners.setdefault(code, name)

    host_dir = next(template_src.glob('*.Api/Resources'), None)
    host_texts = project.compare_resource_pair('host', host_dir)[0] if host_dir else None
    for directory in resource_dirs:
        label = f'framework {directory.parent.name}'
        texts, problems = project.compare_resource_pair(label, directory)
        errors += problems
        if texts is None:
            continue
        for path, _, code in project.error_code_constants(directory.parent):
            if code not in texts:
                errors.append(f'{project.relative(path, repo)}: error code {code} has no resource entry in {label}')
        for key in sorted(set(texts) & set(host_texts or {}) - HOST_OVERRIDE_ALLOWED):
            errors.append(f'host resources copy {key} from {directory.parent.name}: delete the host entry, '
                          'or add it to HOST_OVERRIDE_ALLOWED with the reason')
    return errors


def check_english_tables(frontend):
    en, _, errors = project.load_frontend_catalog(frontend)
    if errors:
        return []  # 词条本身的问题由分发脚本报告，这里不重复
    errors = []
    validation_found = False
    for path in project.source_files(frontend / 'src', {'.ts', '.html'}):
        text = path.read_text(encoding='utf-8')
        html = path.with_suffix('.html')
        html_refs = project.template_refs(html.read_text(encoding='utf-8')) if html.exists() else []
        prefixes = dict(html_refs)
        for table in project.TABLE.finditer(text):
            values = {}
            for m in ENTRY.finditer(table[2]):
                key = prefixes.get(m[1], m[1])
                values[m[1]] = re.sub(r'\\(.)', r'\1', m[2][1:-1])
                if key not in en or values[m[1]] != en[key]:
                    errors.append(f'{path.name}: English table differs for {key}')
            for raw, _ in html_refs + [(m[1], m[1]) for m in project.CALL.finditer(project.TABLE.sub('', text))]:
                if raw not in values:
                    errors.append(f'{path.name}: English table missing {raw}')
            if table[1]:
                validation_found = True
                for key in en:
                    if key.startswith('validation.') and key not in values:
                        errors.append(f'{path.name}: validation table missing {key}')
    if not validation_found:
        errors.append('missing ENGLISH_VALIDATION table')
    return errors


def check(repo):
    return check_framework(repo) + check_english_tables(repo / 'template/frontend')


def self_test():
    def write(name, text):
        def apply(root):
            path = root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding='utf-8')
        return apply

    def edit(name, old, new):
        def apply(root):
            path = root / name
            text = path.read_text(encoding='utf-8')
            assert old in text, (name, old)
            path.write_text(text.replace(old, new), encoding='utf-8')
        return apply

    component = 'framework/components/settings/Leistd.Settings.Core'

    def fixture(root):
        project.write_fixture(root / 'template')
        write('template/frontend/src/util.ts', "const ENGLISH_VALIDATION: Record<string, string> = {\n  'validation.required': 'Required',\n};\n")(root)
        for lang, sentence in (('en', 'Setting {Name} is undefined.'), ('zh-CN', '未定义设置 {Name}。')):
            write(f'{component}/Resources/{lang}.json', json.dumps({'culture': lang, 'texts': {'Setting:Undefined': sentence}}))(root)
        write(f'{component}/Errors/SettingErrorCodes.cs', 'public static class SettingErrorCodes { public const string Undefined = "Setting:Undefined"; }')(root)

    page = 'template/frontend/src/page.ts'
    host_en = 'template/backend/src/Demo.Api/Resources/en.json'
    host_zh = 'template/backend/src/Demo.Api/Resources/zh-CN.json'
    component_zh = f'{component}/Resources/zh-CN.json'
    def host_copies(key):
        def apply(root):
            edit(host_en, '"User:NotFound"', f'"{key}": "x",\n    "User:NotFound"')(root)
            edit(host_zh, '"User:NotFound"', f'"{key}": "x", "User:NotFound"')(root)
            for lang in ('en', 'zh-CN'):
                edit(f'{component}/Resources/{lang}.json', '"texts": {', f'"texts": {{"{key}": "y", ')(root)
        return apply

    valid = [
        ('fixture as written', lambda root: None),
        ('allow-listed host override', host_copies('Tenant:DedicatedDatabaseMissing')),
    ]
    cases = [
        ('no component resources', lambda root: shutil.rmtree(root / f'{component}/Resources'), 'no component resource directory found'),
        ('framework key sets', edit(component_zh, '"Setting:Undefined"', '"Setting:Missing"'), 'key sets differ'),
        ('framework placeholders', edit(component_zh, '{Name}', '{Key}'), 'placeholders differ for Setting:Undefined'),
        ('framework culture', edit(component_zh, '"culture": "zh-CN"', '"culture": "zh"'), 'does not match file name zh-CN'),
        ('framework code format', edit(f'{component}/Errors/SettingErrorCodes.cs', '"Setting:Undefined"', '"setting.undefined"'), 'must look like Owner:Name'),
        ('framework code collides with template', edit(f'{component}/Errors/SettingErrorCodes.cs', 'public const string Undefined', 'public const string NotFound = "User:NotFound";\n    public const string Undefined'), 'duplicate error code User:NotFound'),
        ('framework code without resource', edit(f'{component}/Errors/SettingErrorCodes.cs', 'public const string Undefined', 'public const string Locked = "Setting:Locked";\n    public const string Undefined'), 'error code Setting:Locked has no resource entry'),
        ('host copies component key', host_copies('Setting:Copied'), 'host resources copy Setting:Copied'),
        ('English value', edit(page, "'Users'", "'Wrong'"), 'English table differs for users.title'),
        ('English coverage', edit(page, "  'title': 'Users',\n", ''), 'English table missing title'),
        ('validation table coverage', lambda root: [write(f'template/frontend/public/i18n/{lang}.json', json.dumps({'validation': {'required': 'Required', 'email': 'Invalid email'}, 'common': {'save': 'Save'}}))(root) for lang in project.LOCALES], 'validation table missing validation.email'),
        ('validation table', write('template/frontend/src/util.ts', ''), 'missing ENGLISH_VALIDATION table'),
    ]
    failures = []
    for name, mutate, *expected in [(n, m) for n, m in valid] + cases:
        with tempfile.TemporaryDirectory(prefix='check-i18n-repo-') as tmp:
            root = Path(tmp)
            fixture(root)
            mutate(root)
            errors = check(root)
        if expected and not any(expected[0] in e for e in errors):
            failures.append(f'{name}: expected "{expected[0]}", got {errors}')
        if not expected and errors:
            failures.append(f'{name}: expected no problem, got {errors}')
    if failures:
        print('FAIL: check-i18n-repo self-test')
        for failure in failures:
            print(f'  - {failure}')
        return 1
    print(f'PASS: check-i18n-repo self-test ({len(valid)} valid fixtures, {len(cases)} independent rule mutations)')
    return 0


def main():
    if '--self-test' in sys.argv[1:]:
        return self_test()
    problems = check(REPO)
    if problems:
        print(f'FAIL: repository i18n check found {len(problems)} problem(s):')
        for problem in problems:
            print(f'  - {problem}')
        return 1
    print('PASS: framework resources and error codes, host does not copy component texts, English tables match')
    return 0


if __name__ == '__main__':
    sys.exit(main())
