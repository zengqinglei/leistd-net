namespace CompanyName.ProjectName.Api;

/// <summary>
/// DataAnnotations 校验消息本地化的标记类型：作为 <c>DataAnnotationLocalizerProvider</c> 的 resource 类型传入。
/// 框架的 JSON localizer 工厂对任意类型返回同一共享视图（读取全部已登记资源程序集），
/// 故此类型仅需存在即可，真正的校验文案键存放在本项目 <c>Resources/{en,zh-CN}.json</c>。
/// </summary>
public sealed class ApiResource;
