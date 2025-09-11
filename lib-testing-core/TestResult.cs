namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Result of executing a test
/// </summary>
public record TestResult(bool Success, string Message, Exception? Exception = null)
{
    public static TestResult Successful(string message = "Test completed successfully")
        => new(true, message);

    public static TestResult Failed(string message, Exception? exception = null)
        => new(false, message, exception);
}

/// <summary>
/// Response from a test client API call
/// </summary>
public record TestResponse<T>(bool Success, int StatusCode, T? Data, string? ErrorMessage = null) where T : class
{
    public static TestResponse<T> Successful(T data, int statusCode = 200)
        => new(true, statusCode, data);

    public static TestResponse<T> Failed(int statusCode, string errorMessage)
        => new(false, statusCode, null, errorMessage);
}
