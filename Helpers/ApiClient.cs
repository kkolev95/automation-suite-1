using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TestIT.ApiTests.Helpers;

public class ApiClient : IDisposable
{
    [ThreadStatic] private static Action<string>? _log;
    public static void SetOutput(Action<string> log) { _log = log; }

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly bool _enableRetry;

    public ApiClient(string baseUrl, bool enableRetry = true)
    {
        _enableRetry = enableRetry;
        _baseUrl = baseUrl;

        // Create HttpClientHandler to force HTTP/1.1 (matching Postman/curl behavior)
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10
        };

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(_baseUrl)
        };

        // Force HTTP/1.1 to match Postman's behavior
        _httpClient.DefaultRequestVersion = new Version(1, 1);
        _httpClient.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;

        // Keep headers minimal like Postman - just Accept header
        _httpClient.DefaultRequestHeaders.Add("Accept", "*/*");

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public void SetAuthToken(string token)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public void ClearAuthToken()
    {
        _httpClient.DefaultRequestHeaders.Authorization = null;
    }

    private static readonly int[] RetryDelaysMs = { 1000, 2000 };

    private static void Log(string method, string endpoint, HttpResponseMessage response)
    {
        try { _log?.Invoke($"{method} {endpoint}  →  {(int)response.StatusCode} {response.StatusCode}"); }
        catch (InvalidOperationException) { /* output helper expired — test already finished */ }
    }

    /// <summary>
    /// Executes a request, retrying up to 2 times (3 total attempts) on 5xx responses
    /// or network failures. 4xx responses are returned immediately — they are expected
    /// test assertions and must not be retried.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<Task<HttpResponseMessage>> sendRequest, string method, string endpoint)
    {
        if (!_enableRetry)
        {
            var r = await sendRequest();
            Log(method, endpoint, r);
            return r;
        }

        HttpResponseMessage? response = null;
        for (int attempt = 0; attempt <= RetryDelaysMs.Length; attempt++)
        {
            try
            {
                response = await sendRequest();
                if ((int)response.StatusCode < 500)
                    break;

                if (attempt < RetryDelaysMs.Length)
                {
                    try { _log?.Invoke($"{method} {endpoint}  →  {(int)response.StatusCode} (retry {attempt + 1})"); }
                    catch (InvalidOperationException) { }
                    await Task.Delay(RetryDelaysMs[attempt]);
                }
            }
            catch (HttpRequestException) when (attempt < RetryDelaysMs.Length)
            {
                await Task.Delay(RetryDelaysMs[attempt]);
            }
        }
        Log(method, endpoint, response!);
        return response!;
    }

    public Task<HttpResponseMessage> GetAsync(string endpoint) =>
        SendWithRetryAsync(() => _httpClient.GetAsync(endpoint), "GET", endpoint);

    public async Task<T?> GetAsync<T>(string endpoint)
    {
        var response = await GetAsync(endpoint);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) return default;
        return JsonSerializer.Deserialize<T>(content, _jsonOptions);
    }

    public Task<HttpResponseMessage> PostAsync<T>(string endpoint, T data) =>
        SendWithRetryAsync(() =>
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return _httpClient.PostAsync(endpoint, content);
        }, "POST", endpoint);

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(string endpoint, TRequest data)
    {
        var response = await PostAsync(endpoint, data);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) return default;
        return JsonSerializer.Deserialize<TResponse>(content, _jsonOptions);
    }

    public Task<HttpResponseMessage> PutAsync<T>(string endpoint, T data) =>
        SendWithRetryAsync(() =>
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return _httpClient.PutAsync(endpoint, content);
        }, "PUT", endpoint);

    public Task<HttpResponseMessage> PatchAsync<T>(string endpoint, T data) =>
        SendWithRetryAsync(() =>
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Patch, endpoint) { Content = content };
            return _httpClient.SendAsync(request);
        }, "PATCH", endpoint);

    public Task<HttpResponseMessage> DeleteAsync(string endpoint) =>
        SendWithRetryAsync(() => _httpClient.DeleteAsync(endpoint), "DELETE", endpoint);

    public async Task<string> GetResponseBodyAsync(HttpResponseMessage response)
    {
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<T?> DeserializeResponseAsync<T>(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(content, _jsonOptions);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
