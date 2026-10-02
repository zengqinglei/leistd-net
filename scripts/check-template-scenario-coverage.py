#!/usr/bin/env python3
"""模板场景覆盖闸门：每一行条件代码都必须由 PR 档的某个场景生成。

为什么需要：PR 只跑场景子集，完整十场景放在合入后、夜间与发布。子集能否代表全集，
取决于"有没有哪行代码只在子集之外的场景里出现"。这个问题不能靠人记——新增一个
`#if (IncludeNotifications && !IncludeLocalization)` 分支，就可能让某个原本冗余的场景
变成唯一的覆盖者，而 PR 档照样全绿。

这是行覆盖，不是组合覆盖：同一产物里几处条件分支一起编译、lint 的交互（未使用的 import、
折行）不在 PR 档保证之内，由合入后的 full 档兜底。

判据按模板引擎的语义求值：文件级 modifiers（含第二个来源的 condition）、嵌套的
#if/#elif/#elseif/#else，以及 template.json 的 computed 符号。对每个条件行算出
"哪些登记场景会生成它"，要求：

  1. 至少一个 PR 档场景生成它（否则 PR 档漏测这行）；
  2. 只要 24 种参数组合里有一种会生成它，就至少有一个登记场景生成它（否则矩阵本身有洞）。

场景与档位的唯一定义在 template-matrix-scenarios.ps1，这里经 pwsh 读取，不重抄清单。
块结构是否配平由 check-template-conditional-blocks.py 负责；本闸门假定结构合法。
"""

from __future__ import annotations

import fnmatch
import itertools
import json
import re
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
TEMPLATE_ROOT = REPO_ROOT / "template"
CONFIG_PATH = TEMPLATE_ROOT / ".template.config" / "template.json"
SCENARIOS_SCRIPT = REPO_ROOT / "scripts" / "template-matrix-scenarios.ps1"

# 与 check-template-conditional-blocks.py 同一套注释载体，另认 #elseif（模板引擎的同义写法）
DIRECTIVE = re.compile(r'^\s*(?:<!--\s*|//\s*|/\*\s*)?#(if|elseif|elif|else|endif)\b(.*)$')
EXPR_TAIL = re.compile(r'\s*(?:-->|\*/)\s*$')
TOKEN = re.compile(r'\s*(?:(\(|\)|&&|\|\||==|!=|!)|"([^"]*)"|([A-Za-z_]\w*))')


def evaluate(expr: str, values: dict[str, object]) -> bool:
    """按模板引擎的 C 风格表达式求值；未知符号为 false。"""
    parts: list[str] = []
    pos = 0
    expr = EXPR_TAIL.sub("", expr).strip()
    while pos < len(expr):
        match = TOKEN.match(expr, pos)
        if not match:
            raise ValueError(f"无法解析条件表达式：{expr!r}")
        op, literal, name = match.groups()
        if op:
            parts.append({"&&": " and ", "||": " or ", "!": " not "}.get(op, op))
        elif literal is not None:
            parts.append(repr(literal))
        elif name in ("true", "false"):
            parts.append(name.capitalize())
        else:
            parts.append(repr(values.get(name, False)))
        pos = match.end()
    return bool(eval("".join(parts), {"__builtins__": {}}))  # noqa: S307 —— 只含上面翻译出的字面量与运算符


def symbol_values(config: dict, arguments: dict[str, object]) -> dict[str, object]:
    symbols = config["symbols"]
    values: dict[str, object] = {}
    for name, symbol in symbols.items():
        if symbol.get("type") != "parameter":
            continue
        default = symbol.get("defaultValue")
        if symbol.get("datatype") == "bool":
            default = str(default).lower() == "true"
        values[name] = arguments.get(name, default)
    # computed 可以引用其他 computed，按声明顺序求值直到稳定
    for _ in range(len(symbols)):
        for name, symbol in symbols.items():
            if symbol.get("type") == "computed":
                values[name] = evaluate(symbol["value"], values)
    return values


def all_combinations(config: dict) -> list[dict[str, object]]:
    choices: list[tuple[str, list[object]]] = []
    for name, symbol in config["symbols"].items():
        if symbol.get("type") != "parameter":
            continue
        if symbol.get("datatype") == "bool":
            choices.append((name, [False, True]))
        elif symbol.get("datatype") == "choice":
            choices.append((name, [c["choice"] for c in symbol["choices"]]))
    return [
        symbol_values(config, dict(zip([n for n, _ in choices], combo)))
        for combo in itertools.product(*[v for _, v in choices])
    ]


def parse_cli_arguments(config: dict, args: list[str]) -> dict[str, object]:
    """把 dotnet new 参数（--service-role Resource、--include-notifications）映射回符号。"""
    by_long_name: dict[str, str] = {}
    for name, symbol in config["symbols"].items():
        if symbol.get("type") == "parameter":
            kebab = re.sub(r"(?<!^)(?=[A-Z])", "-", name).lower()
            by_long_name[f"--{kebab}"] = name
    result: dict[str, object] = {}
    index = 0
    while index < len(args):
        flag = args[index]
        if flag not in by_long_name:
            raise ValueError(f"场景参数 {flag} 不对应 template.json 中的任何参数")
        name = by_long_name[flag]
        symbol = config["symbols"][name]
        if symbol.get("datatype") == "bool":
            if index + 1 < len(args) and args[index + 1] in ("true", "false"):
                result[name] = args[index + 1] == "true"
                index += 2
            else:
                result[name] = True
                index += 1
        else:
            result[name] = args[index + 1]
            index += 2
    return result


def glob_match(path: str, pattern: str) -> bool:
    """模板引擎的 glob：`**/` 可以匹配零层目录。"""
    return fnmatch.fnmatch(path, pattern) or (pattern.startswith("**/") and fnmatch.fnmatch(path, pattern[3:]))


def iter_sources(config: dict, files: list[str]):
    """按 template.json 的 sources 产出 (相对来源根的路径, 仓库内路径, 来源条件, modifiers)。"""
    for source in config["sources"]:
        root = (source.get("source") or "./").removeprefix("./").rstrip("/")
        prefix = f"template/{root}/" if root else "template/"
        excludes = source.get("exclude", [])
        for path in files:
            if not path.startswith(prefix):
                continue
            relative = path[len(prefix):]
            if any(glob_match(relative, pattern) for pattern in excludes):
                continue
            yield relative, path, source.get("condition"), source.get("modifiers", [])


def line_contexts(text: str):
    """逐行产出 (行号, 条件帧)；帧为 (当前分支表达式, 此前同块分支表达式列表)。"""
    stack: list[list] = []
    for lineno, line in enumerate(text.splitlines(), 1):
        match = DIRECTIVE.match(line)
        if match:
            kind, expr = match.groups()
            if kind == "if":
                stack.append([expr, []])
            elif kind in ("elif", "elseif") and stack:
                stack[-1][1].append(stack[-1][0])
                stack[-1][0] = expr
            elif kind == "else" and stack:
                stack[-1][1].append(stack[-1][0])
                stack[-1][0] = "true"
            elif kind == "endif" and stack:
                stack.pop()
            continue
        yield lineno, tuple((current, tuple(previous)) for current, previous in stack)


def coverage_problems(
    config: dict,
    files: dict[str, str],
    scenarios: dict[str, dict],
) -> tuple[list[str], int]:
    """返回 (问题列表, 检查过的条件行数)。files 为 仓库内路径 → 文本。"""
    scenario_values = {
        name: symbol_values(config, parse_cli_arguments(config, info["Arguments"]))
        for name, info in scenarios.items()
    }
    pr_scenarios = {name for name, info in scenarios.items() if "pr" in info["Shards"]}
    combinations = all_combinations(config)

    problems: list[str] = []
    checked = 0
    for relative, path, source_condition, modifiers in iter_sources(config, sorted(files)):
        file_conditions = [
            modifier["condition"]
            for modifier in modifiers
            if any(glob_match(relative, pattern) for pattern in modifier.get("exclude", []))
        ]

        def generated(values: dict[str, object], frames) -> bool:
            if source_condition and not evaluate(source_condition, values):
                return False
            if any(evaluate(condition, values) for condition in file_conditions):
                return False
            return all(
                evaluate(current, values) and not any(evaluate(p, values) for p in previous)
                for current, previous in frames
            )

        for lineno, frames in line_contexts(files[path]):
            if not frames and not file_conditions and not source_condition:
                continue
            checked += 1
            producers = {name for name, values in scenario_values.items() if generated(values, frames)}
            if not producers:
                if any(generated(values, frames) for values in combinations):
                    problems.append(f"{path}:{lineno} 有参数组合会生成这一行，但没有任何登记场景生成它")
            elif not producers & pr_scenarios:
                problems.append(
                    f"{path}:{lineno} 只由非 PR 档场景生成（{', '.join(sorted(producers))}）；"
                    "调整 PR 档场景或改写条件"
                )
    return problems, checked


def load_scenarios() -> dict[str, dict]:
    command = (
        f". '{SCENARIOS_SCRIPT}'; "
        "$AllScenarios | ForEach-Object { [ordered]@{ Name = $_; Arguments = @($scenarioMap[$_].Arguments); "
        "Shards = $scenarioMap[$_].Shards } } | ConvertTo-Json -Depth 4 -AsArray"
    )
    completed = subprocess.run(
        ["pwsh", "-NoProfile", "-Command", command],
        capture_output=True, text=True, encoding="utf-8",
    )
    if completed.returncode != 0:
        raise SystemExit(f"❌ 读取场景定义失败：\n{completed.stderr.strip() or completed.stdout.strip()}")
    return {item["Name"]: item for item in json.loads(completed.stdout)}


def self_test() -> int:
    config = {
        "symbols": {
            "Role": {"type": "parameter", "datatype": "choice", "defaultValue": "A",
                     "choices": [{"choice": "A"}, {"choice": "B"}]},
            "X": {"type": "parameter", "datatype": "bool", "defaultValue": "false"},
            "Y": {"type": "parameter", "datatype": "bool", "defaultValue": "false"},
            "IsA": {"type": "computed", "value": '(Role == "A")'},
        },
        "sources": [
            {"exclude": ["**/.template.config/**"], "modifiers": [{"condition": "(!X)", "exclude": ["only-x/**"]}]},
            {"source": "./.template.config/y", "target": "./", "condition": "Y"},
        ],
    }
    # 两个 PR 场景（全关、全开），一个只在全集的单特性场景 x-only
    scenarios = {
        "base": {"Arguments": [], "Shards": {"full": 1, "pr": 1}},
        "all": {"Arguments": ["--x", "--y"], "Shards": {"full": 1, "pr": 1}},
        "x-only": {"Arguments": ["--x"], "Shards": {"full": 2}},
        "b-role": {"Arguments": ["--role", "B"], "Shards": {"full": 2, "pr": 2}},
    }
    cases: list[tuple[str, dict[str, str], int]] = [
        ("单符号两侧都由 PR 场景生成", {"template/a.ts": "//#if (X)\nx\n//#else\nnot x\n//#endif\n"}, 0),
        ("X 开 Y 关只由 x-only 生成 → 红", {"template/a.ts": "//#if (X && !Y)\nx\n//#endif\n"}, 1),
        ("嵌套等价于 X 开 Y 关 → 红", {"template/a.cs": "#if (X)\n#if (!Y)\nx\n#endif\n#endif\n"}, 1),
        ("#else 分支取反后由 x-only 唯一生成 → 红", {"template/a.html": "<!--#if (!X || Y) -->\na\n<!--#else -->\nb\n<!--#endif -->\n"}, 1),
        ("#elseif 之后的分支排除前面的条件", {"template/a.cs": "#if (X && Y)\na\n#elseif (X)\nb\n#endif\n"}, 1),
        ("文件级 modifier：only-x 下的 !Y 行只在 x-only → 红", {"template/only-x/f.ts": "//#if (!Y)\nz\n//#endif\n"}, 1),
        ("文件级 modifier：only-x 下的无条件行由 all 生成", {"template/only-x/f.ts": "plain\n"}, 0),
        ("第二来源的 condition 生效：Y 下的 !X 行无人生成但组合可达 → 红",
         {"template/.template.config/y/g.ts": "//#if (!X)\nq\n//#endif\n"}, 1),
        ("computed 符号求值", {"template/a.ts": "//#if (!IsA && X)\nb\n//#endif\n"}, 1),
        ("任何组合都不生成的行不报", {"template/a.ts": "//#if (X && !X)\nnever\n//#endif\n"}, 0),
        ("Role 取 B 的行由 b-role 生成", {"template/a.ts": "//#if (!IsA)\nb\n//#endif\n"}, 0),
    ]
    failures = 0
    for name, files, expected in cases:
        problems, _ = coverage_problems(config, files, scenarios)
        ok = len(problems) == expected
        failures += 0 if ok else 1
        print(f"  {'✅' if ok else '❌'} {name}" + ("" if ok else f"：期望 {expected} 个问题，实际 {problems}"))
    if failures:
        print(f"❌ 场景覆盖规则自检失败：{failures}/{len(cases)} 个用例不符")
        return 1
    print(f"✅ 场景覆盖规则自检通过（{len(cases)} 个用例）")
    return 0


def main() -> int:
    if "--self-test" in sys.argv:
        return self_test()

    config = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
    listed = subprocess.run(
        ["git", "-C", str(REPO_ROOT), "-c", "core.quotepath=off", "ls-files", "--cached", "--others", "--exclude-standard", "--", "template"],
        check=True, capture_output=True, text=True, encoding="utf-8",
    ).stdout.splitlines()
    files: dict[str, str] = {}
    for path in listed:
        if "/node_modules/" in path:
            continue
        try:
            files[path] = (REPO_ROOT / path).read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue

    scenarios = load_scenarios()
    problems, checked = coverage_problems(config, files, scenarios)
    if problems:
        print("❌ 模板场景覆盖检查失败：")
        for problem in problems:
            print(f"  - {problem}")
        return 1

    pr = [name for name, info in scenarios.items() if "pr" in info["Shards"]]
    print(f"✅ 模板场景覆盖检查通过（{checked} 个条件行；PR 档 {len(pr)} 个场景覆盖全部，登记场景覆盖全部可达组合）。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
