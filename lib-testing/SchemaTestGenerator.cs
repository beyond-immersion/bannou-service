using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Generates test cases automatically from OpenAPI schema definitions
/// </summary>
public class SchemaTestGenerator
{
    private readonly IDeserializer _yamlDeserializer;
    private readonly JsonSerializerOptions _jsonOptions;

    public SchemaTestGenerator()
    {
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true
        };
    }

    /// <summary>
    /// Generate test handlers from OpenAPI schema files
    /// </summary>
    /// <param name="schemaPath">Path to OpenAPI YAML schema file</param>
    /// <returns>Array of generated service tests</returns>
    public async Task<ServiceTest[]> GenerateTestsFromSchema(string schemaPath)
    {
        var schemaContent = await File.ReadAllTextAsync(schemaPath);
        var schema = _yamlDeserializer.Deserialize<OpenApiSchema>(schemaContent);

        var tests = new List<ServiceTest>();

        foreach (var path in schema.Paths)
        {
            foreach (var operation in path.Value.Operations)
            {
                // Generate success test
                tests.Add(CreateSuccessTest(schema, path.Key, operation.Key, operation.Value));

                // Generate validation failure tests
                tests.AddRange(CreateValidationFailureTests(schema, path.Key, operation.Key, operation.Value));

                // Generate authorization tests if security is defined
                if (operation.Value.Security?.Any() == true)
                {
                    tests.Add(CreateAuthorizationTest(schema, path.Key, operation.Key, operation.Value));
                }
            }
        }

        return tests.ToArray();
    }

    private ServiceTest CreateSuccessTest(OpenApiSchema schema, string path, string method, OpenApiOperation operation)
    {
        var testName = $"{operation.OperationId}_Success";
        var testType = GetServiceNameFromPath(path);
        var description = $"Test successful {operation.Summary?.ToLowerInvariant() ?? operation.OperationId}";

        return new ServiceTest(
            async (client, args) => await ExecuteSuccessTest(client, schema, path, method, operation),
            testName,
            testType,
            description
        );
    }

    private ServiceTest[] CreateValidationFailureTests(OpenApiSchema schema, string path, string method, OpenApiOperation operation)
    {
        var tests = new List<ServiceTest>();
        var testType = GetServiceNameFromPath(path);

        if (operation.RequestBody?.Content?.ContainsKey("application/json") == true)
        {
            var requestSchema = operation.RequestBody.Content["application/json"].Schema;
            if (requestSchema?.Properties != null)
            {
                foreach (var property in requestSchema.Properties)
                {
                    if (requestSchema.Required?.Contains(property.Key) == true)
                    {
                        var testName = $"{operation.OperationId}_Missing{ToPascalCase(property.Key)}";
                        var description = $"Test {operation.OperationId} with missing required field: {property.Key}";

                        tests.Add(new ServiceTest(
                            async (client, args) => await ExecuteValidationTest(client, schema, path, method, operation, property.Key, ValidationTestType.MissingRequired),
                            testName,
                            testType,
                            description
                        ));
                    }

                    // Generate type validation tests
                    if (property.Value.Type != null)
                    {
                        var testName = $"{operation.OperationId}_Invalid{ToPascalCase(property.Key)}Type";
                        var description = $"Test {operation.OperationId} with invalid type for field: {property.Key}";

                        tests.Add(new ServiceTest(
                            async (client, args) => await ExecuteValidationTest(client, schema, path, method, operation, property.Key, ValidationTestType.InvalidType),
                            testName,
                            testType,
                            description
                        ));
                    }
                }
            }
        }

        return tests.ToArray();
    }

    private ServiceTest CreateAuthorizationTest(OpenApiSchema schema, string path, string method, OpenApiOperation operation)
    {
        var testName = $"{operation.OperationId}_Unauthorized";
        var testType = GetServiceNameFromPath(path);
        var description = $"Test {operation.OperationId} without proper authorization";

        return new ServiceTest(
            async (client, args) => await ExecuteAuthorizationTest(client, schema, path, method, operation),
            testName,
            testType,
            description
        );
    }

    private async Task<TestResult> ExecuteSuccessTest(ITestClient client, OpenApiSchema schema, string path, string method, OpenApiOperation operation)
    {
        try
        {
            // Ensure client is authenticated if required
            if (operation.Security?.Any() == true && !client.IsAuthenticated)
            {
                var authSuccess = await AuthenticateClient(client);
                if (!authSuccess)
                    return TestResult.Failed("Could not authenticate client for test");
            }

            // Generate valid request body
            var requestBody = GenerateValidRequestBody(operation);
            var endpoint = ConvertPathToEndpoint(path);

            object? response;
            if (method.Equals("post", StringComparison.OrdinalIgnoreCase))
            {
                var result = await client.PostAsync<object>(endpoint, requestBody);
                if (!result.Success)
                    return TestResult.Failed($"Request failed: {result.ErrorMessage}");
                response = result.Data;
            }
            else if (method.Equals("get", StringComparison.OrdinalIgnoreCase))
            {
                var result = await client.GetAsync<object>(endpoint);
                if (!result.Success)
                    return TestResult.Failed($"Request failed: {result.ErrorMessage}");
                response = result.Data;
            }
            else
            {
                return TestResult.Failed($"Unsupported HTTP method: {method}");
            }

            // Validate response against schema
            var validationResult = ValidateResponse(response, operation);
            if (!validationResult.IsValid)
                return TestResult.Failed($"Response validation failed: {validationResult.ErrorMessage}");

            return TestResult.Successful($"{operation.OperationId} completed successfully via {client.TransportType}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test execution failed: {ex.Message}", ex);
        }
    }

    private async Task<TestResult> ExecuteValidationTest(ITestClient client, OpenApiSchema schema, string path, string method, OpenApiOperation operation, string fieldName, ValidationTestType testType)
    {
        try
        {
            // Ensure client is authenticated if required
            if (operation.Security?.Any() == true && !client.IsAuthenticated)
            {
                var authSuccess = await AuthenticateClient(client);
                if (!authSuccess)
                    return TestResult.Failed("Could not authenticate client for test");
            }

            // Generate invalid request body based on test type
            var requestBody = GenerateInvalidRequestBody(operation, fieldName, testType);
            var endpoint = ConvertPathToEndpoint(path);

            if (method.Equals("post", StringComparison.OrdinalIgnoreCase))
            {
                var result = await client.PostAsync<object>(endpoint, requestBody);

                // Expect failure for validation tests
                if (result.Success)
                    return TestResult.Failed($"Expected validation failure but request succeeded");

                // Check if error indicates validation failure (400 status)
                if (result.StatusCode != 400)
                    return TestResult.Failed($"Expected 400 Bad Request but got {result.StatusCode}");
            }
            else
            {
                return TestResult.Failed($"Unsupported HTTP method for validation test: {method}");
            }

            return TestResult.Successful($"Validation test passed: {fieldName} validation correctly failed");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Validation test execution failed: {ex.Message}", ex);
        }
    }

    private async Task<TestResult> ExecuteAuthorizationTest(ITestClient client, OpenApiSchema schema, string path, string method, OpenApiOperation operation)
    {
        try
        {
            // Ensure client is NOT authenticated for this test
            var originalAuthState = client.IsAuthenticated;

            // Generate valid request body
            var requestBody = GenerateValidRequestBody(operation);
            var endpoint = ConvertPathToEndpoint(path);

            if (method.Equals("post", StringComparison.OrdinalIgnoreCase))
            {
                var result = await client.PostAsync<object>(endpoint, requestBody);

                // Expect authorization failure
                if (result.Success)
                    return TestResult.Failed("Expected authorization failure but request succeeded");

                // Check if error indicates authorization failure (401 status)
                if (result.StatusCode != 401)
                    return TestResult.Failed($"Expected 401 Unauthorized but got {result.StatusCode}");
            }
            else if (method.Equals("get", StringComparison.OrdinalIgnoreCase))
            {
                var result = await client.GetAsync<object>(endpoint);

                if (result.Success)
                    return TestResult.Failed("Expected authorization failure but request succeeded");

                if (result.StatusCode != 401)
                    return TestResult.Failed($"Expected 401 Unauthorized but got {result.StatusCode}");
            }
            else
            {
                return TestResult.Failed($"Unsupported HTTP method for authorization test: {method}");
            }

            return TestResult.Successful("Authorization test passed: properly rejected unauthorized request");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Authorization test execution failed: {ex.Message}", ex);
        }
    }

    private async Task<bool> AuthenticateClient(ITestClient client)
    {
        // Try to authenticate with test credentials
        var testUsername = $"testuser_{DateTime.Now.Ticks}";
        var testPassword = "TestPassword123!";

        // First register the test user
        var registerSuccess = await client.RegisterAsync(testUsername, testPassword);
        if (registerSuccess) return true;

        // If registration failed, try logging in (user might already exist)
        return await client.LoginAsync(testUsername, testPassword);
    }

    private object GenerateValidRequestBody(OpenApiOperation operation)
    {
        if (operation.RequestBody?.Content?.ContainsKey("application/json") != true)
            return new { };

        var schema = operation.RequestBody.Content["application/json"].Schema;
        return GenerateObjectFromSchema(schema, false);
    }

    private object GenerateInvalidRequestBody(OpenApiOperation operation, string fieldName, ValidationTestType testType)
    {
        if (operation.RequestBody?.Content?.ContainsKey("application/json") != true)
            return new { };

        var schema = operation.RequestBody.Content["application/json"].Schema;
        return GenerateObjectFromSchema(schema, false, fieldName, testType);
    }

    private object GenerateObjectFromSchema(OpenApiSchemaProperty? schema, bool includeOptional, string? invalidField = null, ValidationTestType? validationType = null)
    {
        if (schema?.Properties == null)
            return new { };

        var result = new Dictionary<string, object?>();

        foreach (var property in schema.Properties)
        {
            var isRequired = schema.Required?.Contains(property.Key) == true;

            // Handle invalid field generation
            if (invalidField == property.Key && validationType != null)
            {
                switch (validationType)
                {
                    case ValidationTestType.MissingRequired:
                        // Skip this field to make it missing
                        continue;
                    case ValidationTestType.InvalidType:
                        // Provide wrong type (string instead of number, etc.)
                        result[property.Key] = GenerateInvalidValue(property.Value);
                        continue;
                }
            }

            // Include field if it's required or we're including optional fields
            if (isRequired || includeOptional)
            {
                result[property.Key] = GenerateValueFromProperty(property.Value);
            }
        }

        return result;
    }

    private object? GenerateValueFromProperty(OpenApiSchemaProperty property)
    {
        return property.Type switch
        {
            "string" => property.Format switch
            {
                "email" => "test@example.com",
                "date-time" => DateTime.UtcNow.ToString("O"),
                "uuid" => Guid.NewGuid().ToString(),
                _ => property.Example?.ToString() ?? $"test_string_{DateTime.Now.Ticks}"
            },
            "integer" => property.Example?.ToString() ?? "123",
            "number" => property.Example?.ToString() ?? "123.45",
            "boolean" => property.Example?.ToString() ?? "false",
            "array" => new List<object>(),
            _ => property.Example ?? "default_value"
        };
    }

    private object GenerateInvalidValue(OpenApiSchemaProperty property)
    {
        return property.Type switch
        {
            "string" => 12345, // number instead of string
            "integer" => "not_a_number",
            "number" => "not_a_number",
            "boolean" => "not_a_boolean",
            "array" => "not_an_array",
            _ => "invalid_value"
        };
    }

    private (bool IsValid, string? ErrorMessage) ValidateResponse(object? response, OpenApiOperation operation)
    {
        // Basic validation - could be enhanced with JSON schema validation
        if (response == null && operation.Responses?.ContainsKey("200") == true)
        {
            var successResponse = operation.Responses["200"];
            if (successResponse.Content?.Any() == true)
                return (false, "Expected response content but got null");
        }

        return (true, null);
    }

    private string GetServiceNameFromPath(string path)
    {
        var segments = path.Trim('/').Split('/');
        return segments.Length > 0 ? ToPascalCase(segments[0]) : "Unknown";
    }

    private string ConvertPathToEndpoint(string path)
    {
        // Convert OpenAPI path to actual endpoint
        // e.g., "/create" -> "api/accounts/create"
        return $"api{path}";
    }

    private string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return char.ToUpperInvariant(input[0]) + input[1..];
    }
}

public enum ValidationTestType
{
    MissingRequired,
    InvalidType,
    InvalidFormat,
    OutOfRange
}

// OpenAPI Schema Models for YAML deserialization
public class OpenApiSchema
{
    public OpenApiInfo Info { get; set; } = new();
    public Dictionary<string, OpenApiPathItem> Paths { get; set; } = new();
    public OpenApiComponents? Components { get; set; }
}

public class OpenApiInfo
{
    public string Title { get; set; } = "";
    public string Version { get; set; } = "";
    public string? Description { get; set; }
}

public class OpenApiPathItem
{
    public Dictionary<string, OpenApiOperation> Operations => new Dictionary<string, OpenApiOperation>
    {
        { "get", Get ?? new OpenApiOperation() },
        { "post", Post ?? new OpenApiOperation() },
        { "put", Put ?? new OpenApiOperation() },
        { "delete", Delete ?? new OpenApiOperation() }
    }.Where(x => x.Value.OperationId != null).ToDictionary(x => x.Key, x => x.Value);

    public OpenApiOperation? Get { get; set; }
    public OpenApiOperation? Post { get; set; }
    public OpenApiOperation? Put { get; set; }
    public OpenApiOperation? Delete { get; set; }
}

public class OpenApiOperation
{
    public string? OperationId { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public OpenApiRequestBody? RequestBody { get; set; }
    public Dictionary<string, OpenApiResponse>? Responses { get; set; }
    public List<Dictionary<string, object>>? Security { get; set; }
    public string[]? Tags { get; set; }
}

public class OpenApiRequestBody
{
    public bool Required { get; set; }
    public Dictionary<string, OpenApiMediaType> Content { get; set; } = new();
}

public class OpenApiMediaType
{
    public OpenApiSchemaProperty Schema { get; set; } = new();
}

public class OpenApiResponse
{
    public string? Description { get; set; }
    public Dictionary<string, OpenApiMediaType>? Content { get; set; }
}

public class OpenApiSchemaProperty
{
    public string? Type { get; set; }
    public string? Format { get; set; }
    public object? Example { get; set; }
    public Dictionary<string, OpenApiSchemaProperty>? Properties { get; set; }
    public string[]? Required { get; set; }
    public OpenApiSchemaProperty? Items { get; set; }
}

public class OpenApiComponents
{
    public Dictionary<string, OpenApiSchemaProperty>? Schemas { get; set; }
}
