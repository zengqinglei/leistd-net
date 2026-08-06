export enum Role {
  Admin = 'Admin',
  Operator = 'Operator',
  Member = 'Member',
}

//#if (IncludeLocalization)
/**
 * 角色标签映射。值为 Transloco 词条键，由消费方
 * （RoleLabelPipe / getRoleLabel / roleOptions）通过 TranslocoService 翻译。
 */
//#else
/**
 * 角色标签映射。值为英文默认标签（Administrator/Operator/Member），直接展示。
 */
//#endif
export const ROLE_LABEL_MAP: Record<Role, string> = {
  //#if (IncludeLocalization)
  [Role.Admin]: 'role.admin',
  [Role.Operator]: 'role.operator',
  [Role.Member]: 'role.member',
  //#else
  [Role.Admin]: 'Administrator',
  [Role.Operator]: 'Operator',
  [Role.Member]: 'Member',
  //#endif
};

/** 角色图标映射（ng-icon 名称）。使用方需自行 provideIcons 注册对应图标。 */
export const ROLE_ICON_MAP: Record<string, string> = {
  [Role.Admin]: 'lucideShieldCheck',
  [Role.Operator]: 'lucideBriefcase',
  [Role.Member]: 'lucideUser',
};
