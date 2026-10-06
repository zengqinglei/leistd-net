#!/usr/bin/env python3
"""统计生成项目典型任务读取的规范与 Skill 篇幅，检查不超过登记的上限。

口径：模板源码（含条件标记、全部功能开启）；每类任务按 docs/README.md 的按任务读取表与
项目 Skill 的意图路由列出应读文件，去重后累加字符数（Python len）。篇幅调整需要人工判断，
本脚本不纳入 check-all，调整模板文档结构或篇幅时手动运行。
"""
from __future__ import annotations

import argparse
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent.parent
SKILL = 'template/.agents/skills/leistd-project-workflow/'
STANDARDS = 'template/docs/standards/'
SPARTAN = 'template/.agents/skills/spartan/SKILL.md'
COMMON = [SKILL + 'SKILL.md', 'template/docs/README.md']
IMPLEMENT = COMMON + [SKILL + 'references/development.md', SKILL + 'references/quality.md']


def standards(*names: str) -> list[str]:
    return [STANDARDS + name for name in names]


# 任务 → (样例请求, 应读文件)
TASKS: dict[str, tuple[str, list[str]]] = {
    'backend-crud': ('新增一个带分页查询的资源（后端）',
                     IMPLEMENT + standards('coding-common.md', 'coding-backend.md', 'api.md', 'testing.md')),
    'fullstack-crud': ('新增上述资源并加列表页',
                       IMPLEMENT + standards('coding-common.md', 'coding-backend.md', 'api.md', 'testing.md',
                                             'coding-frontend.md', 'frontend-ui.md') + [SPARTAN]),
    'ui-change': ('调整现有页面布局与表单',
                  IMPLEMENT + standards('coding-common.md', 'coding-frontend.md', 'frontend-ui.md', 'testing.md')
                  + [SPARTAN]),
    'new-text': ('新增界面文案',
                 COMMON + [SKILL + 'references/development.md']
                 + standards('coding-common.md', 'coding-frontend.md', 'frontend-i18n.md')),
    'review-only': ('只审查一个全栈改动',
                    COMMON + [SKILL + 'references/quality.md']
                    + standards('coding-common.md', 'coding-backend.md', 'coding-frontend.md', 'api.md', 'testing.md')),
}

# 基线提交 e984db19（规范分层之前）的读数，仅供对比
BASELINE = {'backend-crud': 53365, 'fullstack-crud': 89680, 'ui-change': 51672,
            'new-text': 33266, 'review-only': 78841}

# 当前上限：调整后不得回升；有意放宽时连同理由一并修改
LIMITS = {'backend-crud': 33747, 'fullstack-crud': 58291, 'ui-change': 39271,
          'new-text': 19055, 'review-only': 39404}


def measure(task: str) -> tuple[int, list[tuple[str, int]]]:
    files = list(dict.fromkeys(TASKS[task][1]))
    sizes = [(path, len((ROOT / path).read_text(encoding='utf-8'))) for path in files]
    return sum(size for _, size in sizes), sizes


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--check', action='store_true', help='任一任务超过上限时以非 0 退出')
    parser.add_argument('--files', action='store_true', help='列出每类任务的去重文件与字符数')
    args = parser.parse_args()

    over = []
    for task, (sample, _) in TASKS.items():
        total, sizes = measure(task)
        change = (total - BASELINE[task]) * 100 / BASELINE[task]
        status = 'OK' if total <= LIMITS[task] else 'OVER'
        print(f'{task:15} {total:7} 上限 {LIMITS[task]:7} 基线 {BASELINE[task]:7} ({change:+.1f}%) {status}  # {sample}')
        if args.files:
            for path, size in sizes:
                print(f'    {size:7}  {path}')
        if total > LIMITS[task]:
            over.append(task)

    if args.check and over:
        print(f'超过上限：{", ".join(over)}', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
