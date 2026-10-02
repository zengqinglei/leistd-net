/**
 * 不含本地化时组件取文案的函数：按键查组件自带的英文表，`{{name}}` 占位按参数替换。
 *
 * 与含本地化时结构指令 `*transloco="let t"` 给出的 `t` 同名同签名，模板正文因此两种形态共用一份。
 * 英文表的键与值须与 `en.json` 一致（i18n 静态闸门校验）；表里没有的键原样返回，与 Transloco 缺词条时的表现相同。
 *
 * 校验提示（`validation.*`）由这里统一并入每张表：模板按错误类型拼键 `t('validation.' + error.kind, error)`，
 * 各组件英文表列不全也查得到。参数可以直接传校验错误对象，占位符按它的字段名取值（如 `minLength`）。
 */
export function englishText(table: Readonly<Record<string, string>>) {
  const texts: Readonly<Record<string, string>> = { ...ENGLISH_VALIDATION, ...table };
  return (key: string, params?: object): string =>
    texts[key]?.replace(/\{\{\s*(\w+)\s*\}\}/g, (_, name: string) =>
      String((params as Record<string, unknown> | undefined)?.[name] ?? ''),
    ) ?? key;
}

/**
 * 校验提示的英文表，键是 `validation.<错误类型>`，与 `en.json` 的 `validation` 段逐条一致。
 *
 * 新增自定义错误类型时两边同时补：模板里的键是拼出来的，漏了只会在界面上显示裸键（i18n 闸门校验两边键集一致）。
 */
const ENGLISH_VALIDATION: Record<string, string> = {
  'validation.required': 'This field is required.',
  'validation.email': 'Please enter a valid email address.',
  'validation.minLength': 'Must be at least {{minLength}} characters.',
  'validation.maxLength': 'Must not exceed {{maxLength}} characters.',
  'validation.usernamePattern': 'Must be 3–64 letters, digits, or underscores.',
  'validation.roleNamePattern': 'Must be 2–64 letters, digits, or underscores.',
  'validation.phonePattern': 'Only digits, spaces, and + - ( ) are allowed.',
  'validation.passwordMismatch': 'The two passwords do not match.',
  'validation.passwordSameAsCurrent': 'The new password must differ from the current one.',
  'validation.tenantNamePattern':
    'Use letters, digits and hyphens only, not starting or ending with a hyphen, up to 63 characters.',
  'validation.connectionNamePattern':
    'Use lowercase letters, digits and hyphens only, 1 to 64 characters.',
  'validation.connectionNameTaken':
    'This connection name is already registered; change its connection string instead.',
  'validation.pkceRequired': 'Native/Public clients must enable PKCE.',
};
