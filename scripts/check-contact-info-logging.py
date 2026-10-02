#!/usr/bin/env python3
"""联系方式不能以原文进入日志。

邮箱、手机号这类联系方式一旦写进日志，就同时留在集中采集的存储里：保留更久、可见范围更大，
还会被前端错误上报之类的旁路带走。**症状是静默的**——代码照常工作，日志照常有内容，
只有事后审计或外部反馈才会发现。DEF-04 就是这么被发现的。

单测覆盖不到这一类：日志调用点通常在要连外部服务的方法里（`SmtpEmailSender.SendAsync`
要真连 SMTP），为它造接缝的成本高于收益，而**把调用点改回原文不会让任何用例变红**。
所以判据放在这里：扫日志调用的实参，凡是明显承载联系方式的表达式，必须经
`Leistd.Redaction.TextRedactor` 处理，或者压根不出现在日志里。

判据是**实参表达式**而不是占位符名：占位符叫什么由写日志的人定，`{To}` 里塞原文一样泄露。

自测：`python3 scripts/check-contact-info-logging.py --self-test`
"""
import io, os, re, sys, tempfile

ROOT = os.path.join(os.path.dirname(__file__), '..')

# 禁区：会写日志的生产代码
ZONES = [
    'framework/components',
    'framework/ddd-struct',
    'template/backend/src',
]

# 日志调用：ILogger 的扩展方法与源生成方法都以 Log 开头
LOG_CALL_RE = re.compile(r'\bLog(?:Trace|Debug|Information|Warning|Error|Critical)\s*\(', re.I)

# 明显承载联系方式的实参表达式。只认"名字就说明它是联系方式"的形态，
# 不做类型推断——推断不了的由组合规范与评审兜，不在这里猜。
CONTACT_ARG_RE = re.compile(
    r'\b(?:'
    r'[A-Za-z_][A-Za-z0-9_.]*\.(?:Email|EmailAddress|PhoneNumber|Mobile)\b'      # x.Email / dto.PhoneNumber
    r'|(?:email|emailAddress|phoneNumber|mobile)\b'                              # 局部变量
    r'|[A-Za-z_][A-Za-z0-9_.]*\.To\b'                                            # message.To / input.To
    r')',
    re.I)

# 已经脱敏的写法：实参被 TextRedactor 包住就放行
REDACTED_RE = re.compile(r'TextRedactor\s*\.\s*Redact[A-Za-z]*\s*\(')

# 判定前先抠掉普通字符串字面量：消息模板本身常含 "email"（"Email verification is
# unavailable"、"the notification email to user {UserId}"），连模板一起扫会把它们全判成违规。
# 插值字符串（$"…"）不抠——把联系方式插进模板同样是泄露，那种写法要留在判定范围内。
PLAIN_LITERAL_RE = re.compile(r'(?<![$@])"(?:[^"\\]|\\.)*"')

# 豁免：路径 → (被豁免的整条语句片段, 期望出现次数, 理由)。
# 加新条目前先问"这是不是说明规则该改"。
WAIVERS = {}


def log_calls(text):
    """产出每个日志调用的实参串（按括号配平截取，跨行也能取全）。"""
    for match in LOG_CALL_RE.finditer(text):
        start = match.end()
        depth, i = 1, start
        while i < len(text) and depth:
            if text[i] == '(':
                depth += 1
            elif text[i] == ')':
                depth -= 1
            i += 1
        yield text[:match.start()].count('\n') + 1, text[start:i - 1]


def run(zones, waivers, root):
    problems, checked, waived = [], 0, {}
    for zone in zones:
        zone_root = os.path.join(root, zone)
        for dirpath, dirnames, filenames in os.walk(zone_root):
            dirnames[:] = [d for d in dirnames if d not in ('obj', 'bin')]
            for fname in filenames:
                if not fname.endswith('.cs'):
                    continue
                path = os.path.join(dirpath, fname)
                rel = os.path.relpath(path, root).replace(os.sep, '/')
                text = io.open(path, encoding='utf-8').read()
                checked += 1
                for line, args in log_calls(text):
                    hit = CONTACT_ARG_RE.search(PLAIN_LITERAL_RE.sub('""', args))
                    if not hit or REDACTED_RE.search(args):
                        continue
                    snippet = ' '.join(args.split())[:120]
                    entry = waivers.get(rel)
                    if entry and entry[0] in snippet:
                        waived[rel] = waived.get(rel, 0) + 1
                        continue
                    problems.append(
                        f'❌ {rel}:{line} 日志实参 `{hit.group(0)}` 看起来是联系方式，'
                        f'却没经 TextRedactor 脱敏：\n     {snippet}')
    for rel, (frag, expected, reason) in waivers.items():
        got = waived.get(rel, 0)
        if got != expected:
            problems.append(
                f'❌ 豁免与实际不符：{rel} 期望命中 {expected} 次 `{frag}`，实际 {got} 次（{reason}）')
    if problems:
        return 1, ['发现联系方式以原文进入日志：', *problems, '',
                   '改法：实参用 TextRedactor.RedactEmail / RedactPartially 包一层，'
                   '或把该字段从日志里去掉（用标识符代替）。']
    return 0, [f'✅ 联系方式日志检查通过（{checked} 个文件，{len(zones)} 个禁区，'
               f'{sum(waived.values())} 处豁免全部命中）。']


SELF_TEST_CASES = [
    ('原文进日志应拦下', 1, {
        'Bad.cs': 'logger.LogInformation("Sent to {To}", message.To);\n'}),
    ('脱敏后放行', 0, {
        'Good.cs': 'logger.LogInformation("Sent to {To}", TextRedactor.RedactEmail(message.To));\n'}),
    ('局部变量原文应拦下', 1, {
        'Bad2.cs': 'logger.LogWarning("Creating {Email}", email);\n'}),
    ('跨行调用也要取全实参', 1, {
        'Bad3.cs': 'logger.LogInformation(\n    "Sent to {To}",\n    input.Email);\n'}),
    ('不含联系方式的日志不误报', 0, {
        'Fine.cs': 'logger.LogInformation("Created {UserId}", user.Id);\n'}),
    # 占位符叫别的名字照样要拦：判据是实参不是占位符
    ('占位符改名不能绕过', 1, {
        'Bad4.cs': 'logger.LogInformation("Target {Who}", user.Email);\n'}),
    # 消息模板里出现 email 字样不算违规——这是加这条抠字面量规则前的误报形态
    ('模板文本含 email 不误报', 0, {
        'Fine2.cs': 'logger.LogInformation("Email verification unavailable: {Section}", section);\n'}),
    ('模板文本含 email 且实参无关也不误报', 0, {
        'Fine3.cs': 'logger.LogWarning("the notification email to user {UserId} was dropped", userId);\n'}),
    # 插值字符串不抠：把联系方式插进模板同样要拦
    ('插值把联系方式塞进模板要拦', 1, {
        'Bad5.cs': 'logger.LogInformation($"Sent to {user.Email}");\n'}),
]


def self_test():
    failures = 0
    print('联系方式日志规则自检：')
    for name, expected, files in SELF_TEST_CASES:
        with tempfile.TemporaryDirectory() as tmp:
            zone = os.path.join(tmp, 'zone')
            os.makedirs(zone)
            for fname, content in files.items():
                io.open(os.path.join(zone, fname), 'w', encoding='utf-8').write(content)
            code, msgs = run(['zone'], {}, tmp)
            ok = code == expected
            print(f"  {'✅' if ok else '❌'} {name}（期望退出 {expected}，实际 {code}）")
            if not ok:
                failures += 1
                print('     ' + '\n     '.join(msgs))
    if failures:
        print(f'\n❌ 自测失败 {failures} 例。')
        return 1
    print(f'\n✅ 自测通过（{len(SELF_TEST_CASES)} 例）。')
    return 0


def main():
    if '--self-test' in sys.argv:
        return self_test()
    code, messages = run(ZONES, WAIVERS, ROOT)
    print('\n'.join(messages))
    return code


if __name__ == '__main__':
    sys.exit(main())
