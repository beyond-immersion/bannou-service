#nullable enable

namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Declares that this method's implementation MUST acquire a distributed lock
/// before performing any state store writes on ANY code path.
/// </summary>
/// <remarks>
/// <para>
/// The structural test <c>LockRequired_ImplementationsAcquireLockBeforeWrites</c>
/// (in <c>structural-tests/DistributedLockTests.cs</c>) enforces this by scanning the
/// implementation source file. The rule is:
/// </para>
/// <list type="bullet">
///   <item>The implementation method body must contain a <c>LockAsync(</c> call.</item>
///   <item>No direct state store write (<c>SaveAsync</c>, <c>DeleteAsync</c>, <c>TrySaveAsync</c>,
///     <c>UpdateWithRetryAsync</c>) may appear in the method body before the lock acquisition.</item>
///   <item>No call to a private or internal helper method in the same class that transitively
///     contains a state store write may appear before the lock acquisition.</item>
/// </list>
/// <para>
/// <b>Why this attribute exists:</b> A method may branch early on a fast-path condition
/// and call a helper that writes to state before the lock block is reached, creating a
/// concurrent-write race that the rest of the method's locking strategy appears to prevent.
/// Reviewers and the compiler cannot detect this by reading the primary method alone —
/// they have to trace every early-return path through every helper. This attribute plus
/// its structural test makes that trace mechanical.
/// </para>
/// <para>
/// <b>Where to apply it:</b>
/// </para>
/// <list type="bullet">
///   <item>On a <b>concrete class method</b> (including overrides of interface methods):
///     the structural test will check only that one method. Use this when the locking
///     contract is specific to one implementation.</item>
///   <item>On an <b>interface method</b>: the structural test will walk every class that
///     implements the interface in plugin assemblies and check each implementation.
///     Use this only when ALL implementations of the interface genuinely require a
///     distributed lock — do not annotate an interface method speculatively, because
///     every existing implementation must already comply or the test will fail for
///     each of them.</item>
/// </list>
/// <para>
/// <b>The <see cref="LockScope"/> parameter is informational.</b> It documents the
/// expected lock resource identifier (e.g., <c>"transition:{entityId}"</c>) in source
/// and in failure messages. The structural test does not parse it — the test only
/// verifies that a <c>LockAsync(</c> call is lexically present before any write. The
/// developer is responsible for choosing a lock scope that correctly serializes
/// concurrent access.
/// </para>
/// <para>
/// <b>What the structural test does NOT check:</b>
/// </para>
/// <list type="bullet">
///   <item>The correctness of the lock key (whether it actually prevents the race).</item>
///   <item>Lock release semantics (<c>await using</c> vs manual disposal).</item>
///   <item>Writes performed via inter-service client calls (<c>_someClient.Xyz(...)</c>) —
///     these are not state store writes and the structural test does not scan for them.</item>
///   <item>Writes performed by methods in other classes.</item>
/// </list>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RequiresDistributedLockAttribute : Attribute
{
    /// <summary>
    /// Informational description of the expected lock resource identifier (e.g.,
    /// <c>"transition:{entityId}"</c>). Surfaces in the structural test's failure
    /// message to help the developer locate the correct lock scope — but is not
    /// parsed or validated by the test itself.
    /// </summary>
    public string LockScope { get; }

    /// <summary>
    /// Creates a new distributed lock requirement declaration.
    /// </summary>
    /// <param name="lockScope">
    /// Informational description of the expected lock resource identifier.
    /// Example: <c>"transition:{entityId}"</c>.
    /// </param>
    public RequiresDistributedLockAttribute(string lockScope)
    {
        LockScope = lockScope;
    }
}
