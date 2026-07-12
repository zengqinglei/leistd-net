namespace Leistd.Exception.Core
{
    public abstract class BusinessException : CommonException
    {
        protected string CodePrefix { get; private set; }

        public int Code { get; private set; }

        public string? Details { get; private set; }

        /// <summary>
        /// Optional resource key used by an HTTP-boundary localizer.
        /// </summary>
        public string? LocalizationKey { get; private set; }

        /// <summary>
        /// Format arguments for <see cref="LocalizationKey"/>.
        /// </summary>
        public IReadOnlyList<object?> LocalizationArguments { get; private set; } = [];

        protected BusinessException(string codePrefix, string message, System.Exception? innerException = null)
            : base(message, innerException)
        {
            this.CodePrefix = codePrefix;
            this.Code = int.Parse(codePrefix + "00");
        }

        public BusinessException WithCode(string code)
        {
            this.Code = int.Parse(CodePrefix + code);
            return this;
        }

        /// <summary>
        /// Sets a complete five-digit error code whose first three digits match the exception HTTP prefix.
        /// </summary>
        public BusinessException WithCode(int code)
        {
            var codeText = code.ToString();
            if (codeText.Length != 5 || !codeText.StartsWith(CodePrefix, StringComparison.Ordinal))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(code),
                    code,
                    $"The complete error code must contain five digits and start with the exception prefix '{CodePrefix}'.");
            }

            Code = code;
            return this;
        }

        public BusinessException WithDetails(string details)
        {
            this.Details = details;
            return this;
        }

        /// <summary>
        /// Adds localization metadata without coupling the exception core to a resource implementation.
        /// </summary>
        public BusinessException WithLocalization(string localizationKey, params object?[] arguments)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(localizationKey);

            LocalizationKey = localizationKey;
            LocalizationArguments = arguments;
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
