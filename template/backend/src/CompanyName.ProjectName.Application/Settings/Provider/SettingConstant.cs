namespace CompanyName.ProjectName.Application.Settings.Provider;

/// <summary>
/// 设置名称常量
/// </summary>
/// <remarks>
/// 设置名是跨前后端的字符串契约，集中在此避免两侧各写一份字面量后漂移。
/// </remarks>
public static class SettingConstant
{
    /// <summary>界面显示偏好。</summary>
    public static class Display
    {
#if (IncludeLocalization)
        /// <summary>界面语言（culture 名，如 <c>zh-CN</c>）。</summary>
        public const string Language = "Display.Language";

#endif
        /// <summary>展示时区（IANA 名，如 <c>Asia/Shanghai</c>）。时间一律以 UTC 存储，仅展示时换算。</summary>
        public const string TimeZone = "Display.TimeZone";
    }
}
