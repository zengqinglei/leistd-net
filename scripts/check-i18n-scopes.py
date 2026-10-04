#!/usr/bin/env python3
"""前端全局与功能 scope 的键、引用、英文表和动态前缀必须一致。"""
import argparse
import json
from pathlib import Path
import re
import tempfile


def source_json(text):
    # Source catalogs contain all conditional keys; generated catalogs are strict JSON.
    # Only template directives are removed. Ordinary comments and invalid JSON still fail.
    text = re.sub(r'(?m)^[ \t]*//[ \t]*#(?:if|elif|else|endif)\b[^\n]*(?:\n|$)', '', text)
    return json.loads(text)


def flatten(node, prefix=''):
    result = {}
    for key, value in node.items():
        name = f'{prefix}.{key}' if prefix else key
        if isinstance(value, dict):
            result.update(flatten(value, name))
        else:
            result[name] = value
    return result


TABLE = re.compile(r'const ENGLISH(_VALIDATION)?: Record<string, string> = \{(.*?)\n\};', re.S)
ENTRY = re.compile(r'''(?:['"])?([\w.]+)(?:['"])?:\s*('(?:[^'\\]|\\.)*'|"(?:[^"\\]|\\.)*")\s*,''')
CALL = re.compile(r"\bt\(\s*'([\w.]+)'(?!\s*\+)")
TS_CALL = re.compile(r"(?:transloco\.translate|\btranslateSignal)\(\s*'([\w.]+)'(?!\s*\+)")
OBJECT = re.compile(r"\btranslateObjectSignal\(\s*'([\w.]+)'")
PIPE = re.compile(r"'([\w.]+)'\s*\|\s*transloco")
PREFIX = re.compile(r'''(?:prefix|read):\s*'([\w.]+)' ''', re.X)
INLINE_TEMPLATE = re.compile(r'\btemplate\s*:\s*`([^`]*)`', re.S)


def template_refs(text):
    """按结构指令的词法块还原前缀；全局与嵌套功能翻译可共存。"""
    stack = []
    refs = []
    tokens = re.compile(r'<(/?)([\w-]+)\b([^>]*)>|\bt\(\s*\'([\w.]+)\'(?!\s*\+)')
    for match in tokens.finditer(text):
        if match[4]:
            prefix = next((p for _, p in reversed(stack) if p is not None), '')
            refs.append((match[4], f'{prefix}.{match[4]}' if prefix else match[4]))
        elif match[1]:
            for i in range(len(stack) - 1, -1, -1):
                if stack[i][0] == match[2]:
                    del stack[i:]
                    break
        else:
            attr = match[3]
            prefix = None
            if '*transloco=' in attr:
                found = PREFIX.search(attr)
                prefix = found[1] if found else ''
            effective = prefix if prefix is not None else next((p for _, p in reversed(stack) if p is not None), '')
            for call in CALL.finditer(attr):
                refs.append((call[1], f'{effective}.{call[1]}' if effective else call[1]))
            if not attr.rstrip().endswith('/') and match[2] not in {'input', 'img', 'br', 'hr', 'meta', 'link'}:
                stack.append((match[2], prefix))
    return refs


def source_parts(text, separator=','):
    """只在顶层切分 TS 属性，跳过字符串、注释及嵌套括号。"""
    pieces, stack = [], []
    start, index = 0, 0
    while index < len(text):
        char = text[index]
        if text.startswith('//', index):
            end = text.find('\n', index)
            index = len(text) if end < 0 else end
            continue
        if text.startswith('/*', index):
            end = text.find('*/', index + 2)
            index = len(text) if end < 0 else end + 2
            continue
        if char in "'\"`":
            quote = char
            index += 1
            while index < len(text) and text[index] != quote:
                index += 2 if text[index] == '\\' else 1
        elif char == separator and not stack:
            pieces.append(text[start:index])
            start = index + 1
        elif char in '({[':
            stack.append(char)
        elif char in ')}]':
            if stack:
                stack.pop()
        index += 1
    return [*pieces, text[start:]]


def scope_bindings(text, context=None):
    """登记只读自官方 provider 调用，alias 映射同样由登记推导。"""
    bindings = {}
    for call in re.finditer(r'provideTranslocoScope\(', text):
        arguments = source_parts(text[call.end():], separator=')')[0]
        for argument in source_parts(arguments):
            argument = argument.strip()
            literal = re.fullmatch(r"'([\w/-]+)'", argument)
            if literal:
                bindings[literal[1]] = literal[1]
                continue
            if not argument.startswith('{') and context:
                constant = re.search(r'\bconst\s+' + re.escape(argument) + r'\s*(?::\s*[\w.<>\[\],\s]+)?=\s*\{', context)
                if constant:
                    argument = '{' + source_parts(context[constant.end():], separator='}')[0] + '}'
            scope = re.search(r"\bscope:\s*'([\w/-]+)'", argument)
            alias = re.search(r"\balias:\s*'([\w-]+)'", argument)
            if scope:
                bindings[alias[1] if alias else scope[1]] = scope[1]
    return bindings


def check_route_scopes(frontend, scopes):
    """从路由组件沿实际模板 selector 追溯嵌入组件，逐入口核对继承的 scope 登记。"""
    sources = {p.resolve(): p.read_text(encoding='utf-8') for p in (frontend / 'src').rglob('*.ts') if '.spec.' not in p.name}
    aliases = {alias: scope for text in sources.values() for alias, scope in scope_bindings(text, text).items()}
    components, selectors = {}, {}
    for path, text in sources.items():
        selector = re.search(r"\bselector:\s*'([^']+)'", text)
        if '@Component' not in text:
            continue
        template = '\n'.join(m[1] for m in INLINE_TEMPLATE.finditer(text))
        external = re.search(r"\btemplateUrl:\s*'([^']+)'", text)
        if external:
            template += (path.parent / external[1]).read_text(encoding='utf-8')
        refs = [full for _, full in template_refs(template)]
        refs += [m[1] for pattern in (TS_CALL, OBJECT, PIPE) for m in pattern.finditer(TABLE.sub('', text))]
        components[path] = (template, {aliases.get(key.split('.')[0], key.split('.')[0]) for key in refs} & scopes)
        if selector:
            selectors[selector[1]] = path

    def needs(path, visited=None):
        visited = set() if visited is None else visited
        if path in visited or path not in components:
            return set()
        visited.add(path)
        template, required = components[path]
        required = set(required)
        for tag in re.findall(r'<([\w-]+)\b', template):
            if tag in selectors:
                required |= needs(selectors[tag], visited)
        return required

    def resolve(path, module):
        target = path.parent / module
        return next((p.resolve() for p in (Path(str(target) + '.ts'), target / 'index.ts') if p.is_file()), None)

    def route_objects(text):
        # 顶层路由数组；其子路由通过 children 递归，不把 provider 对象误认成路由。
        arrays = re.finditer(r'(?:Routes\s*=|export\s+default)\s*\[', text)
        for array in arrays:
            for part in source_parts(text[array.end():], separator=','):
                part = re.sub(r'(?m)^\s*//[^\n]*', '', part).strip()
                if part.startswith('{'):
                    yield part

    def properties(route):
        # 去掉首个路由对象外壳，后面的 as Routes 不属于属性。
        closing = source_parts(route[1:], separator='}')[0]
        props = {}
        for part in source_parts(closing):
            part = re.sub(r'(?m)^\s*//[^\n]*', '', part).strip()
            match = re.match(r'(\w+)\s*:\s*(.*)', part, re.S)
            if match:
                props[match[1]] = match[2]
        return props

    route_files = {p for p in sources if p.name.endswith('.routes.ts')}
    incoming = set()
    for path in route_files:
        for child in re.finditer(r"loadChildren:\s*\(\)\s*=>\s*import\('([^']+)'\)", sources[path]):
            incoming.add(resolve(path, child[1]))
    errors = []

    def walk(path, routes, inherited, chain):
        for route in routes:
            props = properties(route)
            registered = set(inherited)
            providers = props.get('providers', '')
            registered.update(scope_bindings(providers, sources[path]).values())
            target = None
            component = props.get('loadComponent', '')
            dynamic = re.search(r"import\('([^']+)'\)", component)
            if dynamic:
                target = resolve(path, dynamic[1])
            elif 'component' in props:
                name = props['component'].strip()
                for imported in re.finditer(r"import\s*\{([^}]+)\}\s*from\s*'([^']+)'", sources[path]):
                    if re.search(r'\b' + re.escape(name) + r'\b', imported[1]) and imported[2].startswith('.'):
                        target = resolve(path, imported[2])
                        break
            missing = needs(target) - registered
            if missing:
                errors.append(f'{path.name}: host route {props.get("path", "?")} missing scope registration {sorted(missing)} for {target.name}')
            children = props.get('children', '').strip()
            if children.startswith('['):
                walk(path, source_parts(children[1:-1]), registered, chain)
            child_module = re.search(r"import\('([^']+)'\)", props.get('loadChildren', ''))
            if child_module:
                child = resolve(path, child_module[1])
                if child in route_files and child not in chain:
                    walk(child, route_objects(sources[child]), registered, chain | {child})

    for path in route_files - incoming:
        walk(path, route_objects(sources[path]), set(), {path})
    return errors


def check(frontend):
    errors = []
    resources = frontend / 'public/i18n'
    en = {}
    dirs = [resources, *sorted(p for p in resources.rglob('*') if p.is_dir())]
    for directory in dirs:
        label = str(directory.relative_to(resources))
        trees = {}
        for lang in ('en', 'zh-CN'):
            path = directory / f'{lang}.json'
            try:
                trees[lang] = flatten(source_json(path.read_text(encoding='utf-8')))
            except (OSError, ValueError) as error:
                errors.append(f'{label}/{lang}: invalid or missing JSON: {error}')
        if len(trees) != 2:
            continue
        if trees['en'].keys() != trees['zh-CN'].keys():
            errors.append(f'{label}: scope key sets differ: {sorted(trees["en"].keys() ^ trees["zh-CN"].keys())}')
        prefix = '' if directory == resources else label.replace('/', '.') + '.'
        for lang, entries in trees.items():
            for key, value in entries.items():
                if not isinstance(value, str) or not value.strip():
                    errors.append(f'{label}/{lang}:{key}: empty or non-string translation')
                    continue
                stripped = re.sub(r'\{\{[^}]+\}\}', '', value)
                if re.search(r'\{[A-Za-z]\w*\}', stripped):
                    errors.append(f'{label}/{lang}:{key}: invalid Transloco interpolation')
        for key, value in trees['en'].items():
            full = prefix + key
            if full in en:
                errors.append(f'{full}: duplicate global/scope key')
            en[full] = value
            if isinstance(value, str) and isinstance(trees['zh-CN'].get(key), str):
                placeholders = lambda text: set(re.findall(r'\{\{(\w+)\}\}', text))
                if placeholders(value) != placeholders(trees['zh-CN'][key]):
                    errors.append(f'{full}: placeholders differ')
    validation_found = False
    for path in (frontend / 'src').rglob('*.ts'):
        if '.spec.' in path.name:
            continue
        text = path.read_text(encoding='utf-8')
        for alias, scope in scope_bindings(text, text).items():
            if alias != scope:
                en.update({alias + key[len(scope):]: value for key, value in list(en.items()) if key.startswith(scope + '.')})
    for path in sorted((frontend / 'src').rglob('*')):
        if path.suffix not in ('.ts', '.html') or '.spec.' in path.name:
            continue
        text = path.read_text(encoding='utf-8')
        code = TABLE.sub('', text)
        if path.suffix == '.html':
            refs = template_refs(text)
        else:
            refs = [ref for inline in INLINE_TEMPLATE.finditer(code) for ref in template_refs(inline[1])]
            code = INLINE_TEMPLATE.sub('', code)
            refs += [(m[1], m[1]) for m in CALL.finditer(code)]
        refs += [(m[1], m[1]) for pattern in (TS_CALL, PIPE) for m in pattern.finditer(TABLE.sub('', text))]
        for _, key in refs:
            if key not in en:
                errors.append(f'{path.name}: missing reference {key}')
        for m in OBJECT.finditer(text):
            if not any(k.startswith(m[1] + '.') for k in en):
                errors.append(f'{path.name}: empty object prefix {m[1]}')
        for table in TABLE.finditer(text):
            html = path.with_suffix('.html')
            prefixes = {raw: full for raw, full in template_refs(html.read_text(encoding='utf-8'))} if html.exists() else {}
            values = {}
            for m in ENTRY.finditer(table[2]):
                raw = m[1]
                key = prefixes.get(raw, raw)
                value = re.sub(r'\\(.)', r'\1', m[2][1:-1])
                values[raw] = value
                if key not in en or value != en[key]:
                    errors.append(f'{path.name}: English table differs for {key}')
            for raw, _ in (template_refs(html.read_text(encoding='utf-8')) if html.exists() else []) + [(m[1], m[1]) for m in CALL.finditer(TABLE.sub('', text))]:
                if raw not in values:
                    errors.append(f'{path.name}: English table missing {raw}')
            if table[1]:
                validation_found = True
                for key in en:
                    if key.startswith('validation.') and key not in values:
                        errors.append(f'{path.name}: validation table missing {key}')
        if path.suffix == '.ts' and "from '@angular/forms/signals'" in text:
            kinds = set(re.findall(r"\bkind:\s*'(\w+)'", text))
            imported = re.search(r"import \{([^}]*)\} from '@angular/forms/signals'", text)
            if imported:
                kinds.update(set(re.findall(r'\b(required|minLength|maxLength|email|min|max)\b', imported[1])))
            if any('error:' not in m[1] for m in re.finditer(r'\bpattern\((.*?)\);', text, re.S)):
                kinds.add('pattern')
            for kind in kinds:
                if f'validation.{kind}' not in en:
                    errors.append(f'{path.name}: missing validation.{kind}')
    if not validation_found:
        errors.append('missing ENGLISH_VALIDATION table')
    errors += check_route_scopes(frontend, {p.name for p in dirs if p != resources})
    return errors


def self_test():
    # 每项变异独立运行，避免其它错误遮住失效的判据。
    temporary_root = Path(__file__).resolve().parents[1] / '.tmp'
    temporary_root.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='i18n-scopes-', dir=temporary_root) as tmp:
        root = Path(tmp)
        (root / 'src').mkdir()
        (root / 'public/i18n/users').mkdir(parents=True)
        def write(lang, value):
            (root / f'public/i18n/users/{lang}.json').write_text(json.dumps(value))
        global_data = {'validation': {'required': 'Required'}, 'common': {'save': 'Save'}}
        for lang in ('en', 'zh-CN'):
            (root / f'public/i18n/{lang}.json').write_text(json.dumps(global_data))
            write(lang, {'title': 'Users', 'nested': {'label': 'Label {{name}}'}})
        html = root / 'src/page.html'
        original = '''<ng-container *transloco="let t; prefix: 'users'">{{ t('title') }}<ng-container *transloco="let t">{{ t('common.save') }}</ng-container></ng-container>'''
        html.write_text(original)
        ts = root / 'src/page.ts'
        original_ts = """const ENGLISH: Record<string, string> = {
  'title': 'Users',
  'common.save': 'Save',
};
const ENGLISH_VALIDATION: Record<string, string> = {
  'validation.required': 'Required',
};
const obj = translateObjectSignal('users.nested', {}, { scope: 'users' });
import { required } from '@angular/forms/signals';
"""
        ts.write_text(original_ts)
        # 一文件只含一张表是当前组件惯例，校验提示表独立在 util 中。
        ts.write_text(original_ts.split('const ENGLISH_VALIDATION')[0] + original_ts.split('};\n', 2)[2])
        (root / 'src/util.ts').write_text("const ENGLISH_VALIDATION: Record<string, string> = {\n  'validation.required': 'Required',\n};\n")
        assert check(root) == [], check(root)
        (root / 'public/i18n/permissions').mkdir()
        for lang in ('en', 'zh-CN'):
            (root / f'public/i18n/permissions/{lang}.json').write_text(json.dumps({'title': 'Grant'}))
        (root / 'src/grant.ts').write_text('''@Component({ selector: 'example-grant', template: `<div *transloco="let t">{{ t('permissions.title') }}</div>` }) class Grant {}''')
        original += '<example-grant />'
        html.write_text(original)
        ts.write_text(ts.read_text() + "\n@Component({ selector: 'example-page', templateUrl: './page.html' }) class Page {}\n")
        routes = root / 'src/app.routes.ts'
        original_routes = """export const routes: Routes = [{ path: 'page', providers: [provideTranslocoScope('users', 'permissions')], loadComponent: () => import('./page').then(m => m.Page) }];"""
        routes.write_text(original_routes)
        aliased_routes = original_routes.replace("'users', 'permissions'", "'users', { scope: 'permissions', alias: 'grants' }")
        grant = root / 'src/grant.ts'
        original_grant = grant.read_text()
        grant.write_text(original_grant.replace('permissions.title', 'grants.title'))
        routes.write_text(aliased_routes)
        assert check(root) == [], check(root)
        routes.write_text(original_routes)
        grant.write_text(original_grant)
        assert check(root) == [], check(root)
        inherited_routes = """export const routes: Routes = [{ path: '', providers: [provideTranslocoScope('users', 'permissions')], children: [{ path: 'page', loadComponent: () => import('./page').then(m => m.Page) }] }];"""
        routes.write_text(inherited_routes)
        assert check(root) == [], check(root)
        routes.write_text("const USERS_SCOPE: ProviderScope = { scope: 'users' };\nconst PERMISSIONS_SCOPE: ProviderScope = { scope: 'permissions', alias: 'grants' };\n" + aliased_routes.replace("'users', { scope: 'permissions', alias: 'grants' }", "USERS_SCOPE, PERMISSIONS_SCOPE"))
        grant.write_text(original_grant.replace('permissions.title', 'grants.title'))
        assert check(root) == [], check(root)
        routes.write_text(original_routes)
        grant.write_text(original_grant)
        selectorless_ts = ts.read_text().replace("selector: 'example-page', ", '')
        ts.write_text(selectorless_ts)
        assert check(root) == [], check(root)
        ts.write_text(selectorless_ts.replace("templateUrl: './page.html'", "template: `<div *transloco=\"let t\">{{ t('permissions.title') }}</div>`"))
        assert check_route_scopes(root, {'users', 'permissions'}) == []
        ts.write_text(selectorless_ts)
        cases = [
            ('ordinary JSON comment remains invalid', lambda: (root / 'public/i18n/users/en.json').write_text('// not a template directive\n' + json.dumps({'title': 'Users', 'nested': {'label': 'Label {{name}}'}})), 'invalid or missing JSON'),
            ('conditional malformed JSON remains invalid', lambda: (root / 'public/i18n/users/en.json').write_text('{\n//#if (Impersonation)\n"title": "Users"\n//#endif\n"nested": {"label": "Label {{name}}"}\n}'), 'invalid or missing JSON'),
            ('selector-less route scope registration', lambda: (ts.write_text(selectorless_ts.replace("templateUrl: './page.html'", "template: `<div *transloco=\"let t\">{{ t('permissions.title') }}</div>`")), routes.write_text(original_routes.replace("'users', 'permissions'", "'users'"))), "missing scope registration ['permissions'] for page.ts"),
            ('scope key sets', lambda: write('zh-CN', {}), 'key sets'),
            ('scope reference', lambda: html.write_text(original.replace("t('title')", "t('absent')")), 'missing reference users.absent'),
            ('attribute reference', lambda: html.write_text(original.replace("{{ t('title') }}", "<span [title]=\"t('absent')\"></span>")), 'missing reference users.absent'),
            ('inline reference', lambda: ts.write_text(ts.read_text() + '''\nconst c = { template: `<div *transloco="let t; prefix: 'users'">{{ t('absent') }}</div>` };'''), 'missing reference users.absent'),
            ('English value', lambda: ts.write_text(ts.read_text().replace("'Users'", "'Wrong'")), 'English table differs'),
            ('English coverage', lambda: ts.write_text(ts.read_text().replace("  'title': 'Users',\n", '')), 'English table missing'),
            ('object prefix', lambda: ts.write_text(ts.read_text().replace("'users.nested'", "'users.absent'")), 'empty object prefix'),
            ('validation kind', lambda: ts.write_text(ts.read_text() + "const e = { kind: 'absent' };"), 'missing validation.absent'),
            ('validation table', lambda: (root / 'src/util.ts').write_text(''), 'missing ENGLISH_VALIDATION'),
            ('placeholder', lambda: write('zh-CN', {'title': 'Users', 'nested': {'label': 'Label'}}), 'placeholders differ'),
            ('Chinese empty value', lambda: write('zh-CN', {'title': '', 'nested': {'label': 'Label {{name}}'}}), 'empty or non-string translation'),
            ('Chinese interpolation', lambda: write('zh-CN', {'title': 'Users', 'nested': {'label': '用户 {name}'}}), 'zh-CN:nested.label: invalid Transloco interpolation'),
            ('Chinese non-string value', lambda: write('zh-CN', {'title': 1, 'nested': {'label': 'Label {{name}}'}}), 'empty or non-string translation'),
            ('interpolation', lambda: write('en', {'title': 'Users', 'nested': {'label': 'Label {name}'}}), 'invalid Transloco interpolation'),
            ('missing pair', lambda: (root / 'public/i18n/users/zh-CN.json').unlink(), 'invalid or missing JSON'),
            ('duplicate', lambda: (root / 'public/i18n/en.json').write_text(json.dumps({**global_data, 'users': {'title': 'Users'}})), 'duplicate global/scope key'),
            ('embedded scope registration', lambda: routes.write_text(original_routes.replace("'users', 'permissions'", "'users'")), 'missing scope registration'),
            ('second host registration', lambda: routes.write_text(original_routes.replace('}];', "}, { path: 'other', providers: [provideTranslocoScope('users')], loadComponent: () => import('./page').then(m => m.Page) }];")), 'missing scope registration'),
            ('aliased second host registration', lambda: (routes.write_text(aliased_routes.replace('}];', "}, { path: 'other', providers: [provideTranslocoScope('users')], loadComponent: () => import('./page').then(m => m.Page) }];")), grant.write_text(original_grant.replace('permissions.title', 'grants.title'))), 'missing scope registration'),
        ]
        baseline = {p: p.read_text() for p in root.rglob('*') if p.is_file()}
        for lang in ('en', 'zh-CN'):
            path = root / 'public/i18n/users' / f'{lang}.json'
            path.write_text('{\n//#if (Impersonation)\n"title": "Users",\n//#endif\n"nested": {"label": "Label {{name}}"}\n}')
        assert check(root) == [], check(root)
        for p, text in baseline.items():
            p.write_text(text)
        for name, mutate, expected in cases:
            for p, text in baseline.items():
                p.write_text(text)
            mutate()
            assert any(expected in e for e in check(root)), (name, check(root))
        assert template_refs(original.replace('prefix:', 'read:')) == [('title', 'users.title'), ('common.save', 'common.save')]
        assert template_refs('''<div *transloco="let t; prefix: 'users'"><input [title]="t('title')" /></div>''') == [('title', 'users.title')]
        assert scope_bindings("provideTranslocoScope(SCOPE)", "const SCOPE = { scope: 'users', alias: 'people' };") == {'people': 'users'}
        assert scope_bindings("provideTranslocoScope(SCOPE)", "const SCOPE: ProviderScope = { scope: 'users', alias: 'people' };") == {'people': 'users'}
        print(f'PASS: {len(cases)} independent rule mutations and prefix/read nesting')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--self-test', action='store_true')
    parser.add_argument('--repo-root', type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    if args.self_test:
        self_test()
    else:
        problems = check(args.repo_root / 'template/frontend')
        print('\n'.join(problems) if problems else 'PASS: frontend scope resources, references, English tables and validation')
        raise SystemExit(bool(problems))
