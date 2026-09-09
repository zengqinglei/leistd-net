/**
 * 设置名常量。
 *
 * 必须与后端 `SettingConstant` 逐字一致：设置名是跨前后端的字符串契约，
 * 启动流程、布局与设置页都按它取值，漂移会让某一处静默读不到。
 */
export const SETTINGS = {
  display: {
    //#if (IncludeLocalization)
    language: 'Display.Language',
    //#endif
    timeZone: 'Display.TimeZone',
  },
  logging: {
    minimumLevel: 'Logging.MinimumLevel',
    requestLevel: 'Logging.RequestLevel',
  },
  //#if (LocalIdentity)
  registration: {
    enableEmailVerification: 'Registration.EnableEmailVerification',
  },
  //#endif
} as const;
