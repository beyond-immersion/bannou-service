namespace BeyondImmersion.Bannou.Core;

/// <summary>
/// Single source of truth for API exceptions across all Bannou services and SDKs.
/// Used by all generated clients and service models.
/// </summary>
[System.CodeDom.Compiler.GeneratedCode("NSwag", "14.2.0.0 (NJsonSchema v11.1.0.0 (Newtonsoft.Json v13.0.0.0))")]
public partial class ApiException : System.Exception
{
    /// <summary>
    /// HTTP status code of the error response.
    /// </summary>
    public int StatusCode { get; private set; }

    /// <summary>
    /// Raw response body content, if available.
    /// </summary>
    public string? Response { get; private set; }

    /// <summary>
    /// Response headers from the failed request.
    /// </summary>
    public System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IEnumerable<string>> Headers { get; private set; }

    private static readonly System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IEnumerable<string>> EmptyHeaders =
        new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IEnumerable<string>>();

    /// <summary>
    /// Creates a new ApiException with the specified details.
    /// </summary>
    /// <param name="message">Error message describing the failure.</param>
    /// <param name="statusCode">HTTP status code of the error response.</param>
    /// <param name="response">Raw response body content (optional, defaults to null).</param>
    /// <param name="headers">Response headers from the failed request (optional, defaults to empty).</param>
    /// <param name="innerException">Inner exception that caused this failure (optional).</param>
    public ApiException(
        string message,
        int statusCode,
        string? response = null,
        System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IEnumerable<string>>? headers = null,
        System.Exception? innerException = null)
        : base(message + "\n\nStatus: " + statusCode + "\nResponse: \n" + ((response == null) ? "(null)" : response.Substring(0, response.Length >= 512 ? 512 : response.Length)), innerException)
    {
        StatusCode = statusCode;
        Response = response;
        Headers = headers ?? EmptyHeaders;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return string.Format("HTTP Response: \n\n{0}\n\n{1}", Response, base.ToString());
    }
}

/// <summary>
/// Generic API exception with typed result for structured error responses.
/// </summary>
/// <typeparam name="TResult">Type of the error result payload.</typeparam>
[System.CodeDom.Compiler.GeneratedCode("NSwag", "14.2.0.0 (NJsonSchema v11.1.0.0 (Newtonsoft.Json v13.0.0.0))")]
public partial class ApiException<TResult> : ApiException
{
    /// <summary>
    /// Typed error result from the API response.
    /// </summary>
    public TResult Result { get; private set; }

    /// <summary>
    /// Creates a new ApiException with a typed result.
    /// </summary>
    /// <param name="message">Error message describing the failure.</param>
    /// <param name="statusCode">HTTP status code of the error response.</param>
    /// <param name="response">Raw response body content.</param>
    /// <param name="headers">Response headers from the failed request.</param>
    /// <param name="result">Typed error result from the API.</param>
    /// <param name="innerException">Inner exception that caused this failure.</param>
    /// <remarks>
    /// Parameter order maintained for backwards compatibility with generated clients.
    /// For the non-generic ApiException, use the simplified constructor with optional parameters.
    /// </remarks>
    public ApiException(
        string message,
        int statusCode,
        string? response,
        System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IEnumerable<string>>? headers,
        TResult result,
        System.Exception? innerException)
        : base(message, statusCode, response, headers, innerException)
    {
        Result = result;
    }
}
