#!/usr/bin/env python3
"""统计生成项目典型任务读取的规范与 Skill 篇幅，检查不超过登记的上限。

口径：按生成后的内容计。对 template.json 的全部参数组合，用场景覆盖闸门的同一套求值器
（check-template-scenario-coverage.py：来源条件、文件级排除、嵌套 #if/#elif/#else）裁剪条件块，
得到该组合实际生成的文字；条件行不计，两个分支不相加，被裁掉的文件计 0。每类任务的读数取
全部组合中的最大值，即 AI 在最重的那个真实生成项目里读到的量。

读取集合：常驻部分（项目 Skill 的 SKILL.md 与 docs/README.md，每个任务都先读）单独一行读数与
上限，任务读数不再重复计它们。任务按 docs/README.md 的按任务读取表与项目 Skill 的意图路由列出
应读文件，连同这些文件要求先读的伴随文件（如 Spartan Skill 的 rules）与按任务内容必读的专题，
去重后累加字符数（Python len）。读取表随形态切换必读文件时（如新增界面文案在本地化开/关时
分别读多语言专题/界面规范），登记为 (文件, 条件)，只在条件成立的组合里计入；同一组合内求总量，
再对全部组合取最大，互斥的必读文件不相加。读取表另须一行一条路由（见 route_problems），保持常驻索引轻量。

上限 = 登记目标值 + MARGIN 余量（向上取整到百位）。超出即失败：先去重、删冗长示例，再按独立
任务主题拆分；确需更多内容时，在对应规格里写明理由后上调 TARGETS 的登记值。读数下降时不必
跟着下调。压缩或改写规范以 Skill 验收（docs/architecture/collaboration-scenarios.md §6）的
真实请求回归，字数不是唯一判据。

由 check-all 以 --check 运行（先跑 self_test 的夹具反例）；--self-test 只跑夹具。
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import math
from pathlib import Path
import re
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parent.parent
_spec = importlib.util.spec_from_file_location('scenario_coverage', ROOT / 'scripts/check-template-scenario-coverage.py')
model = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(model)

SKILL = 'template/.agents/skills/leistd-project-workflow/'
STANDARDS = 'template/docs/standards/'
INDEX = 'template/docs/README.md'
SPARTAN = 'template/.agents/skills/spartan/SKILL.md'
SPARTAN_RULES = 'template/.agents/skills/spartan/rules/'
RESIDENT = [SKILL + 'SKILL.md', INDEX]
IMPLEMENT = [SKILL + 'references/development.md', SKILL + 'references/quality.md']


def standards(*names: str) -> list[str]:
    return [STANDARDS + name for name in names]


def spartan_rules(*names: str) -> list[str]:
    return [SPARTAN_RULES + name for name in names]


# 文件 → 读它时必须同读的伴随文件。Spartan Skill“Critical rules”要求做相关工作前先读对应
# rule：任何界面工作都涉及样式与组件组合；表单规则（forms.md）只在改表单时必读，按任务登记。
REQUIRED_COMPANIONS: dict[str, list[str]] = {
    SPARTAN: spartan_rules('styling.md', 'composition.md'),
}

# 任务 → 按任务内容必读、不随某个文件自动带出的规则与专题：样例请求涉及表单，就要读 Spartan 的
# forms.md 与表单专题；新增列表页要读列表与表格、导航专题（新页面要落菜单分组与页面标题）
TASK_REQUIRED: dict[str, list[str]] = {
    'ui-change': spartan_rules('forms.md') + standards('frontend-forms.md'),
    'fullstack-crud': standards('frontend-lists.md', 'frontend-navigation.md'),
}


# 读取集合的一项：文件路径，或 (文件路径, 条件)——条件按模板条件语法求值，只在成立的组合里计入
Entry = 'str | tuple[str, str]'


def entry_path(entry) -> str:
    return entry if isinstance(entry, str) else entry[0]


def entry_applies(entry, values: dict[str, object]) -> bool:
    return isinstance(entry, str) or model.evaluate(entry[1], values)


def paths_of(files) -> list[str]:
    return [entry_path(entry) for entry in files]


# 任务 → (样例请求, 应读文件；不含常驻部分)。随形态切换的必读文件与 docs/README.md 读取表的
# 条件分支一一对应
TASKS: dict[str, tuple[str, list]] = {
    'backend-crud': ('新增一个带分页查询的资源（后端）',
                     IMPLEMENT + standards('coding-common.md', 'coding-backend.md', 'api.md', 'testing.md')),
    'fullstack-crud': ('新增上述资源并加列表页',
                       IMPLEMENT + standards('coding-common.md', 'coding-backend.md', 'api.md', 'testing.md',
                                             'coding-frontend.md', 'frontend-ui.md', 'frontend-lists.md',
                                             'frontend-navigation.md')
                       + [SPARTAN] + spartan_rules('styling.md', 'composition.md')),
    'ui-change': ('调整现有页面布局与表单',
                  IMPLEMENT + standards('coding-common.md', 'coding-frontend.md', 'frontend-ui.md', 'frontend-forms.md',
                                        'testing.md')
                  + [SPARTAN] + spartan_rules('styling.md', 'composition.md', 'forms.md')),
    'new-text': ('新增界面文案',
                 [SKILL + 'references/development.md']
                 + standards('coding-common.md', 'coding-frontend.md')
                 + [(STANDARDS + 'frontend-i18n.md', '(IncludeLocalization)'),
                    (STANDARDS + 'frontend-ui.md', '(!IncludeLocalization)')]),
    'review-only': ('只审查一个全栈改动',
                    [SKILL + 'references/quality.md']
                    + standards('coding-common.md', 'coding-backend.md', 'coding-frontend.md', 'api.md', 'testing.md')),
}
RESIDENT_NAME = 'resident'

# 余量 10%：一条规则或一行路由（几百字符）不必在别处删等量文字，又让体量翻倍前必须有人在规格里
# 写理由。取整到百位，避免登记值随单字增减抖动。
MARGIN = 0.10

# 登记目标值：2026-11-03 改为按生成内容计量时的读数（docs/specs/2026-11-03-read-cost-gate.md）。
# 上调须在对应规格里写理由；读数下降不必下调。
TARGETS = {RESIDENT_NAME: 4230, 'backend-crud': 31519, 'fullstack-crud': 64182, 'ui-change': 47059,
           'new-text': 14673, 'review-only': 35419}


def limit_of(target: int) -> int:
    return math.ceil(target * (1 + MARGIN) / 100) * 100


# ---------- 按生成内容计量 ----------

def size_model(config: dict, path: str, text: str):
    """返回 values → 该组合下 path 生成后的字符数；条件行不计，未生成的文件为 0。"""
    matches = [(condition, model.file_conditions(relative, modifiers))
               for relative, _, condition, modifiers in model.iter_sources(config, [path])]
    groups: dict[tuple, int] = {}
    lines = text.splitlines()
    contexts = dict(model.line_contexts(text))
    for lineno, line in enumerate(lines, 1):
        if lineno in contexts:
            frames = contexts[lineno]
            groups[frames] = groups.get(frames, 0) + len(line) + 1

    def size(values: dict[str, object]) -> int:
        return sum(chars for frames, chars in groups.items()
                   if any(model.generated(values, frames, condition, excludes) for condition, excludes in matches))
    return size


def readings(config: dict, texts: dict[str, str], tasks: dict[str, list], combinations: list[dict]):
    """每个任务 → (全部组合中的最大读数, 取到最大值的组合, [(文件, 该组合下字符数)])。

    同一组合内先选出该形态的读取集合（条件项只在条件成立时计入）并去重求总量，再对组合取最大。"""
    models = {path: size_model(config, path, texts[path]) for files in tasks.values() for path in paths_of(files)}
    table = {path: [size(values) for values in combinations] for path, size in models.items()}
    result = {}
    for task, files in tasks.items():
        selected = [list(dict.fromkeys(entry_path(e) for e in files if entry_applies(e, values))) for values in combinations]
        totals = [sum(table[path][i] for path in selected[i]) for i in range(len(combinations))]
        best = max(range(len(combinations)), key=totals.__getitem__)
        result[task] = (totals[best], combinations[best], [(path, table[path][best]) for path in selected[best]])
    return result


def over_limit(values: dict[str, int], limits: dict[str, int]) -> list[str]:
    return [task for task, total in values.items() if total > limits[task]]


def resident_overlap(tasks: dict[str, tuple[str, list]], resident: list[str]) -> list[str]:
    return [f'{task}：常驻部分 {path} 已单独计量，不再计入任务读数'
            for task, (_, files) in tasks.items() for path in paths_of(files) if path in resident]


# ---------- 读取表一行一条路由 ----------

def route_problems(config: dict, path: str, text: str, combinations: list[dict]) -> list[str]:
    """“## 按任务读取”节内只有一张表；每行一条路由（列数与表头一致、不用 <br> 合并），
    且任一组合生成后任务名不重复。"""
    matches = [(condition, model.file_conditions(relative, modifiers))
               for relative, _, condition, modifiers in model.iter_sources(config, [path])]
    contexts = dict(model.line_contexts(text))
    problems: list[str] = []
    rows: list[tuple[int, str]] = []
    in_section = table_started = table_ended = False
    columns = 0
    for lineno, line in enumerate(text.splitlines(), 1):
        if line.startswith('## '):
            in_section = line.strip() == '## 按任务读取'
            continue
        if not in_section or lineno not in contexts or not line.strip():
            if in_section and table_started and lineno in contexts:
                table_ended = True
            continue
        if not line.startswith('|'):
            if table_started:
                table_ended = True
                problems.append(f'{path}:{lineno} 读取表内出现非表格行，路由只写成表格行')
            continue
        if table_ended:
            problems.append(f'{path}:{lineno} 按任务读取节只放一张表')
            continue
        cells = [cell.strip() for cell in line.strip().strip('|').split('|')]
        if not table_started:
            table_started, columns = True, len(cells)
            continue
        if set(line.replace('|', '').strip()) <= set('-: '):
            continue
        if len(cells) != columns or '<br' in line.lower() or not cells[0]:
            problems.append(f'{path}:{lineno} 读取表一行一条路由：列数须为 {columns}、任务名非空、不用 <br> 合并')
        rows.append((lineno, cells[0]))
    if not table_started:
        problems.append(f'{path} 缺少“## 按任务读取”表')
    seen: set[str] = set()
    for values in combinations:
        names: dict[str, int] = {}
        for lineno, name in rows:
            if any(model.generated(values, contexts[lineno], c, e) for c, e in matches):
                if name in names and name not in seen:
                    seen.add(name)
                    problems.append(f'{path}:{lineno} 任务“{name}”在同一生成形态里重复（另见第 {names[name]} 行）')
                names[name] = lineno
    return problems


# ---------- 自检 ----------

def missing_companions(tasks: dict[str, tuple[str, list]]) -> list[str]:
    errors = []
    for task, (_, entries) in tasks.items():
        files = paths_of(entries)
        for path, companions in REQUIRED_COMPANIONS.items():
            if path in files:
                errors += [f'{task}：含 {path} 但缺伴随文件 {c}' for c in companions if c not in files]
    for required_task, required in TASK_REQUIRED.items():
        if required_task in tasks:
            files = paths_of(tasks[required_task][1])
            errors += [f'{required_task}：缺按任务必读的 {r}' for r in required if r not in files]
    return errors


def self_test() -> list[str]:
    failures: list[str] = []
    # 反例：从含 Spartan Skill 的任务里删掉一个必读伴随文件，必须报出
    task, (sample, files) = next((t, v) for t, v in TASKS.items() if SPARTAN in v[1])
    removed = REQUIRED_COMPANIONS[SPARTAN][0]
    broken = {task: (sample, [f for f in files if f != removed])}
    if not any(removed in e for e in missing_companions(broken)):
        failures.append(f'自检反例未报出：{task} 删掉 {removed}')
    # 反例：从任务里删掉按任务必读的规则，必须报出
    for required_task, required in TASK_REQUIRED.items():
        sample, files = TASKS[required_task]
        for path in required:
            broken = {required_task: (sample, [f for f in files if f != path])}
            if not any(path in e for e in missing_companions(broken)):
                failures.append(f'自检反例未报出：{required_task} 删掉 {path}')

    # 夹具：两个布尔参数，b/ 目录在 !Y 时整体裁掉
    config = {
        'symbols': {'X': {'type': 'parameter', 'datatype': 'bool', 'defaultValue': 'false'},
                    'Y': {'type': 'parameter', 'datatype': 'bool', 'defaultValue': 'false'}},
        'sources': [{'modifiers': [{'condition': '(!Y)', 'exclude': ['b/**']}]}],
    }
    combinations = model.all_combinations(config)
    texts = {
        'template/a.md': 'aaaa\n<!--#if (X)-->\n' + 'x' * 9 + '\n<!--#else-->\n' + 'y' * 4 + '\n<!--#endif-->\n',
        'template/c.md': '<!--#if (!X)-->\n' + 'z' * 19 + '\n<!--#endif-->\n',
        'template/b/d.md': 'd' * 29 + '\n',
    }
    measured = readings(config, texts, {'branch': ['template/a.md'], 'max': ['template/a.md', 'template/c.md'],
                                        'excluded': ['template/b/d.md']}, combinations)
    # 条件块：X 开读到 5+10，X 关读到 5+5；两支相加（20）或计条件行都算错
    if measured['branch'][0] != 15:
        failures.append(f'自检：条件块两分支不应相加，期望 15，实际 {measured["branch"][0]}')
    # 取最大形态：X 开 15+0、X 关 10+20；按文件各取最大再相加（15+20）是错的
    if measured['max'][0] != 30 or measured['max'][1]['X']:
        failures.append(f'自检：应取同一组合的总量最大值 30（X 关），实际 {measured["max"][0]}')
    if measured['excluded'][0] != 30 or not measured['excluded'][1]['Y']:
        failures.append('自检：文件级排除的文件只在生成它的组合里计量')
    if over_limit({'branch': 15}, {'branch': 14}) != ['branch'] or over_limit({'branch': 15}, {'branch': 15}):
        failures.append('自检：超过上限必须失败，等于上限通过')
    if limit_of(1000) != 1100 or limit_of(1001) != 1200:
        failures.append('自检：上限 = 目标值 × (1 + MARGIN) 向上取整到百位')
    # 常驻部分单独计量：任务里再列常驻文件必须报出
    if not resident_overlap({'t': ('', ['template/a.md', INDEX])}, RESIDENT) or resident_overlap(TASKS, RESIDENT):
        failures.append('自检：任务读数里重复列出常驻部分必须报出')
    if RESIDENT_NAME not in TARGETS:
        failures.append('自检：常驻部分须有单独的登记上限')
    # 随形态切换的必读文件：X 开读 i18n、X 关读 ui，互斥不相加；ui 增长后该任务在 X 关的形态超限
    variant = {'v': ['template/b/d.md', ('template/i18n.md', '(X)'), ('template/ui.md', '(!X)')]}
    vtexts = {'template/b/d.md': 'd' * 89 + '\n', 'template/i18n.md': 'i' * 389 + '\n', 'template/ui.md': 'u' * 189 + '\n'}
    got = readings(config, vtexts, variant, combinations)['v']
    if got[0] != 480 or not got[1]['X']:
        failures.append(f'自检：互斥必读文件按形态选择、不相加，期望 480（X 开 Y 开），实际 {got[0]}')
    grown = dict(vtexts, **{'template/ui.md': 'u' * 589 + '\n'})
    got = readings(config, grown, variant, combinations)['v']
    if got[0] != 680 or got[1]['X'] or not over_limit({'v': got[0]}, {'v': limit_of(480)}):
        failures.append(f'自检：替代文档增长后该任务须在其形态超限，期望 680（X 关），实际 {got[0]}')
    # 入口：同一条 run_gate 路径，超限返回 1 且有诊断，合法输入返回 0
    gate_index = 'template/docs/README.md'
    gate_texts = {gate_index: '## 按任务读取\n\n| 任务 | 必读 | 按需 |\n| --- | --- | --- |\n| 后端 | a | |\n',
                  **vtexts}
    gate_tasks = {'v': ('样例', variant['v'])}
    for name, texts_in, expected in (('合法输入退出 0', gate_texts, 0),
                                     ('超限输入非零退出并报超限', dict(gate_texts, **grown), 1)):
        lines: list[str] = []
        code = run_gate(config, texts_in, gate_tasks, [gate_index], {RESIDENT_NAME: 100, 'v': 480}, True,
                        out=lines.append, err=lines.append)
        if code != expected or (expected and not any('超过上限：v' in line for line in lines)):
            failures.append(f'自检：入口{name}，期望退出 {expected}，实际 {code}：{lines}')
    # 读取表：一行一条路由；同一形态任务名不重复，互斥分支同名不算重复
    head = '## 按任务读取\n\n说明\n\n| 任务 | 必读 | 按需 |\n| --- | --- | --- |\n'
    good = head + '| 后端 | a | |\n<!--#if (X)-->\n| 文案 | b | |\n<!--#else-->\n| 文案 | c | |\n<!--#endif-->\n\n## 其他\n'
    route_cases = [
        ('互斥分支同名的路由通过', good, 0),
        ('同一形态任务名重复', head + '| 后端 | a | |\n<!--#if (X)-->\n| 后端 | b | |\n<!--#endif-->\n', 1),
        ('用 <br> 合并两条路由', head + '| 后端<br>前端 | a | |\n', 1),
        ('表内插入说明段落', head + '| 后端 | a | |\n另见下文\n| 前端 | b | |\n', 2),
        ('缺少读取表', '## 其他\n', 1),
    ]
    for name, text, expected in route_cases:
        problems = route_problems(config, 'template/a.md', text, combinations)
        if len(problems) != expected:
            failures.append(f'自检：{name}，期望 {expected} 个问题，实际 {problems}')
    return failures


def run_gate(config: dict, texts: dict[str, str], tasks: dict[str, tuple[str, list]], resident: list[str],
             targets: dict[str, int], check: bool, show_files: bool = False,
             out=print, err=lambda line: print(line, file=sys.stderr)) -> int:
    """结构检查、计量与上限比对；--check 时任一不过返回 1。main 与自检的入口反例共用这一条路径。"""
    combinations = model.all_combinations(config)
    index = next((path for path in resident if path.endswith('docs/README.md')), None)
    errors = missing_companions(tasks) + resident_overlap(tasks, resident)
    if index:
        errors += route_problems(config, index, texts[index], combinations)
    if errors:
        for line in errors:
            err(line)
        return 1
    measured = readings(config, texts, {RESIDENT_NAME: resident} | {t: files for t, (_, files) in tasks.items()},
                        combinations)
    samples = {RESIDENT_NAME: '常驻：项目 Skill 入口与文档索引'} | {t: sample for t, (sample, _) in tasks.items()}
    limits = {task: limit_of(targets[task]) for task in measured}
    for task, (total, values, sizes) in measured.items():
        status = 'OK' if total <= limits[task] else 'OVER'
        out(f'{task:15} {total:7} 上限 {limits[task]:7} 目标 {targets[task]:7} {status}  # {samples[task]}')
        if show_files:
            shape = ', '.join(f'{k}={values[k]}' for k in model.EFFECTIVE_DIMENSIONS if k in values)
            out(f'    形态：{shape}')
            for path, size in sizes:
                out(f'    {size:7}  {path}')
    over = over_limit({task: total for task, (total, _, _) in measured.items()}, limits)
    if check and over:
        err(f'超过上限：{", ".join(over)}；先去重压缩，确需更多内容时在规格里写理由后上调 TARGETS')
        return 1
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--check', action='store_true', help='任一读数超过上限或结构检查不过时以非 0 退出')
    parser.add_argument('--files', action='store_true', help='列出读数所在组合的去重文件与字符数')
    parser.add_argument('--self-test', action='store_true', help='只运行夹具反例自检')
    args = parser.parse_args()

    errors = self_test()
    if args.self_test:
        print('\n'.join(errors) if errors else '✅ 读取成本闸门自检通过', file=sys.stderr if errors else sys.stdout)
        return 1 if errors else 0
    if errors:
        print('读取成本闸门自检失败：\n' + '\n'.join(errors), file=sys.stderr)
        return 1
    config = json.loads(model.CONFIG_PATH.read_text(encoding='utf-8'))
    texts = {path: (ROOT / path).read_text(encoding='utf-8')
             for files in [RESIDENT] + [files for _, files in TASKS.values()] for path in paths_of(files)}
    return run_gate(config, texts, TASKS, RESIDENT, TARGETS, args.check, args.files)

if __name__ == '__main__':
    sys.exit(main())
