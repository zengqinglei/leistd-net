//#if (LocalIdentity)
/**
 * 口令规则的唯一定义处。
 *
 * 这里只做**快速反馈**，安全不变量在服务端 `PasswordPolicy`——直接调 API 能绕开前端，
 * 内部调用（种子、租户初始化、bootstrap）连表单都不经过。因此本规则必须与服务端保持一致：
 * 比服务端松会让用户提交后才被拒，比服务端严会拒绝服务端本来接受的合法口令。
 *
 * 所有表单均 import 本常量，不各自内联正则，避免静默的规则分叉。
 *
 * 规则本身选长度而不选复杂度：复杂度要求把人推向 `Passw0rd!` 这类可预测形态却挡不住它。
 */
export const PASSWORD_MIN_LENGTH = 12;
export const PASSWORD_MAX_LENGTH = 256;

/** 与服务端 `PasswordPolicy` 对齐：足够长、允许长口令、不强制字符类别 */
export const PASSWORD_RULE = new RegExp(`^.{${PASSWORD_MIN_LENGTH},${PASSWORD_MAX_LENGTH}}$`);
//#endif
