#!/usr/bin/env python3
"""业务异常必须带错误码。

为什么要闸门：不带码就没有词条可查，处理器只能直出构造时的英文 `Message`——中文界面上
冒出一句英文诊断串；而部署方若把 `MessageExposure` 收紧到 `None`，连这句都没有，
只剩"请求无效。"这类通用话，用户无从修正。两种结果都不该出现在产品里，
而这既不报错也不影响任何测试。

判据：模板后端源码里 400 / 409 / 422 这三类业务异常的 throw 语句，必须在同一条语句内出现
`.WithCode("...")`。

带码 + 配词条是唯一一条让原因既说得清、又随语言走的路（词条缺失由 i18n 闸门另行拦住）。
"""
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


def statement_at(text: str, start: int) -> str:
    """取从 throw 到语句结束分号之间的文本（跳过字符串里的分号）。"""
    i, depth, in_string, verbatim = start, 0, False, False
    while i < len(text):
        ch = text[i]
        if in_string:
            if ch == "\\" and not verbatim:
                i += 2
                continue
            if ch == '"':
                if verbatim and text[i + 1 : i + 2] == '"':
                    i += 2
                    continue
                in_string = False
        elif ch == '"':
            in_string = True
            verbatim = text[max(0, i - 1) : i] == "@" or text[max(0, i - 2) : i] == '$@'
        elif ch in "([{":
            depth += 1
        elif ch in ")]}":
            depth -= 1
        elif ch == ";" and depth <= 0:
            return text[start : i + 1]
        i += 1
    return text[start:]


def main() -> int:
    problems: list[str] = []
    scanned = 0
    checked = 0
    for root in SCAN_ROOTS:
        for path in sorted(root.rglob("*.cs")):
            if "/obj/" in str(path) or "/bin/" in str(path):
                continue
            scanned += 1
            text = path.read_text(encoding="utf-8")
            for match in THROW.finditer(text):
                checked += 1
                statement = statement_at(text, match.start())
                if ".WithCode(" in statement:
                    continue
                line = text.count("\n", 0, match.start()) + 1
                problems.append(
                    f"  - {path.relative_to(REPO)}:{line} {match.group(1)} 未带错误码"
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
