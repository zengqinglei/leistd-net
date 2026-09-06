#!/usr/bin/env python3
"""模板源码在条件裁剪下的静态检查。

文件名只说了第一项，实际检查五项——都建立在同一遍「守卫条件解析 + 在全部符号取值
组合上求值」之上，所以聚在一个脚本里而不是拆成五个入口：

1. **C# using 守卫**：using 被条件 S 守卫、其命名空间下的类型在条件 U 下被引用时，
   必须 U ⟹ S——否则存在一组取值让"类型在、using 不在"，生成后报 CS0246。
   反方向同样查：using 不得**宽于**命名空间的可用性（含 `sources.modifiers` 排除）。
2. **前端 TS import 守卫**：同一不变量，按符号级取 `#if`/`#else` 分支的并集；
   `of`/`in` 这类与 rxjs 同名的 JS 关键字走 denylist，不当作导入符号。
3. **`InternalsVisibleTo` 必须无条件**：放进条件块会让某些场景丢掉它，
   而依赖它的测试工程仍被生成。
4. **csproj XML 良构**：条件块的插入/删除不对称时会留下未闭合标签，
   而其它闸门都看不见畸形 XML。
5. **无仅含空行的条件块**：裁剪后留下的空块会被 prettier/lint 判为格式问题。

判定在**全部符号取值组合**上求值，而不是靠人工维护蕴含关系表；因此比只编译
8 个场景的矩阵更严，能覆盖矩阵没排到的组合。
"""
import io, os, re, sys, json, fnmatch, itertools, collections
from xml.etree import ElementTree

ROOT = os.path.join(os.path.dirname(__file__), '..')
SRC = os.path.join(ROOT, 'template/backend')
FRAMEWORK_SRC = os.path.join(ROOT, 'framework')
TEMPLATE_JSON = os.path.join(ROOT, 'template/.template.config/template.json')

DIR_RE = re.compile(r'^\s*#(if|elif|else|endif)\b\s*(?:\(\s*(.*?)\s*\))?\s*$')
NS_RE = re.compile(r'^\s*namespace\s+([\w.]+)', re.M)
USING_RE = re.compile(r'^\s*using\s+(?!static)([\w.]+)\s*;')
DECL_RE = re.compile(r'\b(?:class|interface|record|struct|enum)\s+([A-Z]\w*)')


def load_exclusions():
    """读取 `sources.modifiers` 的条件排除，返回 [(condition, [glob, ...])]。

    不认识排除会让检查对**在该场景下根本不生成的文件**报警。本仓库有大量这类文件
    （`Application/Auth/**`、`Application/Tenants/**` 等整目录按条件排除），
    漏掉这一层会把噪声当成缺陷，而噪声会让整道闸门失去可信度。
    """
    modifiers = json.load(io.open(TEMPLATE_JSON, encoding='utf-8-sig'))['sources'][0]['modifiers']
    return [(m.get('condition'), m.get('exclude', [])) for m in modifiers if m.get('condition')]


def file_exists_in(env, template_relative, exclusions):
    """该文件在这组符号取值下是否会被生成"""
    for condition, globs in exclusions:
        if eval_expr(condition, env) is not True:
            continue
        for pattern in globs:
            if fnmatch.fnmatch(template_relative, pattern):
                return False
            # `dir/**` 应当匹配 dir 下任意深度
            if pattern.endswith('/**') and template_relative.startswith(pattern[:-3] + '/'):
                return False
    return True


def load_symbol_space():
    """从 template.json 枚举全部符号取值组合，computed 由 parameter 推导。"""
    symbols = json.load(io.open(TEMPLATE_JSON, encoding='utf-8-sig'))['symbols']
    choices, flags, computed = {}, [], {}
    for name, spec in symbols.items():
        kind = spec.get('type')
        if kind == 'parameter' and spec.get('datatype') == 'choice':
            choices[name] = [c['choice'] for c in spec['choices']]
        elif kind == 'parameter' and spec.get('datatype') == 'bool':
            flags.append(name)
        elif kind == 'computed':
            computed[name] = spec['value']

    names = list(choices) + flags
    space = []
    for combo in itertools.product(*([choices[c] for c in choices] + [[True, False]] * len(flags))):
        env = dict(zip(names, combo))
        for cname, expr in computed.items():
            env[cname] = eval_expr(expr, env)
        space.append(env)
    return space, sorted(set(names) | set(computed))


def eval_expr(expr, env):
    """求值模板引擎的条件表达式（!、&&、||、==、!=、括号、"字符串"）。"""
    py = expr
    py = re.sub(r'(?<![!=<>])==', '==', py)
    py = py.replace('&&', ' and ').replace('||', ' or ')
    py = re.sub(r'!(?=[\w(])', ' not ', py)
    py = re.sub(r'"([^"]*)"', r"'\1'", py)
    try:
        return bool(eval(py, {'__builtins__': {}}, dict(env)))
    except Exception:
        return None   # 无法求值：交给 check-template-symbols 的悬空符号检查


def conj(conds, env):
    """条件栈全部成立？任一项无法求值则整体视为无法求值。"""
    for c in conds:
        v = eval_expr(c, env)
        if v is None:
            return None
        if not v:
            return False
    return True


def guard_map(lines):
    """行号 -> 生效的条件栈"""
    out, stack = {}, []
    for i, l in enumerate(lines, 1):
        m = DIR_RE.match(l)
        if m:
            kind, cond = m.group(1), m.group(2)
            if kind == 'if':
                stack.append(cond or 'false')
            elif kind == 'elif' and stack:
                stack[-1] = '!(%s) && (%s)' % (stack[-1], cond or 'false')
            elif kind == 'else' and stack:
                stack[-1] = '!(%s)' % stack[-1]
            elif kind == 'endif' and stack:
                stack.pop()
        out[i] = tuple(stack)
    return out


XML_DIR_RE = re.compile(r'^\s*<!--\s*#(if|elif|else|endif)\b(?:\s*\(\s*(.*?)\s*\))?\s*-->\s*$')
IVT_RE = re.compile(r'<InternalsVisibleTo\b')


TS_DIR_RE = re.compile(r'^\s*//#(if|elif|else|endif)\b(?:\s*\(\s*(.*?)\s*\))?\s*$')
JS_KEYWORDS = frozenset({
    'of', 'in', 'as', 'is', 'from', 'type', 'new', 'this', 'default', 'get', 'set',
})

TS_IMPORT_RE = re.compile(r"^\s*import\s+(?:type\s+)?(?:\{(?P<named>[^}]*)\}|(?P<default>[A-Za-z_$][\w$]*))\s+from\s+'(?P<spec>[^']+)';")


def check_typescript_imports(space):
    """前端 import 的守卫必须被它所有使用点的守卫蕴含。

    与 C# 那侧同一条不变量，只是语言不同。最常见的形态是把 import 插在
    "最后一个 import 之后"或按字母序插入时，恰好落进某个 `//#if` 块——
    条件为假的场景里符号消失，要到构建才报错。本仓库为此付过多轮。

    条件块内的 import 本身是正当的，只要**用它的地方也在同一条件下**；
    因此判据不是"import 有没有被守卫"，而是"有没有某组符号取值让使用点在、import 不在"。
    """
    root = os.path.join(ROOT, 'template/frontend/src')
    if not os.path.isdir(root):
        print('⚠️  前端源码目录不存在，可能已被重命名：template/frontend/src')
        return 1

    problems = []
    for dp, dn, fn in os.walk(root):
        dn[:] = [d for d in dn if d not in ('node_modules',)]
        for name in fn:
            if not name.endswith('.ts'):
                continue

            path = os.path.join(dp, name)
            lines = io.open(path, encoding='utf-8', errors='replace').read().split('\n')

            guards, stack = {}, []
            for i, line in enumerate(lines, 1):
                m = TS_DIR_RE.match(line)
                if m:
                    kind, cond = m.group(1), m.group(2)
                    if kind == 'if':
                        stack.append(cond or 'false')
                    elif kind == 'elif' and stack:
                        stack[-1] = '!(%s) && (%s)' % (stack[-1], cond or 'false')
                    elif kind == 'else' and stack:
                        stack[-1] = '!(%s)' % stack[-1]
                    elif kind == 'endif' and stack:
                        stack.pop()
                guards[i] = tuple(stack)

            imports = []
            for i, line in enumerate(lines, 1):
                m = TS_IMPORT_RE.match(line)
                if not m or not guards[i]:
                    continue
                if m.group('named'):
                    names = [n.split(' as ')[-1].strip()
                             for n in m.group('named').split(',') if n.strip()]
                else:
                    names = [m.group('default')]
                imports.append((i, guards[i], [n for n in names if n]))

            # 同一符号可能在多个互斥分支里各导入一次（典型是 #if / #else 各一行）。
            # 有效可用性是这些守卫的**析取**——只看单条 import 会把正当写法误报。
            by_symbol = {}
            for line_no, import_guard, names in imports:
                for symbol in names:
                    by_symbol.setdefault(symbol, []).append((line_no, import_guard))

            for symbol, sites in by_symbol.items():
                # 与 JS/TS 关键字同名的导入（最典型是 rxjs 的 `of`）无法用正则与语法区分：
                # `for (const x of [...])` 里的 `of` 会被当成使用点。
                # 这类符号直接跳过——放过它们，好过让整道闸门产出必须逐条豁免的噪声。
                if symbol in JS_KEYWORDS:
                    continue
                if True:
                    pattern = re.compile(r'(?<![\w$.])' + re.escape(symbol) + r'(?![\w$])')
                    import_lines = {ln for ln, _ in sites}

                    def available(env, sites=sites):
                        return any(conj(g, env) is True for _, g in sites)

                    for j, other in enumerate(lines, 1):
                        if j in import_lines or TS_DIR_RE.match(other) or TS_IMPORT_RE.match(other):
                            continue
                        st = other.strip()
                        if st.startswith(('//', '*', '/*')):
                            continue
                        if not pattern.search(other):
                            continue

                        usage_guard = guards[j]
                        bad = next((env for env in space
                                    if conj(usage_guard, env) is True and not available(env)), None)
                        if bad:
                            problems.append(
                                (os.path.relpath(path, ROOT), sites[0][0], symbol, sites[0][1],
                                 j, usage_guard))
                            break

    seen = set()
    for path, line_no, symbol, import_guard, usage_line, usage_guard in problems:
        key = (path, line_no, symbol)
        if key in seen:
            continue
        seen.add(key)
        print(f'❌ {path}')
        print(f'   import {symbol} 受守卫 {" && ".join(import_guard)}（第 {line_no} 行）')
        print(f'   但在 {" && ".join(usage_guard) or "<无条件>"} 下被使用（第 {usage_line} 行）')
    if seen:
        print('\n条件为假的场景里符号会消失。把 import 移到条件块外，或让使用点受同样的守卫。')
    return len(seen)


EMPTY_BLOCK_DIR_RE = re.compile(r'^\s*(?://|<!--)?\s*#(if|elif|else|endif)\b')


def check_no_empty_conditional_blocks():
    """条件块内不得只剩空行。

    删掉块内唯一一条语句却留下 `#if`/`#endif` 时，模板源看不出异常（指令各占一行），
    生成后指令被剥离，那里就多出一个空行——条件为真的场景 prettier 报格式错误，
    而报错位置与真正的原因（某处删了内容）相距甚远。
    """
    problems = []
    for dp, dn, fn in os.walk(os.path.join(ROOT, 'template')):
        dn[:] = [d for d in dn if d not in ('node_modules', 'obj', 'bin', 'dist')]
        for name in fn:
            if not name.endswith(('.ts', '.cs', '.html', '.json', '.csproj')):
                continue
            path = os.path.join(dp, name)
            lines = io.open(path, encoding='utf-8', errors='replace').read().split('\n')
            start = None
            for i, line in enumerate(lines):
                stripped = line.strip()
                if not EMPTY_BLOCK_DIR_RE.match(stripped):
                    continue
                if '#if' in stripped:
                    start = i
                elif '#endif' in stripped and start is not None:
                    body = [x.strip() for x in lines[start + 1:i]]
                    if body and all(x == '' for x in body):
                        problems.append((os.path.relpath(path, ROOT), start + 1, i + 1))
                    start = None

    for path, start, end in problems:
        print(f'❌ {path}:{start}-{end} 条件块内只剩空行')
    if problems:
        print('\n块内内容被删后应连同 #if/#endif 一起删除，'
              '否则生成产物会多出空行并在格式检查处报错。')
    return len(problems)


def check_project_files_are_well_formed():
    """所有 `.csproj` 必须是结构完整的 XML。

    模板指令 `<!--#if ... -->` 本身就是合法 XML 注释，所以未生成的模板源也能直接解析——
    标签不配对一定是真损坏。加这条是因为踩过一次：手工编辑 csproj 时留下重复的
    `<ItemGroup>` 开标签，而当时唯一的 csproj 检查只看某个元素在不在条件块里，
    看不见结构错误，于是"检查通过"给了虚假信心，直到矩阵在第 7 个场景才炸。
    """
    problems = []
    for dp, dn, fn in os.walk(os.path.join(ROOT, 'template')):
        dn[:] = [d for d in dn if d not in ('obj', 'bin', 'node_modules')]
        for name in fn:
            if not name.endswith('.csproj'):
                continue
            path = os.path.join(dp, name)
            try:
                ElementTree.parse(path)
            except ElementTree.ParseError as error:
                problems.append((os.path.relpath(path, ROOT), str(error)))

    for path, error in problems:
        print(f'❌ {path} 不是结构完整的 XML：{error}')
    return len(problems)


def check_internals_visible_to():
    """`InternalsVisibleTo` 不得被条件守卫。

    测试程序集在所有场景下都存在，而功能开关决定的是产品代码。把它放进某个
    `#if` 块里，等于让"测试能不能看见内部类型"随一个无关的开关变化——
    条件为假的场景下测试直接编译不过。本仓库踩过一次：它被插在
    `<!--#if (IncludeLocalization)-->` 的下一行，于是所有不含本地化的场景全红。
    """
    problems = []
    for dp, dn, fn in os.walk(os.path.join(ROOT, 'template')):
        dn[:] = [d for d in dn if d not in ('obj', 'bin', 'node_modules')]
        for name in fn:
            if not name.endswith('.csproj'):
                continue
            path = os.path.join(dp, name)
            depth = 0
            for i, line in enumerate(
                    io.open(path, encoding='utf-8', errors='replace').read().split('\n'), 1):
                m = XML_DIR_RE.match(line)
                if m:
                    kind = m.group(1)
                    if kind == 'if':
                        depth += 1
                    elif kind == 'endif':
                        depth = max(0, depth - 1)
                    continue
                if depth > 0 and IVT_RE.search(line):
                    problems.append((os.path.relpath(path, ROOT), i, line.strip()))

    for path, line, text in problems:
        print(f'❌ {path}:{line} InternalsVisibleTo 处于条件块内')
        print(f'   {text}')
    if problems:
        print('\n测试程序集在所有场景下都存在；把 InternalsVisibleTo 放进条件块会让'
              '条件为假的场景编译不过。')
    return len(problems)


def main():
    space, all_symbols = load_symbol_space()
    exclusions = load_exclusions()
    ivt_problems = (check_internals_visible_to()
                    + check_project_files_are_well_formed()
                    + check_typescript_imports(space)
                    + check_no_empty_conditional_blocks())

    files = []
    for dp, dn, fn in os.walk(SRC):
        dn[:] = [d for d in dn if d not in ('obj', 'bin')]
        files += [os.path.join(dp, n) for n in fn if n.endswith('.cs')]

    ns_types = collections.defaultdict(set)
    ns_guards = collections.defaultdict(list)
    for p in files:
        txt = io.open(p, encoding='utf-8', errors='replace').read()
        m = NS_RE.search(txt)
        if not m:
            continue
        ns_types[m.group(1)].update(DECL_RE.findall(txt))

        # 命名空间的"存在条件"要看**类型声明处**的守卫，不能看文件首行。
        # 文件完全可以是"开头一段被守卫的 using，随后是无条件内容"——
        # 按首行判断会把这类文件整个误判成条件生成，从而报出大量假阳性。
        decl_lines = txt.split('\n')
        decl_guards = guard_map(decl_lines)
        for line_no, line in enumerate(decl_lines, 1):
            if DECL_RE.search(line) and not line.strip().startswith(('//', '///', '*')):
                stack = decl_guards[line_no]
                rel_decl = os.path.relpath(p, os.path.join(ROOT, 'template')).replace(os.sep, '/')
                ns_guards[m.group(1)].append(
                    (rel_decl, ' && '.join(stack) if stack else None))
                break

    # Framework packages are external to generated projects, but their public types are still
    # needed to detect a conditional using that is narrower than an unconditional type usage.
    for dp, dn, fn in os.walk(FRAMEWORK_SRC):
        dn[:] = [d for d in dn if d not in ('obj', 'bin')]
        for name in fn:
            if not name.endswith('.cs'):
                continue
            txt = io.open(os.path.join(dp, name), encoding='utf-8', errors='replace').read()
            if m := NS_RE.search(txt):
                ns_types[m.group(1)].update(DECL_RE.findall(txt))

    problems = []
    for p in files:
        lines = io.open(p, encoding='utf-8', errors='replace').read().split('\n')
        gm = guard_map(lines)
        local_types = set(DECL_RE.findall('\n'.join(lines)))
        usings = [(i, m.group(1)) for i, l in enumerate(lines, 1)
                  if (m := USING_RE.match(l)) and gm[i]]
        if not usings:
            continue
        for i, ns in usings:
            types = ns_types.get(ns)
            if not types:
                continue                       # 外部/框架命名空间，不在本仓库内
            S = gm[i]
            for j, l2 in enumerate(lines, 1):
                if j == i or USING_RE.match(l2) or DIR_RE.match(l2):
                    continue
                st = l2.strip()
                if st.startswith(('//', '///', '*', '/*')):
                    continue
                hit = next((t for t in types
                            if t not in local_types
                            if re.search(r'(?<![\w.])' + re.escape(t) + r'(?![\w])', l2)), None)
                if not hit:
                    continue
                U = gm[j]
                rel = os.path.relpath(p, os.path.join(ROOT, 'template')).replace(os.sep, '/')
                bad = next((env for env in space
                            if file_exists_in(env, rel, exclusions)
                            and conj(U, env) is True and conj(S, env) is False), None)
                if bad:
                    problems.append((p, i, ns, S, j, hit, U, bad))

    # 反方向：using 不得**宽于**它所引用命名空间的存在条件。
    # 上面查的是"using 比用法窄"，这里查的是"命名空间在某些场景下根本不存在，
    # 而 using 仍无条件写着"。两者是同一个错误的两个方向，各自都漏不得——
    # 本仓库先后各踩过一次，而每一次另一个方向的检查都是绿的。
    for source in files:
        lines = io.open(source, encoding='utf-8', errors='replace').read().split('\n')
        source_guards = guard_map(lines)
        for i, line in enumerate(lines, 1):
            m = USING_RE.match(line)
            if not m:
                continue
            declared = ns_guards.get(m.group(1))
            if not declared:
                continue

            # 命名空间在某组取值下是否可用：**声明它的文件既要被生成、其声明处守卫也要成立**。
            # 只看守卫会漏掉整目录按条件排除的情形——那类文件源码里根本没有 #if，
            # 却在该场景下不存在。本仓库就是这么漏掉一次的。
            def namespace_available(env, declared=declared):
                return any(file_exists_in(env, rel_decl, exclusions)
                           and (guard is None or eval_expr(guard, env))
                           for rel_decl, guard in declared)

            using_guard = source_guards[i]
            rel = os.path.relpath(source, os.path.join(ROOT, 'template')).replace(os.sep, '/')
            bad = next((env for env in space
                        if file_exists_in(env, rel, exclusions)
                        and conj(using_guard, env) is not False
                        and not namespace_available(env)), None)
            if bad:
                print(f'❌ {os.path.relpath(source, ROOT)}:{i} using {m.group(1)}')
                where = " || ".join(g or f'{d}（无条件声明但按场景排除）'
                                    for d, g in declared)
                print(f'   该命名空间只在 {where} 下存在，'
                      f'而此处守卫为 {" && ".join(using_guard) or "<无条件>"}')
                print(f'   反例取值：{ {k: v for k, v in bad.items() if k in all_symbols} }')
                ivt_problems += 1

    if not problems:
        if ivt_problems:
            return 1
        print(f'✅ using 守卫检查通过（{len(files)} 个文件，{len(space)} 组符号取值）；'
              f'csproj 结构完整、InternalsVisibleTo 均无条件、前端 import 守卫正确。')
        return 0

    seen = set()
    for p, i, ns, S, j, t, U, bad in problems:
        key = (p, i, U)
        if key in seen:
            continue
        seen.add(key)
        rel = os.path.relpath(p, ROOT)
        print(f'❌ {rel}')
        print(f'   using {ns} 受守卫 {" && ".join(S)}（第 {i} 行）')
        print(f'   但 {t} 在 {" && ".join(U) or "<无条件>"} 下被引用（第 {j} 行）')
        env = {k: v for k, v in bad.items() if k in all_symbols}
        print(f'   反例取值：{env}')
    print(f'\n共 {len(seen)} 处 using 守卫窄于用法。')
    return 1


if __name__ == '__main__':
    sys.exit(main())
