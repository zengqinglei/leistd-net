using Leistd.Exceptions;
using Leistd.ExceptionHandling.Constants;

namespace Leistd.ExceptionHandling
{
    /// <summary>
    /// 业务异常基类：带对外 HTTP 状态码与稳定错误码，由全局异常处理器转成 RFC 9457 ProblemDetails。
    /// </summary>
    /// <remarks>
    /// 派生类声明状态码语义（如 <c>NotFoundException</c> = 404）。抛出方按需用
    /// <c>WithCode</c> / <c>WithDetails</c> / <c>WithData</c> 链式补充。
    /// </remarks>
    public abstract class BusinessException : CommonException
    {
        /// <summary>
        /// 获取此异常对外公开的 HTTP 状态码。
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// 错误码：既是对外的稳定机器契约，也是本地化资源的查找键（如 <c>User:EmailAlreadyUsed</c>）
        /// </summary>
        /// <remarks>
        /// 未显式 <see cref="WithCode(string)"/> 时为 <see cref="StatusCode"/> 对应的通用码
        /// （<see cref="GenericErrorCodes.ForStatus"/>），因此<b>恒有值</b>，对外响应里不会缺这一项。
        /// 一经对外即为契约：重命名它是破坏性变更。
        /// 与 <see cref="Exception.Message"/> 的分工是：后者永远是给日志/诊断的英文，不参与身份。
        /// </remarks>
        public string Code { get; private set; }

        /// <summary>
        /// 面向排查的补充说明。是否写入响应由宿主的 <c>IncludeExceptionDetails</c> 决定。
        /// </summary>
        public string? Details { get; private set; }

        /// <summary>
        /// 获取诊断消息是否可直接呈现给终端用户。
        /// </summary>
        /// <remarks>
        /// <see cref="Exception.Message"/> 面向运维、统一用英文，而 HTTP 边界在词条全部未命中时可能回落到它。
        /// 本标记让"这条消息我确认可以给用户看"成为显式声明：只有标记过的异常，
        /// 或宿主把 <c>GlobalExceptionOptions.FallbackToExceptionMessage</c> 置为 <see langword="true"/> 时，该消息才会呈现出去。
        /// </remarks>
        public bool IsUserFacingMessage { get; private set; }

        /// <summary>
        /// 本地化占位参数，供资源中的具名占位符（如 <c>{Sku}</c>）填充；未启用本地化时忽略。
        /// </summary>
        public IReadOnlyDictionary<string, object?> LocalizationData => _localizationData;

        private readonly Dictionary<string, object?> _localizationData = new(StringComparer.Ordinal);

        /// <summary>
        /// 以状态码与诊断消息构造。<see cref="Code"/> 初始为该状态码的通用码。
        /// </summary>
        /// <param name="statusCode">对外 HTTP 状态码。</param>
        /// <param name="message">面向日志与诊断的英文消息，不直接呈现给终端用户。</param>
        /// <param name="innerException">内层异常。</param>
        protected BusinessException(int statusCode, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            this.StatusCode = statusCode;
            this.Code = GenericErrorCodes.ForStatus(statusCode);
        }

        /// <summary>
        /// 设置错误码和对应的本地化资源键。
        /// </summary>
        /// <remarks>
        /// 示例：<c>throw new BadRequestException("Email already in use").WithCode("User:EmailAlreadyUsed").WithData("Email", email)</c>，
        /// 资源 <c>"User:EmailAlreadyUsed": "Email '{Email}' is already in use"</c>。
        /// 词条缺失时回落到状态码通用文案，不会把键或诊断消息漏给用户。
        /// </remarks>
        /// <param name="code">错误码兼资源键，不能为空或全空白。</param>
        /// <exception cref="ArgumentException"><paramref name="code"/> 为空或全空白。</exception>
        public BusinessException WithCode(string code)
        {
            // 空码会作为对外契约泄出，且让词条查找落到一个无意义的键上
            ArgumentException.ThrowIfNullOrWhiteSpace(code);

            this.Code = code;
            return this;
        }

        /// <summary>
        /// 设置面向排查的补充说明。是否写入响应由宿主的 <c>IncludeExceptionDetails</c> 决定。
        /// </summary>
        public BusinessException WithDetails(string details)
        {
            this.Details = details;
            return this;
        }

        /// <summary>
        /// 声明 <see cref="Exception.Message"/> 可直接呈现给终端用户。
        /// </summary>
        /// <remarks>
        /// 适用于消息本身就是写给用户看的场景（且不含内部标识、路径或技术细节）。
        /// 需要多语言时仍应改用 <see cref="WithCode(string)"/> 并配词条——本标记只是
        /// "无词条可用时允许直出原文"的许可，不替代本地化。
        /// </remarks>
        public BusinessException AsUserFacing()
        {
            this.IsUserFacingMessage = true;
            return this;
        }

        /// <summary>
        /// 追加一个本地化占位参数，供资源中的具名占位符填充。
        /// </summary>
        /// <param name="name">占位符名，与资源中的 <c>{Name}</c> 对应。</param>
        /// <param name="value">填充值。</param>
        public BusinessException WithData(string name, object? value)
        {
            _localizationData[name] = value;
            return this;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return this.GetType().Name + " : [code=" + Code + ", message=" + Message + ", details=" + Details + "]";
        }
    }
}
