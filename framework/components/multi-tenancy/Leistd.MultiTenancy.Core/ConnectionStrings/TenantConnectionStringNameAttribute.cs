namespace Leistd.MultiTenancy;

/// <summary>
/// Declares the connection-string name resolved for a DbContext.
/// </summary>
/// <remarks>
/// DbContexts without this attribute use <c>Default</c>. A host can pin a control-plane
/// DbContext to a separate named connection while tenant business DbContexts continue
/// to use the tenant-aware default connection.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class TenantConnectionStringNameAttribute : Attribute
{
    /// <summary>Creates a connection-string name declaration.</summary>
    public TenantConnectionStringNameAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>The name passed to <see cref="ITenantConnectionStringResolver"/>.</summary>
    public string Name { get; }
}
