using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Documentation;

/// <summary>
/// Manual implementation for endpoints marked with x-manual-implementation in the schema.
/// This partial class provides concrete implementations for endpoints that return non-JSON content.
/// </summary>
public partial class DocumentationController
{
    /// <summary>
    /// View documentation page in browser - returns HTML content.
    /// This is a manual implementation because it returns HTML, not JSON.
    /// </summary>
    [HttpGet("documentation/view/{slug}")]
    public async Task<IActionResult> ViewDocumentBySlug(string slug, [FromQuery] string? ns, CancellationToken cancellationToken = default)
    {
        var (statusCode, result) = await _implementation.GetDocumentAsync(
            new GetDocumentRequest { Slug = slug, Namespace = ns ?? "bannou" },
            cancellationToken);

        if (statusCode != StatusCodes.OK || result == null)
        {
            return statusCode switch
            {
                StatusCodes.NotFound => NotFound($"Document '{slug}' not found in namespace '{ns ?? "bannou"}'"),
                _ => StatusCode((int)statusCode, "Error retrieving document")
            };
        }

        // Return HTML content
        return Content(result.Document.Content, "text/html");
    }
}
