#nullable enable

namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Declares that this service requires a cleanup method with the specified name.
/// Applied by generate-references.py for each unique cleanup endpoint in x-references.
/// </summary>
/// <remarks>
/// <para>
/// The structural test <c>Service_HasRequiredCleanupMethods</c> scans for this attribute
/// and verifies that the service type has a corresponding method. This catches the case
/// where x-references declares a cleanup callback but the schema endpoint or service
/// implementation is missing.
/// </para>
/// <para>
/// The method name is derived from the cleanup endpoint path (e.g.,
/// <c>/status/cleanup-by-owner</c> → <c>CleanupByOwnerAsync</c>). Multiple x-references
/// targets may share the same endpoint (polymorphic cleanup), producing one attribute.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ResourceCleanupRequiredAttribute : Attribute
{
    /// <summary>
    /// The expected method name (e.g., "CleanupByOwnerAsync").
    /// Derived from the cleanup endpoint path in x-references.
    /// </summary>
    public string MethodName { get; }

    /// <summary>
    /// Creates a new resource cleanup requirement declaration.
    /// </summary>
    /// <param name="methodName">The expected cleanup method name (from x-references cleanup endpoint).</param>
    public ResourceCleanupRequiredAttribute(string methodName)
    {
        MethodName = methodName;
    }
}
