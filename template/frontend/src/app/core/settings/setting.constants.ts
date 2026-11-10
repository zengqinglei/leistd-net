/** 设置名常量，与后端 `SettingConstant` 逐字一致。 */
export const SETTINGS = {
  display: {
    //#if (IncludeLocalization)
    language: 'Display.Language',
    //#endif
    timeZone: 'Display.TimeZone',
    theme: 'Display.Theme',
  },
  logging: {
    minimumLevel: 'Logging.MinimumLevel',
  },
  telemetry: {
    exportEnabled: 'Telemetry.Export.Enabled',
    exportEndpoint: 'Telemetry.Export.Endpoint',
    exportHeaders: 'Telemetry.Export.Headers',
  },
} as const;
