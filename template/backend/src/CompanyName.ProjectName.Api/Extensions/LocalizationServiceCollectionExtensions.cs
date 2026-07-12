using System.Globalization;
using CompanyName.ProjectName.Api.Localization;
using Leistd.Exception.AspNetCore.Localization;
using Microsoft.AspNetCore.Localization;

namespace CompanyName.ProjectName.Api.Extensions;

public static class LocalizationServiceCollectionExtensions
{
    private static readonly CultureInfo[] SupportedCultures =
    [
        new("en-US"),
        new("zh-CN")
    ];

    public static IServiceCollection AddMyProjectLocalization(this IServiceCollection services)
    {
        services.AddLocalization(options => options.ResourcesPath = "Localization/Resources");
        services.AddSingleton<IExceptionResponseLocalizer, ResourceExceptionResponseLocalizer>();
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture("en-US");
            options.SupportedCultures = SupportedCultures;
            options.SupportedUICultures = SupportedCultures;
        });

        return services;
    }
}
