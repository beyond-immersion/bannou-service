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
    /// View documentation page in browser - returns fully rendered HTML page.
    /// This is a manual implementation because it returns HTML, not JSON.
    /// </summary>
    [HttpGet("documentation/view/{slug}")]
    [Produces("text/html")]
    public async Task<IActionResult> ViewDocumentBySlug(string slug, [FromQuery] string? ns, CancellationToken cancellationToken = default)
    {
        // Cast to concrete implementation to access ViewDocumentBySlugAsync (not on interface due to x-manual-implementation)
        var documentationService = (DocumentationService)_implementation;
        var (statusCode, html) = await documentationService.ViewDocumentBySlugAsync(slug, ns, cancellationToken);

        if (statusCode != StatusCodes.OK || html == null)
        {
            return statusCode switch
            {
                StatusCodes.NotFound => NotFound($"Document '{slug}' not found in namespace '{ns ?? "bannou"}'"),
                _ => StatusCode((int)statusCode, "Error retrieving document")
            };
        }

        // Return full HTML page with rendered markdown
        return Content(html, "text/html");
    }

    /// <summary>
    /// Get raw markdown content for a document.
    /// This is a manual implementation because it returns text/markdown, not JSON.
    /// </summary>
    [HttpGet("documentation/raw/{slug}")]
    [Produces("text/markdown")]
    public async Task<IActionResult> RawDocumentBySlug(string slug, [FromQuery] string? ns, CancellationToken cancellationToken = default)
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

        // Return raw markdown content
        if (result.Document.Content == null)
        {
            return NotFound($"Document '{slug}' has no content in namespace '{ns ?? "bannou"}'");
        }
        return Content(result.Document.Content, "text/markdown; charset=utf-8");
    }
}
