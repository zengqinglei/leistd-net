#!/usr/bin/env python3
"""framework/ 下 csproj 的三条约定闸门。

三条都曾自然漂移过，且都无法靠编译发现：

1. **不重复声明 common.props 已注入的共享属性。**`LangVersion` / `ImplicitUsings` /
   `Nullable` 由 `framework/common.props` 统一给定。各 csproj 再写一遍时，改共享值
   只对没重复声明的项目生效——差异静默存在，直到某个项目行为与别人不同才被发现。
   写成与继承值**不同**的值是合法覆盖（测试项目的
   `GenerateDocumentationFile=false` 就是），因此只拦与继承值相同的重复声明。

2. **`Leistd.<家族>.Core` 的根命名空间必须剥掉 `.Core`，其余项目一律不声明。**
   `.Core` 是打包边界（哪个程序集），不是类型的归属。此前两套并存：一半剥掉、
   一半保留，两套都自洽，但没有任何机制阻止第三套出现——而根命名空间不像
   "目录即命名空间"那样能由 IDE0130 机械判定，只能靠这里。

3. **`.Core` 包只依赖抽象。**业务项目在架构门禁里限制"应用层/领域层能引用什么"，
   而 Core 包的依赖会顺着传递引用进它们的闭包。宿主或基础设施实现渗进去时，
   业务项目只能往白名单里加一行，而框架侧没有任何机制阻止第四例、第五例——
   `Leistd.Authorization.Resource.Core` 引用本地化组件就是这样悄悄多出来的。
   两条子规则：Core 不得引用 `.AspNetCore` / `.EntityFrameworkCore` 等宿主与基础设施包；
   `PackageReference` 只允许 `Microsoft.Extensions.*` 与 `*.Abstractions`。
   跨家族的 Core → Core 引用**不禁止**（禁了就等于禁掉"译文随包分发"这类能力），
   但被引用方受同一条约束，所以闭包里传递进来的仍然只有抽象。

退出码非 0 表示存在违规，供 CI 阻断。
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
FRAMEWORK = REPO_ROOT / "framework"

# 属性名 → common.props 里的继承值。声明成同值即重复，声明成别的值是合法覆盖。
INHERITED = {
    "LangVersion": "latest",
    "ImplicitUsings": "enable",
    "Nullable": "enable",
    "GenerateDocumentationFile": "true",
}

# Core 包允许的 PackageReference：只有抽象。`Microsoft.Extensions.*` 整族都是抽象或极轻的
# 默认实现（Options、Logging.Abstractions 之类），其余一律要求以 `.Abstractions` 结尾。
#
# 今天全仓只有一个非 Extensions 的例外：Leistd.Settings.Core 的
# Microsoft.AspNetCore.DataProtection.Abstractions。它名字里带 AspNetCore 但是纯抽象包
# （IDataProtectionProvider / IDataProtector 两个接口，不含 Web 运行时），机密设置的加解密
# 真的需要它，微软自己在非 Web 场景也这么用——所以它由"以 .Abstractions 结尾"这条正常放行，
# 不需要豁免名单。要是哪天真需要豁免，加在这里并写明理由，而不是放宽规则。
ABSTRACTION_PACKAGE_PREFIXES = ("Microsoft.Extensions.",)
ABSTRACTION_PACKAGE_SUFFIX = ".Abstractions"

# 宿主与基础设施实现：Core 引用它们就是把 Web/EF 拖进业务项目的领域层闭包。
# 判的是包名后缀而不是内容——框架自己的分层命名已经把这件事表达清楚了。
INFRASTRUCTURE_SUFFIXES = (".AspNetCore", ".EntityFrameworkCore")


def core_dependency_problems(relative: str, assembly_name: str, text: str) -> list[str]:
    """Core 包只依赖抽象：见模块文档串第 3 条。"""
    if not assembly_name.endswith(".Core"):
        return []

    problems: list[str] = []

    # InternalsVisibleTo 不是依赖方向，只是可见性，不参与判定
    for kind in ("PackageReference", "ProjectReference"):
        for match in re.finditer(rf'<{kind}\s+Include="([^"]+)"', text):
            include = match.group(1)
            # ProjectReference 写的是路径，取文件名作为包名
            name = include.replace("\\", "/").rsplit("/", 1)[-1].removesuffix(".csproj")

            if any(name.endswith(suffix) for suffix in INFRASTRUCTURE_SUFFIXES):
                problems.append(
                    f"{relative}: Core 包引用了宿主/基础设施包 {name}——"
                    f"它会顺着传递引用进业务项目的领域层闭包；把这段实现挪到对应的 .AspNetCore "
                    f"或 .EntityFrameworkCore 包里"
                )
                continue

            if kind == "PackageReference" and not (
                name.startswith(ABSTRACTION_PACKAGE_PREFIXES)
                or name.endswith(ABSTRACTION_PACKAGE_SUFFIX)
            ):
                problems.append(
                    f"{relative}: Core 包的 PackageReference 只允许抽象包，{name} 不是"
                    f"（要求 Microsoft.Extensions.* 或以 .Abstractions 结尾）"
                )

    return problems


# `.Core` 是打包边界，不是类型归属：命名空间一律剥掉它。
# 这条对根原语包同样成立（Leistd.Core → Leistd），与 Volo.Abp.Core → Volo.Abp 一致。
CORE_SUFFIX = re.compile(r"^(Leistd(?:\..+)?)\.Core$")


def expected_root_namespace(assembly_name: str) -> str | None:
    """返回该项目应声明的 RootNamespace；None 表示不应声明。"""
    match = CORE_SUFFIX.match(assembly_name)
    return match.group(1) if match else None


# 子命名空间的两条硬规则：
#   - 不得与包名任一段重复（Leistd.Exception.Exceptions、Leistd.Lock.Memory.Locks）——纯噪声；
#   - 不得是缩写（Uow）——违反 FDG「避免缩写」，且往往同时是自重复。
# 命名空间 = RootNamespace + 目录路径（由 framework/.editorconfig 的 IDE0130 机械保证），
# 因此这里检查目录名即等价于检查命名空间。
#
# `Abstractions` 不在规则内——包内如何切分契约与实现见 development-guide §1，那里是唯一副本。
ABBREVIATIONS = {"Uow": "UnitOfWork"}

# `Services/` 在框架里恒定表示「实现」：11 个包用它放 DefaultXxx / XxxProvider 这类具体类型。
# 契约混进来会让这个词失去含义——读者无从判断 Services/ 里到底有没有可实现的接口。
IMPLEMENTATION_ONLY_DIRS = {"Services"}
PUBLIC_INTERFACE_RE = re.compile(r"^public interface ([A-Za-z0-9_]+)", re.M)


def namespace_segment_problems(csproj: Path, assembly_name: str, root_ns: str) -> list[str]:
    problems: list[str] = []
    package_words = {w.lower().rstrip("s") for w in assembly_name.split(".") if w != "Leistd"}
    for folder in sorted(csproj.parent.rglob("*")):
        if not folder.is_dir():
            continue
        if any(part in {"obj", "bin"} for part in folder.parts):
            continue
        if not any(folder.glob("*.cs")):
            continue
        seg = folder.name
        rel = folder.relative_to(REPO_ROOT).as_posix()
        if seg in IMPLEMENTATION_ONLY_DIRS:
            for cs in sorted(folder.glob("*.cs")):
                found = PUBLIC_INTERFACE_RE.findall(cs.read_text(encoding="utf-8"))
                if found:
                    problems.append(
                        f"{cs.relative_to(REPO_ROOT).as_posix()}: 公共接口 {', '.join(found)} 放在 "
                        f"'{seg}/' 里——该目录在框架内恒定表示实现；契约归 Abstractions/ 或内容目录"
                    )
        if seg in ABBREVIATIONS:
            problems.append(f"{rel}: 目录名 '{seg}' 是缩写（应为 {ABBREVIATIONS[seg]}），且与包名重复")
        elif seg.lower().rstrip("s") in package_words:
            problems.append(f"{rel}: 目录名 '{seg}' 与包名 {assembly_name} 重复，形成 {root_ns}.{seg} 这样的自重复命名空间")
    return problems


def read_property(text: str, name: str) -> str | None:
    match = re.search(rf"<{name}>([^<]*)</{name}>", text)
    return match.group(1).strip() if match else None


def main() -> int:
    problems: list[str] = []
    checked = 0

    for csproj in sorted(FRAMEWORK.rglob("*.csproj")):
        if any(part in {"obj", "bin"} for part in csproj.parts):
            continue

        checked += 1
        relative = csproj.relative_to(REPO_ROOT).as_posix()
        assembly_name = csproj.stem
        text = csproj.read_text(encoding="utf-8-sig")

        for name, inherited in INHERITED.items():
            declared = read_property(text, name)
            if declared is not None and declared == inherited:
                problems.append(
                    f"{relative}: 重复声明 <{name}>{declared}</{name}>，"
                    f"common.props 已给出同值——删掉这一行"
                )

        expected = expected_root_namespace(assembly_name)
        declared = read_property(text, "RootNamespace")

        if expected is None and declared is not None:
            problems.append(
                f"{relative}: 不应声明 <RootNamespace>（默认已等于程序集名 {assembly_name}）"
            )
        elif expected is not None and declared != expected:
            actual = "未声明" if declared is None else f"'{declared}'"
            problems.append(
                f"{relative}: <RootNamespace> 应为 '{expected}'（程序集名剥掉 .Core），实际 {actual}"
            )

        problems.extend(
            namespace_segment_problems(csproj, assembly_name, expected or assembly_name)
        )

        problems.extend(core_dependency_problems(relative, assembly_name, text))

        if csproj.read_bytes().startswith(b"\xef\xbb\xbf"):
            problems.append(f"{relative}: 带 UTF-8 BOM，与其余 csproj 不一致")

    if problems:
        print("csproj 约定检查失败：")
        for problem in problems:
            print(f"  {problem}")
        return 1

    print(f"✅ csproj 约定检查通过（{checked} 个项目：无重复共享属性、根命名空间统一、无自重复/缩写目录、Services/ 只含实现、Core 只依赖抽象、无 BOM）。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
