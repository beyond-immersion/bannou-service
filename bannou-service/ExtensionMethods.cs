using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace BeyondImmersion.BannouService;

/// <summary>
/// Extension methods for various types used throughout the Bannou service platform.
/// </summary>
public static partial class ExtensionMethods
{
    private static bool sProviderRegistered = false;

    /// <summary>
    /// Regex for stripping out characters that would be invalid in URLs.
    /// </summary>
    [GeneratedRegex("[^a-zA-Z0-9\\s-]")]
    public static partial Regex REGEX_InvalidChars();

    /// <summary>
    /// Regex for replacing single spaces.
    /// </summary>
    [GeneratedRegex("\\s")]
    public static partial Regex REGEX_Spaces();

    /// <summary>
    /// Regex for replacing double spaces.
    /// </summary>
    [GeneratedRegex("\\s+")]
    public static partial Regex REGEX_MultipleSpaces();

    /// <summary>
    /// Converts HttpMethodTypes enum to HttpMethod object.
    /// </summary>
    /// <param name="httpMethod">The HTTP method type to convert.</param>
    /// <returns>The corresponding HttpMethod object.</returns>
    public static HttpMethod ToObject(this HttpMethodTypes httpMethod)
        => httpMethod switch
        {
            HttpMethodTypes.PUT => HttpMethod.Put,
            HttpMethodTypes.DELETE => HttpMethod.Delete,
            HttpMethodTypes.HEAD => HttpMethod.Head,
            HttpMethodTypes.OPTIONS => HttpMethod.Options,
            HttpMethodTypes.PATCH => HttpMethod.Patch,
            HttpMethodTypes.GET => HttpMethod.Get,
            _ => HttpMethod.Post
        };

    /// <summary>
    /// Converts StatusCodes enum to IStatusCodeActionResult for ASP.NET Core responses.
    /// </summary>
    /// <param name="httpStatusCode">The status code to convert.</param>
    /// <param name="value">Optional response value.</param>
    /// <returns>The corresponding IStatusCodeActionResult.</returns>
    public static IStatusCodeActionResult ToActionResult(this StatusCodes httpStatusCode, object? value = null)
    {
        if (value == null)
        {
            return httpStatusCode switch
            {
                StatusCodes.OK => new StatusCodeResult(200),
                StatusCodes.Accepted => new StatusCodeResult(202),
                StatusCodes.BadRequest => new StatusCodeResult(400),
                StatusCodes.Forbidden => new StatusCodeResult(403),
                StatusCodes.NotFound => new StatusCodeResult(404),
                _ => new StatusCodeResult(500)
            };
        }

        return httpStatusCode switch
        {
            StatusCodes.OK => new ObjectResult(value) { StatusCode = 200 },
            StatusCodes.Accepted => new ObjectResult(value) { StatusCode = 202 },
            StatusCodes.BadRequest => new ObjectResult(value) { StatusCode = 400 },
            StatusCodes.Forbidden => new ObjectResult(value) { StatusCode = 403 },
            StatusCodes.NotFound => new ObjectResult(value) { StatusCode = 404 },
            _ => new ObjectResult(value) { StatusCode = 500 }
        };
    }

    /// <summary>
    /// Check if field or property has the "Obsolete" attribute attached.
    /// </summary>
    public static bool IsObsolete(this MemberInfo memberInfo)
        => memberInfo.GetCustomAttribute<ObsoleteAttribute>() != null;

    /// <summary>
    /// Check if field or property has the "Obsolete" attribute attached, and return message if so.
    /// </summary>
    public static bool IsObsolete(this MemberInfo memberInfo, out string? message)
    {
        ObsoleteAttribute? obsAttr = memberInfo.GetCustomAttribute<ObsoleteAttribute>();
        if (obsAttr != null)
        {
            message = obsAttr.Message;
            return true;
        }

        message = null;
        return false;
    }

    /// <summary>
    /// Extract Bearer token from Authorization header.
    /// </summary>
    /// <param name="context">HTTP context containing the request headers</param>
    /// <returns>Bearer token without "Bearer " prefix, or null if not found</returns>
    public static string? ExtractBearerToken(this HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
            return authHeader[7..];

        return null;
    }

    /// <summary>
    /// Extract raw header value by name.
    /// </summary>
    /// <param name="context">HTTP context containing the request headers</param>
    /// <param name="headerName">Name of the header to extract</param>
    /// <returns>Header value or null if not found</returns>
    public static string? ExtractHeader(this HttpContext context, string headerName)
    {
        return context.Request.Headers[headerName].FirstOrDefault();
    }

    /// <summary>
    /// Extract multiple headers into a dictionary.
    /// </summary>
    /// <param name="context">HTTP context containing the request headers</param>
    /// <param name="headerNames">Names of headers to extract</param>
    /// <returns>Dictionary of header name to value (null if header not found)</returns>
    public static Dictionary<string, string?> ExtractHeaders(this HttpContext context, params string[] headerNames)
    {
        var result = new Dictionary<string, string?>();
        foreach (var headerName in headerNames)
            result[headerName] = ExtractHeader(context, headerName);

        return result;
    }

    /// <summary>
    /// Gets all interfaces implemented by a type, including inherited interfaces.
    /// </summary>
    /// <param name="type">The type to examine.</param>
    /// <returns>Array of all implemented interface types.</returns>
    public static Type[] GetAllImplementedInterfaces(this Type? type)
    {
        var interfaces = new List<Type>();

        while (type != null)
        {
            interfaces.AddRange(type.GetInterfaces());
            type = type.BaseType;
        }

        return [.. interfaces.Distinct()];
    }

    /// <summary>
    /// Checks if a string is safe to use as a segment with a Path.Combine().
    /// </summary>
    public static bool IsSafeForPath(this string pathSegment)
    {
        if (string.IsNullOrEmpty(pathSegment))
            return false;

        if (pathSegment.Any(ch => Path.GetInvalidFileNameChars().Contains(ch)))
            return false;

        if (pathSegment.Contains(".."))
            return false;

        if (Path.IsPathRooted(pathSegment))
            return false;

        return true;
    }

    /// <summary>
    /// Generate a URL-safe slug from any string.
    /// </summary>
    public static string GenerateSlug(this string phrase)
    {
        var str = phrase.RemoveAccent().ToLower();
        str = REGEX_InvalidChars().Replace(str, "");
        str = REGEX_MultipleSpaces().Replace(str, " ").Trim();
        str = str[..(str.Length <= 45 ? str.Length : 45)].Trim();
        str = REGEX_Spaces().Replace(str, "-");
        return str;
    }

    /// <summary>
    /// Gets the service name from a service type using BannouServiceAttribute or service info.
    /// </summary>
    /// <param name="serviceType">The service type to examine.</param>
    /// <returns>The service name if found, otherwise null.</returns>
    public static string? GetServiceName(this Type serviceType)
    {
        BannouServiceAttribute? serviceAttr = serviceType.GetCustomAttributes<BannouServiceAttribute>().FirstOrDefault();
        if (serviceAttr != null && !string.IsNullOrWhiteSpace(serviceAttr.Name))
            return serviceAttr.Name;

        var serviceInfo = IBannouService.GetServiceInfo(serviceType);
        if (serviceInfo != null && serviceInfo.HasValue)
            return serviceInfo.Value.Item3.Name;

        return null;
    }

    /// <summary>
    /// Remove accent characters from a string.
    /// Returns new string.
    /// </summary>
    public static string RemoveAccent(this string txt)
    {
        if (!sProviderRegistered)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            sProviderRegistered = true;
        }

        var bytes = Encoding.GetEncoding("Cyrillic").GetBytes(txt);
        return Encoding.ASCII.GetString(bytes);
    }

    /// <summary>
    /// Add property headers.
    /// </summary>
    public static void AddPropertyHeaders(this HttpRequestMessage message, ApiRequest request)
    {
        foreach (var headerKVP in request.SetPropertiesToHeaders())
            foreach (var headerValue in headerKVP.Item2)
                message.Headers.Add(headerKVP.Item1, headerValue);
    }
}
