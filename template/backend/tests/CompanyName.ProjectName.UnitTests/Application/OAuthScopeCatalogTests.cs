#if (OpenIddictServer)
using CompanyName.ProjectName.Application.Auth.OAuth;
using CompanyName.ProjectName.Application.Auth.Options;

namespace CompanyName.ProjectName.UnitTests.Application;

public sealed class OAuthScopeCatalogTests
{
    [Fact]
    public void The_default_catalog_excludes_retired_machine_scopes()
    {
        string[] retired =
        [
            "tenant-routing.read", "tenant-migration.read", "account-preferences.read",
            "system-directory.read", "administrators.read"
        ];

        Assert.DoesNotContain(OAuthScopes.All(new OAuthResourceOptions()), scope => retired.Contains(scope.Name));
    }
}
#endif
