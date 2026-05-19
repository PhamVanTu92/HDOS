namespace ReportingPlatform.ProviderSdk.Tests.Helpers;

internal sealed class FakeTokenHandler : HttpMessageHandler
{
    private readonly List<(HttpStatusCode code, string body)> _responses = new();
    private int _callCount;
    public int CallCount => _callCount;

    public void SetupSuccess(string token = "test-jwt-token", int expiresIn = 900)
    {
        _responses.Clear();
        _responses.Add((HttpStatusCode.OK,
            $$"""{"accessToken":"{{token}}","expiresIn":{{expiresIn}},"tokenType":"Bearer"}"""));
    }

    public void SetupAlwaysReturn(HttpStatusCode code, string body = "{\"error\":\"invalid_client\"}")
    {
        _responses.Clear();
        _responses.Add((code, body));
    }

    public void AddResponse(HttpStatusCode code, string body)
        => _responses.Add((code, body));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var idx = Math.Min(Interlocked.Increment(ref _callCount) - 1, _responses.Count - 1);
        var (code, body) = _responses[idx];
        return Task.FromResult(new HttpResponseMessage(code)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        });
    }
}
