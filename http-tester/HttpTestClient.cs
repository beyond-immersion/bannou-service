using BeyondImmersion.BannouService.Testing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;

namespace BeyondImmersion.BannouService.HttpTester;

/// <summary>
/// HTTP test client that makes direct calls to Bannou service endpoints
/// </summary>
public class HttpTestClient : ITestClient, IDisposable
{
    private readonly TestConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private string? _accessToken;
    private string? _refreshToken;

    public HttpTestClient(TestConfiguration configuration)
    {
        _configuration = configuration;

        // Allow "insecure" (curl -k) SSL validation, for testing
        _httpClient = new HttpClient(new HttpClientHandler()
        {
            AllowAutoRedirect = true,
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
        });
    }

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(_accessToken);

    public string TransportType => "HTTP";

    public async Task<bool> RegisterAsync(string username, string password)
    {
        try
        {
            var serverUri = new Uri($"{_configuration.Http_Base_Url?.TrimEnd('/')}/{_configuration.Register_Endpoint?.TrimStart('/')}");
            Console.WriteLine($"Registration Uri: {serverUri}");

            var contentObj = new JObject()
            {
                ["username"] = username,
                ["password"] = password
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, serverUri);
            var jsonContentStr = JsonConvert.SerializeObject(contentObj);
            var strContent = new StringContent(jsonContentStr, Encoding.UTF8, "application/json");
            httpRequest.Content = strContent;

            using var response = await _httpClient.SendAsync(httpRequest);
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                Console.WriteLine($"Registration failed: {response.StatusCode}, Reason: {response.ReasonPhrase}");
                return false;
            }

            var responseStr = await response.Content.ReadAsStringAsync();
            var responseObj = JObject.Parse(responseStr);

            _accessToken = (string?)responseObj["access_token"];
            _refreshToken = (string?)responseObj["refresh_token"];

            return !string.IsNullOrWhiteSpace(_accessToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Registration exception: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        try
        {
            var serverUri = new Uri($"{_configuration.Http_Base_Url?.TrimEnd('/')}/{_configuration.Login_Credentials_Endpoint?.TrimStart('/')}");
            Console.WriteLine($"Login Uri: {serverUri}");

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, serverUri);
            httpRequest.Headers.Add("username", username);
            httpRequest.Headers.Add("password", password);
            httpRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(httpRequest);
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                Console.WriteLine($"Login failed: {response.StatusCode}, Reason: {response.ReasonPhrase}");
                return false;
            }

            var responseStr = await response.Content.ReadAsStringAsync();
            var responseObj = JObject.Parse(responseStr);

            _accessToken = (string?)responseObj["access_token"];
            _refreshToken = (string?)responseObj["refresh_token"];

            return !string.IsNullOrWhiteSpace(_accessToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Login exception: {ex.Message}");
            return false;
        }
    }

    public async Task<TestResponse<T>> PostAsync<T>(string endpoint, object? requestBody = null) where T : class
    {
        return await MakeRequestAsync<T>(HttpMethod.Post, endpoint, requestBody);
    }

    public async Task<TestResponse<T>> GetAsync<T>(string endpoint) where T : class
    {
        return await MakeRequestAsync<T>(HttpMethod.Get, endpoint, null);
    }

    private async Task<TestResponse<T>> MakeRequestAsync<T>(HttpMethod method, string endpoint, object? requestBody) where T : class
    {
        try
        {
            if (!IsAuthenticated)
                return TestResponse<T>.Failed(401, "Client not authenticated");

            var serverUri = new Uri($"{_configuration.Http_Base_Url?.TrimEnd('/')}/{endpoint.TrimStart('/')}");

            using var httpRequest = new HttpRequestMessage(method, serverUri);

            // Add authorization header
            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

            // Add request body if provided
            if (requestBody != null && method != HttpMethod.Get)
            {
                var jsonContent = JsonConvert.SerializeObject(requestBody);
                httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            }

            using var response = await _httpClient.SendAsync(httpRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return TestResponse<T>.Failed((int)response.StatusCode,
                    $"{response.StatusCode}: {response.ReasonPhrase} - {responseContent}");
            }

            // Try to deserialize response
            T? data = null;
            if (!string.IsNullOrWhiteSpace(responseContent))
            {
                try
                {
                    data = JsonConvert.DeserializeObject<T>(responseContent);
                }
                catch (JsonException ex)
                {
                    return TestResponse<T>.Failed(500, $"Failed to deserialize response: {ex.Message}");
                }
            }

            return TestResponse<T>.Successful(data!, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            return TestResponse<T>.Failed(500, $"Request exception: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
