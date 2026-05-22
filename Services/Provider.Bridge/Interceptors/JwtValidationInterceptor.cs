using Grpc.Core.Interceptors;

namespace ReportingPlatform.Bridge.Interceptors;

public sealed class JwtValidationInterceptor : Interceptor
{
    private readonly JwksCache      _jwks;
    private readonly string         _issuer;
    private readonly ILogger<JwtValidationInterceptor> _logger;

    private const string AudienceValue = "provider-bridge";
    private const string ScopeValue    = "provider";

    public JwtValidationInterceptor(JwksCache jwks, IConfiguration config, ILogger<JwtValidationInterceptor> logger)
    {
        _jwks   = jwks;
        _issuer = config["Auth:ProviderIssuer"] ?? "https://platform.reporting/";
        _logger = logger;
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        var claimsPrincipal = await ValidateAsync(context);
        if (claimsPrincipal is null)
        {
            throw new RpcException(new GrpcStatus(StatusCode.Unauthenticated, "UNAUTHENTICATED"));
        }
        context.UserState["claims"] = claimsPrincipal;
        await continuation(requestStream, responseStream, context);
    }

    private async Task<System.Security.Claims.ClaimsPrincipal?> ValidateAsync(ServerCallContext ctx)
    {
        var authHeader = ctx.RequestHeaders.GetValue("authorization");
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        var token = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token)) return null;

        string kid;
        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token)) return null;
            var parsed = handler.ReadJwtToken(token);
            kid = parsed.Header.Kid ?? string.Empty;
        }
        catch { return null; }

        if (string.IsNullOrEmpty(kid)) return null;

        var publicKey = await _jwks.GetKeyAsync(kid);
        if (publicKey is null)
        {
            _logger.LogWarning("JWT validation failed: unknown kid={Kid}", kid);
            return null;
        }

        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = _issuer,
            ValidateAudience         = true,
            ValidAudience            = AudienceValue,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = publicKey,
            ValidateLifetime         = true,
            ClockSkew                = TimeSpan.FromSeconds(30),
            RequireSignedTokens      = true,
        };

        try
        {
            var handler   = new JwtSecurityTokenHandler();
            // Disable legacy SAML claim remapping so "sub" stays as "sub" (not NameIdentifier).
            handler.InboundClaimTypeMap.Clear();
            var principal = handler.ValidateToken(token, validationParams, out _);

            var scope = principal.FindFirst("scope")?.Value ?? string.Empty;
            if (!scope.Split(' ').Contains(ScopeValue, StringComparer.Ordinal))
            {
                _logger.LogWarning("JWT validation failed: missing scope 'provider'");
                return null;
            }

            return principal;
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogDebug("JWT validation failed: token expired");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "JWT validation failed");
            return null;
        }
    }
}
