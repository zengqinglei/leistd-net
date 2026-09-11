#!/usr/bin/env python3
"""业务异常必须带错误码。

为什么要闸门：不带码就没有词条可查，处理器只能直出构造时的英文 `Message`——中文界面上
冒出一句英文诊断串；而部署方若把 `MessageExposure` 收紧到 `None`，连这句都没有，
只剩"请求无效。"这类通用话，用户无从修正。两种结果都不该出现在产品里，
而这既不报错也不影响任何测试。

判据：模板后端源码里 400 / 409 / 422 这三类业务异常的 throw 语句，必须在同一条语句内出现
`.WithCode("...")`。

带码 + 配词条是唯一一条让原因既说得清、又随语言走的路（词条缺失由 i18n 闸门另行拦住）。

## 为什么要先屏蔽字符串与注释

判据是"这条语句真的调了 `WithCode`"，而裸文本里的 `.WithCode(` 可能压根不是调用：
被注释掉的那一行仍然留在文本里（`/* .WithCode("X") */`），消息串里也可能恰好出现这几个字。
按裸文本判断，把调用注释掉之后闸门照样是绿的——那是这道闸门最不该有的失效方式。

所以先把注释和字符串/字符字面量的内容替成空格（长度与换行保持不变，行号才不会偏），
再在这份"只剩代码"的文本上找 `throw` 与 `.WithCode(`。顺带解决另两件事：
语句边界不会被注释或字符串里的 `;` 提前截断，括号深度也不会被串里的括号带偏。

字符串屏蔽刻意做得**偏严**：内插串（`$"{Foo()}"`）整段屏蔽，不保留插值洞里的代码。
洞里出现 `.WithCode(` 只能是把调用写进了消息拼接，那不是这道闸门该放行的形态。
"""
import argparse
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SCAN_ROOTS = [REPO / "template/backend/src"]

# 只管**状态码本身说不清原因**的那几类：400 / 409 / 422 说的是"你的输入有问题"，
# 但到底哪条规则没过只有消息知道，所以必须带码配词条。
#
# 401 / 403 / 404 不在其中：状态本身就是原因（未认证 / 无权限 / 不存在），归一文案已经说得清。
# 500 / 503 更不在其中：那些消息是内部诊断信息，本来就不该呈现给用户，被通用文案盖住是对的。
# InvalidOperationException / ArgumentException 之类是编程错误，走 500 通道，不在业务异常之列。
BUSINESS_EXCEPTIONS = (
    "BadRequestException",
    "ConflictException",
    "UnprocessableEntityException",
)
THROW = re.compile(r"\bthrow new (" + "|".join(BUSINESS_EXCEPTIONS) + r")\b")

# 字面量起始：可带 $ / @ 前缀（含 $@ 与 @$），后接一个引号，或三个以上引号（原始字符串）。
# 必须要求引号，否则 @class 这类逐字标识符会被当成字面量开头。
LITERAL_START = re.compile(r'(?:\$+@|@\$+|@|\$+)?("{3,}|")')


def blank(text: str) -> str:
    """把一段文本替成等长空格，只留换行——长度与行号都不动。"""
    return "".join("\n" if ch == "\n" else " " for ch in text)


def mask_code(text: str) -> str:
    """屏蔽注释与字符串/字符字面量的内容，返回等长的"只剩代码"文本。"""
    out: list[str] = []
    i, n = 0, len(text)
    while i < n:
        two = text[i : i + 2]

        if two == "//":
            end = text.find("\n", i)
            end = n if end < 0 else end
        elif two == "/*":
            end = text.find("*/", i + 2)
            end = n if end < 0 else end + 2
        elif text[i] == "'":
            end = scan_quoted(text, i + 1, "'")
        elif match := LITERAL_START.match(text, i):
            quotes = match.group(1)
            if len(quotes) >= 3:
                end = scan_raw(text, match.end(), len(quotes))
            elif "@" in match.group(0):
                end = scan_verbatim(text, match.end())
            else:
                end = scan_quoted(text, match.end(), '"')
        else:
            out.append(text[i])
            i += 1
            continue

        out.append(blank(text[i:end]))
        i = end

    return "".join(out)


def scan_quoted(text: str, i: int, closer: str) -> int:
    """普通字符串 / 字符字面量：反斜杠转义；遇换行按未闭合处理，就此收尾。"""
    while i < len(text):
        if text[i] == "\\":
            i += 2
            continue
        if text[i] == closer:
            return i + 1
        if text[i] == "\n":
            return i
        i += 1
    return len(text)


def scan_verbatim(text: str, i: int) -> int:
    """逐字字符串 @"..."：没有反斜杠转义，"" 表示一个引号，可跨行。"""
    while i < len(text):
        if text[i] == '"':
            if text[i + 1 : i + 2] == '"':
                i += 2
                continue
            return i + 1
        i += 1
    return len(text)


def scan_raw(text: str, i: int, count: int) -> int:
    """原始字符串：闭合是恰好 count 个引号的那一段，内部更短的引号串不算。"""
    closer = '"' * count
    while (found := text.find(closer, i)) >= 0:
        if text[found + count : found + count + 1] != '"':
            return found + count
        i = found + count
    return len(text)


def statement_at(code: str, start: int) -> str:
    """取从 throw 到语句结束分号之间的文本。要求 code 已经过 mask_code。"""
    i, depth = start, 0
    while i < len(code):
        ch = code[i]
        if ch in "([{":
            depth += 1
        elif ch in ")]}":
            depth -= 1
        elif ch == ";" and depth <= 0:
            return code[start : i + 1]
        i += 1
    return code[start:]


def find_violations(text: str) -> tuple[list[tuple[int, str]], int]:
    """返回（未带码的抛出点 [(行号, 异常名)]，检查过的抛出点总数）。"""
    code = mask_code(text)
    violations: list[tuple[int, str]] = []
    checked = 0
    for match in THROW.finditer(code):
        checked += 1
        if ".WithCode(" in statement_at(code, match.start()):
            continue
        violations.append((code.count("\n", 0, match.start()) + 1, match.group(1)))
    return violations, checked


# 每条样例都是真实可能写出来的 C#。屏蔽规则写错一个边界就会静默不再命中，
# 从那以后闸门永远是绿的——所以规则本身也要被测。
SELF_TEST_CASES = [
    ("普通写法", 'throw new BadRequestException("x").WithCode("A:B");', True),
    (
        "跨行链式调用",
        'throw new ConflictException("x")\n    .WithCode("A:B")\n    .WithData("N", v);',
        True,
    ),
    ("消息里恰好出现这几个字", 'throw new BadRequestException("Missing .WithCode(");', False),
    ("块注释掉的调用", 'throw new BadRequestException("x")\n    /* .WithCode("A:B") */;', False),
    ("行注释掉的调用", 'throw new BadRequestException("x")\n    // .WithCode("A:B")\n    ;', False),
    ("逐字串里的分号不截断语句", 'throw new BadRequestException(@"a;b").WithCode("A:B");', True),
    ("逐字串里的双引号转义", 'throw new BadRequestException(@"say ""hi""").WithCode("A:B");', True),
    # 下面三条刻意写成**跨行**的：逐字串与原始串能跨行，普通串不能。少认一种前缀、
    # 或把逐字串里的 "" 当成闭合，扫描都会在换行处提前收尾，把串里的字当成代码放出来
    # ——于是"消息里提到 WithCode"就成了绕过闸门的办法。同行的样例查不出这类错。
    (
        "跨行逐字串",
        'throw new BadRequestException(@"line1\n.WithCode(""X"") line2");',
        False,
    ),
    (
        "跨行逐字串里的双引号转义",
        'throw new BadRequestException(@"a ""q""\n.WithCode(""X"") b");',
        False,
    ),
    ("跨行原始串", 'throw new BadRequestException("""a\n.WithCode("X") b""");', False),
    (
        "行注释里的分号不截断语句",
        'throw new BadRequestException("x") // 见 #123; 略\n    .WithCode("A:B");',
        True,
    ),
    ("原始串里的调用不算调用", 'throw new BadRequestException("""a; .WithCode("X")""");', False),
    ("内插串整段屏蔽", 'throw new ConflictException($"{name} taken").WithCode("A:B");', True),
    ("串里的括号不带偏深度", 'throw new BadRequestException("a ) ; b").WithCode("A:B");', True),
    # 字符字面量里的引号：不当字面量处理的话，这个 " 会被当成串的开头，
    # 把后面的 .WithCode( 一起吞进去
    (
        "字符字面量里的引号",
        'throw new BadRequestException("x" + \'"\' + "y").WithCode("A:B");',
        True,
    ),
    ("转义引号不提前闭合", 'throw new BadRequestException("a \\" ;").WithCode("A:B");', True),
    ("不在名单里的异常不管", 'throw new NotFoundException("user 42");', True),
    ("未带码", 'throw new UnprocessableEntityException("field", "x");', False),
]


def self_test() -> int:
    failures: list[str] = []
    for name, snippet, should_pass in SELF_TEST_CASES:
        violations, checked = find_violations(snippet)
        if bool(violations) == should_pass:
            expectation = "应判为合规却报了" if should_pass else "应报出却放行了"
            failures.append(f"  - [{name}] {expectation}")
        if name == "不在名单里的异常不管" and checked != 0:
            failures.append(f"  - [{name}] 名单外的异常被计入检查（checked={checked}）")

    if failures:
        print(f"❌ 错误码闸门规则自测失败（共 {len(failures)} 项）：")
        print("\n".join(failures))
        return 1

    print(f"✅ 错误码闸门规则自测通过（{len(SELF_TEST_CASES)} 个样例）。")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="业务异常错误码闸门")
    parser.add_argument("--self-test", action="store_true", help="只跑屏蔽规则自测再退出")
    if parser.parse_args().self_test:
        return self_test()

    problems: list[str] = []
    scanned = 0
    checked = 0
    for root in SCAN_ROOTS:
        for path in sorted(root.rglob("*.cs")):
            if "/obj/" in str(path) or "/bin/" in str(path):
                continue
            scanned += 1
            violations, count = find_violations(path.read_text(encoding="utf-8"))
            checked += count
            for line, exception in violations:
                problems.append(
                    f"  - {path.relative_to(REPO)}:{line} {exception} 未带错误码"
                    " —— 加 .WithCode(\"模块:语义\") 并在 Api/Resources/{en,zh-CN}.json 配词条"
                )

    if problems:
        print(f"❌ 业务异常错误码检查失败（共 {len(problems)} 项，已检查 {checked} 处抛出）：")
        print("\n".join(problems))
        print("\n不带码时没有词条可查，界面上只会出现英文诊断串或通用文案。")
        return 1

    print(f"✅ 业务异常错误码检查通过（{scanned} 个文件，{checked} 处抛出均带码）。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
