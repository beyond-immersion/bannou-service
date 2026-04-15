#nullable enable

namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Marks a generated property as intentionally polymorphic — the schema-level type
/// is deliberately broader than the runtime type used by some consumers.
/// </summary>
/// <remarks>
/// <para>
/// <b>SCHEMA-FIRST:</b> This attribute is generated from <c>x-polymorphic-type: true</c>
/// in the service's OpenAPI schema. The post-processing script
/// <c>postprocess-polymorphic-type.py</c> reads the schema annotation and emits
/// this attribute on the corresponding generated C# property.
/// </para>
/// <para>
/// Properties with this attribute are excluded from the structural test
/// <c>Services_NoParseOnGeneratedResponseProperties</c> (T25 type safety enforcement).
/// The test detects <c>Guid.Parse</c> / <c>int.Parse</c> calls on generated model
/// properties — each such call implies the schema declares a <c>string</c> while
/// the consumer operates on a stronger type. This attribute declares that the
/// string typing is intentional and the parse is expected.
/// </para>
/// <para>
/// <b>Common patterns:</b>
/// </para>
/// <list type="bullet">
///   <item><b>Discriminated union:</b> A field that carries a GUID when one
///     discriminator value is set, and a logical service name when another is set.
///     Example: Analytics <c>serviceId</c> — GUID when <c>serviceType == Game</c>,
///     logical name when <c>serviceType == System</c>.</item>
///   <item><b>Intentionally string-typed identifier:</b> A field that is <c>string</c>
///     in the owning schema because the owner allows non-GUID values, but specific
///     consumers always pass GUIDs and need to parse the response back. Example:
///     Actor <c>actorId</c> — string in Actor's schema (allows custom IDs), but
///     Genesis and Puppetmaster operate on it as a GUID.</item>
/// </list>
/// <para>
/// <b>When NOT to use:</b> If the field should simply be <c>format: uuid</c> in the
/// schema, tighten the schema instead of applying this attribute. This attribute is
/// for fields where the string typing is a deliberate design choice, not an oversight.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class PolymorphicTypeAttribute : Attribute
{
}
