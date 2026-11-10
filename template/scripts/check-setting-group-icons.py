#!/usr/bin/env python3
"""系统设置的每个后端分组都要在前端登记导航图标，登记的图标都要能画出来。

面板由后端分组决定，前端 `GROUP_ICONS` 只管外观：没登记的分组静默退回通用齿轮图标，登记了却没放进
`provideIcons` 的图标根本画不出来。两种错漏都不会让测试变红，只有用户在页面上才看得出。

判据：
  1. `SettingGroupPermissions` 的每个分组键（`[SettingConstant.Groups.X]`，经 `SettingConstant.Groups` 解析出字符串值）
     都在 `system-settings.ts` 的 `GROUP_ICONS` 里登记了图标。
  2. `GROUP_ICONS` 里的每个图标都在同一文件的 `provideIcons({ … })` 里提供。
  3. 兜底图标（`GROUP_ICONS[…] ?? '…'`）同样在 `provideIcons` 里提供。

注释里的条目不算。生成项目里的源码已经按参数裁剪，脚本不处理条件块。

用法：`python3 scripts/check-setting-group-icons.py`；判据自检：`python3 scripts/check-setting-group-icons.py --self-test`。
"""
from pathlib import Path
import re
import sys
import tempfile

# 脚本所在目录的上一级就是被检查的根：生成项目里是项目根，本仓里是 template/。
ROOT = Path(__file__).resolve().parents[1]

PERMISSIONS = 'backend/src/*.Application/Settings/Provider/SettingGroupPermissions.cs'
CONSTANTS = 'backend/src/*.Application/Settings/Provider/SettingConstant.cs'
FRONTEND = 'frontend/src/app/features/settings/components/system-settings/system-settings.ts'

GROUPS_CLASS = re.compile(r'\bclass\s+Groups\s*\{(.*?)\n\s*\}', re.S)
CONSTANT = re.compile(r'\bconst\s+string\s+(\w+)\s*=\s*"([^"]*)"')
GROUP_KEY = re.compile(r'\[\s*SettingConstant\s*\.\s*Groups\s*\.\s*(\w+)\s*\]')
ICON_TABLE = re.compile(r'\bGROUP_ICONS\b[^=]*=\s*\{(.*?)\}', re.S)
ICON_ENTRY = re.compile(r'''(\w+)\s*:\s*['"](\w+)['"]''')
PROVIDED = re.compile(r'\bprovideIcons\s*\(\s*\{(.*?)\}\s*\)', re.S)
FALLBACK = re.compile(r'''\bGROUP_ICONS\s*\[[^\]]+\]\s*\?\?\s*['"](\w+)['"]''')


def code_only(text):
    """去掉注释：注释里的条目不是真实登记。"""
    text = re.sub(r'/\*[\s\S]*?\*/', lambda m: '\n' * m[0].count('\n'), text)
    return '\n'.join(re.sub(r'(^|[^:"\'])//.*$', r'\1', line) for line in text.split('\n'))


def read_one(root, pattern):
    found = sorted(root.glob(pattern))
    if len(found) != 1:
        return None, f'{pattern}: expected exactly one file, found {len(found)}'
    return code_only(found[0].read_text(encoding='utf-8')), None


def check_project(root):
    errors = []
    permissions, error = read_one(root, PERMISSIONS)
    constants, error2 = read_one(root, CONSTANTS)
    frontend, error3 = read_one(root, FRONTEND)
    errors += [e for e in (error, error2, error3) if e]
    if errors:
        return errors

    block = GROUPS_CLASS.search(constants)
    if not block:
        return [f'{CONSTANTS}: class Groups not found']
    values = dict(CONSTANT.findall(block.group(1)))
    groups = []
    for key in GROUP_KEY.findall(permissions):
        if key not in values:
            errors.append(f'SettingConstant.Groups.{key} is used by SettingGroupPermissions but not declared')
        else:
            groups.append(values[key])
    if not groups and not errors:
        errors.append(f'{PERMISSIONS}: no setting group found')

    table = ICON_TABLE.search(frontend)
    provided = PROVIDED.search(frontend)
    fallback = FALLBACK.search(frontend)
    if not table:
        return errors + [f'{FRONTEND}: GROUP_ICONS not found']
    if not provided:
        return errors + [f'{FRONTEND}: provideIcons({{ ... }}) not found']
    icons = dict(ICON_ENTRY.findall(table.group(1)))
    available = set(re.findall(r'\b(\w+)\b', provided.group(1)))

    for group in groups:
        if group not in icons:
            errors.append(f'setting group {group} has no icon in GROUP_ICONS (it would fall back to the generic icon)')
    for icon in sorted(set(icons.values()) - available):
        errors.append(f'icon {icon} in GROUP_ICONS is not provided by provideIcons, so it cannot render')
    if not fallback:
        errors.append(f'{FRONTEND}: fallback icon (GROUP_ICONS[...] ?? \'...\') not found')
    elif fallback.group(1) not in available:
        errors.append(f'fallback icon {fallback.group(1)} is not provided by provideIcons, so it cannot render')
    return errors


# ---------------------------------------------------------------- 自检

FIXTURE = {
    'backend/src/Demo.Application/Settings/Provider/SettingConstant.cs': '''public static class SettingConstant
{
    public static class Groups
    {
        public const string Display = "Display";
        public const string Operations = "Operations";
    }
}
''',
    'backend/src/Demo.Application/Settings/Provider/SettingGroupPermissions.cs': '''public static class SettingGroupPermissions
{
    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>
    {
        [SettingConstant.Groups.Display] = PermissionConstant.Settings.Display,
        [SettingConstant.Groups.Operations] = PermissionConstant.Settings.Operations,
    };
}
''',
    FRONTEND: '''const GROUP_ICONS: Readonly<Record<string, string>> = {
  Display: 'lucideGlobe',
  Operations: 'lucideActivity',
  // Retired: 'lucideArchive',
};

@Component({
  providers: [provideIcons({ lucideActivity, lucideGlobe, lucideSettings })],
})
export class SystemSettings {
  icon = GROUP_ICONS[group.key] ?? 'lucideSettings';
}
''',
}


def write_fixture(root):
    for relative, text in FIXTURE.items():
        path = root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding='utf-8')


def edit(relative, old, new):
    def apply(root):
        path = root / relative
        text = path.read_text(encoding='utf-8')
        assert old in text, (relative, old)
        path.write_text(text.replace(old, new), encoding='utf-8')
    return apply


def self_test():
    permissions = 'backend/src/Demo.Application/Settings/Provider/SettingGroupPermissions.cs'
    constants = 'backend/src/Demo.Application/Settings/Provider/SettingConstant.cs'
    valid = [
        ('fixture as written', lambda root: None),
        ('icon table with a trailing generic entry', edit(FRONTEND, "  Operations: 'lucideActivity',",
                                                          "  Operations: 'lucideActivity',\n  Other: 'lucideSettings',")),
    ]
    cases = [
        ('group without an icon', edit(FRONTEND, "  Display: 'lucideGlobe',\n", ''),
         'setting group Display has no icon in GROUP_ICONS'),
        ('commented-out icon does not count', edit(FRONTEND, "  Display: 'lucideGlobe',", "  // Display: 'lucideGlobe',"),
         'setting group Display has no icon in GROUP_ICONS'),
        ('icon not provided', edit(FRONTEND, 'lucideActivity, lucideGlobe', 'lucideActivity'),
         'icon lucideGlobe in GROUP_ICONS is not provided'),
        ('fallback not provided', edit(FRONTEND, ', lucideSettings })', ' })'),
         'fallback icon lucideSettings is not provided'),
        ('fallback not found', edit(FRONTEND, " ?? 'lucideSettings'", ''), 'fallback icon (GROUP_ICONS[...]'),
        ('group constant not declared', edit(permissions, 'Groups.Operations]', 'Groups.Billing]'),
         'SettingConstant.Groups.Billing is used by SettingGroupPermissions but not declared'),
        ('constant value is the registered key', edit(constants, '"Operations"', '"Maintenance"'),
         'setting group Maintenance has no icon in GROUP_ICONS'),
        ('icon table missing', edit(FRONTEND, 'GROUP_ICONS: Readonly', 'PANEL_ICONS: Readonly'),
         'GROUP_ICONS not found'),
        ('no groups found', edit(permissions, '[SettingConstant.Groups.Display] = PermissionConstant.Settings.Display,\n        '
                                 '[SettingConstant.Groups.Operations] = PermissionConstant.Settings.Operations,', ''),
         'no setting group found'),
    ]

    failures = []
    for name, mutate, *expected in [(n, m) for n, m in valid] + cases:
        with tempfile.TemporaryDirectory(prefix='check-setting-group-icons-') as tmp:
            root = Path(tmp)
            write_fixture(root)
            mutate(root)
            errors = check_project(root)
        if expected and not any(expected[0] in e for e in errors):
            failures.append(f'{name}: expected "{expected[0]}", got {errors}')
        if not expected and errors:
            failures.append(f'{name}: expected no problem, got {errors}')
    if failures:
        print('FAIL: check-setting-group-icons self-test')
        for failure in failures:
            print(f'  - {failure}')
        return 1
    print(f'PASS: check-setting-group-icons self-test ({len(valid)} valid fixtures, {len(cases)} independent rule mutations)')
    return 0


def main():
    # Windows 上管道输出默认按本地代码页编码，打印中文会崩溃
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding='utf-8')
    if '--self-test' in sys.argv[1:]:
        return self_test()
    problems = check_project(ROOT)
    if problems:
        print(f'FAIL: setting group icon check found {len(problems)} problem(s):')
        for problem in problems:
            print(f'  - {problem}')
        return 1
    print('PASS: every system setting group has a navigation icon, and every icon is provided')
    return 0


if __name__ == '__main__':
    sys.exit(main())
