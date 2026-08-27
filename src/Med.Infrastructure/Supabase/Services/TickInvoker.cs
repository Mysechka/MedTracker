using System.Net.Http.Headers;
using Med.Application.Abstractions;
using Med.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Med.Infrastructure.Supabase.Services;

public sealed class TickInvoker : ITickInvoker
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAuthService _auth;
    private readonly SupabaseOptions _options;

    public TickInvoker(
        IHttpClientFactory httpClientFactory,
        IAuthService auth,
        IOptions<SupabaseOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _auth = auth;
        _options = options.Value;
    }

    public async Task<TickInvokeResult> InvokeAsync(CancellationToken cancellationToken = default)
    {
        AuthSession session = _auth.CurrentSession
            ?? throw new InvalidOperationException("Нужна активная сессия.");

        string baseUrl = _options.Url.TrimEnd('/');
        using HttpRequestMessage request = new(HttpMethod.Post, $"{baseUrl}/functions/v1/tick");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        request.Headers.TryAddWithoutValidation("apikey", _options.AnonKey);
        request.Content = new StringContent("{\"source\":\"diagnostics\"}", System.Text.Encoding.UTF8, "application/json");

        HttpClient client = _httpClientFactory.CreateClient(nameof(TickInvoker));
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new TickInvokeResult(response.IsSuccessStatusCode, body);
    }
}
