namespace Leistd.Exception.Core
{
    public abstract class BusinessException : CommonException
    {
        protected string CodePrefix { get; private set; }

        public int Code { get; private set; }

        public string? Details { get; private set; }

        /// <summary>
        /// 可选的展示文案键（如 <c>User:EmailAlreadyUsed</c>）。HTTP 边界的本地化处理器按
        /// <c>LocalizationKey → 状态码通用语义键 → <see cref="System.Exception.Message"/></c> 的顺序
        /// 逐级回落产出用户可见消息（绝不把原始裸键漏给用户）。
        /// </summary>
        /// <remarks>
        /// 三职责分离：<see cref="System.Exception.Message"/> 永远是给日志/诊断的可读英文；
        /// <see cref="Code"/> 是稳定机器契约；<see cref="LocalizationKey"/> 是可改名的展示语义键。
        /// </remarks>
        public string? LocalizationKey { get; private set; }

        /// <summary>
        /// 本地化占位参数，供资源中的具名占位符（如 <c>{Sku}</c>）填充；未启用本地化时忽略。
        /// </summary>
        /// <remarks>命名为 <c>LocalizationData</c> 以区别于基类 <see cref="System.Exception.Data"/>（诊断字典）。</remarks>
        public IReadOnlyDictionary<string, object?> LocalizationData => _localizationData;

        private readonly Dictionary<string, object?> _localizationData = new(StringComparer.Ordinal);

        protected BusinessException(string codePrefix, string message, System.Exception? innerException = null)
            : base(message, innerException)
        {
            this.CodePrefix = codePrefix;
            this.Code = int.Parse(codePrefix + "00");
        }

        /// <summary>
        /// 在异常 HTTP 前缀后追加细分码后缀（如前缀 <c>400</c> + <c>"46"</c> → <c>40046</c>）。
        /// </summary>
        /// <remarks>
        /// 仅在前端需对某具体错误做差异化分支时才需要；多数业务异常用默认码（前缀 + 00）即可。
        /// </remarks>
        public BusinessException WithCode(string code)
        {
            this.Code = int.Parse(CodePrefix + code);
            return this;
        }

        public BusinessException WithDetails(string details)
        {
            this.Details = details;
            return this;
        }

        /// <summary>
        /// 设置展示文案键，把"用户可见消息"与"日志消息 <see cref="System.Exception.Message"/>"解耦。
        /// </summary>
        /// <remarks>
        /// 示例（三分离）：<c>throw new BadRequestException("Email already in use").WithLocalization("User:EmailAlreadyUsed").WithData("Email", email)</c>。
        /// 资源 <c>"User:EmailAlreadyUsed": "Email '{Email}' is already in use"</c>。未启用本地化时忽略键、直出 Message。
        /// <c>Code</c> 保持默认（前缀 + 00）；仅当前端需按码差异化处理时才额外 <see cref="WithCode(string)"/>。
        /// </remarks>
        public BusinessException WithLocalization(string localizationKey)
        {
            this.LocalizationKey = localizationKey;
            return this;
        }

        /// <summary>
        /// 追加一个本地化占位参数，供资源中的具名占位符填充。
        /// </summary>
        public BusinessException WithData(string name, object? value)
        {
            _localizationData[name] = value;
            return this;
        }

        public string? GetStackTraceStr()
        {
            return base.StackTrace;
        }

        public override string ToString()
        {
            return this.GetType().Name + " : [code=" + Code + ", message=" + Message + ", details=" + Details + "]";
        }
    }
}
