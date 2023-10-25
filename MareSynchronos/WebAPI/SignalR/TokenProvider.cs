using MareSynchronos.API.Routes;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Reflection;

namespace MareSynchronos.WebAPI.SignalR;

public sealed class TokenProvider : IDisposable, IMediatorSubscriber
{
    private readonly DalamudUtilService _dalamudUtil;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TokenProvider> _logger;
    private readonly ServerConfigurationManager _serverManager;
    private readonly ConcurrentDictionary<JwtIdentifier, string> _tokenCache = new();

    public TokenProvider(ILogger<TokenProvider> logger, ServerConfigurationManager serverManager, DalamudUtilService dalamudUtil, MareMediator mareMediator)
    {
        _logger = logger;
        _serverManager = serverManager;
        _dalamudUtil = dalamudUtil;
        _httpClient = new();
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        Mediator = mareMediator;
        Mediator.Subscribe<DalamudLogoutMessage>(this, (_) => _tokenCache.Clear());
        Mediator.Subscribe<DalamudLoginMessage>(this, (_) => _tokenCache.Clear());
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MareSynchronos", ver!.Major + "." + ver!.Minor + "." + ver!.Build));
    }

    public MareMediator Mediator { get; }

    private JwtIdentifier CurrentIdentifier => new(_serverManager.CurrentApiUrl, _serverManager.GetSecretKey()!);

    public void Dispose()
    {
        Mediator.UnsubscribeAll(this);
        _httpClient.Dispose();
    }

    public async Task<string> GetNewToken(bool isRenewal, CancellationToken token)
    {
        Uri tokenUri;
        string response = string.Empty;
        HttpResponseMessage result;

        try
        {
            if (!isRenewal)
            {
                _logger.LogDebug("GetNewToken: Requesting");

                tokenUri = MareAuth.AuthFullPath(new Uri(_serverManager.CurrentApiUrl
                    .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
                    .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)));
                var secretKey = _serverManager.GetSecretKey()!;
                var auth = secretKey.GetHash256();
                result = await _httpClient.PostAsync(tokenUri, new FormUrlEncodedContent(new[]
                {
                            new KeyValuePair<string, string>("auth", auth),
                            new KeyValuePair<string, string>("charaIdent", await _dalamudUtil.GetPlayerNameHashedAsync().ConfigureAwait(false)),
                }), token).ConfigureAwait(false);
            }
            else
            {
                _logger.LogDebug("GetNewToken: Renewal");

                tokenUri = MareAuth.RenewTokenFullPath(new Uri(_serverManager.CurrentApiUrl
                    .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
                    .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)));
                HttpRequestMessage request = new(HttpMethod.Get, tokenUri.ToString());
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenCache[CurrentIdentifier]);
                result = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
            }

            response = await result.Content.ReadAsStringAsync().ConfigureAwait(false);
            result.EnsureSuccessStatusCode();
            _tokenCache[CurrentIdentifier] = response;
        }
        catch (HttpRequestException ex)
        {
            _tokenCache.TryRemove(CurrentIdentifier, out _);

            _logger.LogError(ex, "GetNewToken: Failure to get token");

            if (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Mediator.Publish(new NotificationMessage("Error refreshing token", "Your authentication token could not be renewed. Try reconnecting to Mare manually.",
                    Dalamud.Interface.Internal.Notifications.NotificationType.Error));
                Mediator.Publish(new DisconnectedMessage());
                throw new MareAuthFailureException(response);
            }

            throw;
        }

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(response);
        _logger.LogTrace("GetNewToken: JWT {token}", response);
        _logger.LogDebug("GetNewToken: Valid until {date}, ValidClaim until {date}", jwtToken.ValidTo,
                new DateTime(long.Parse(jwtToken.Claims.Single(c => string.Equals(c.Type, "expiration_date", StringComparison.Ordinal)).Value), DateTimeKind.Utc));
        return response;
    }

    public string? GetToken()
    {
        if (_tokenCache.TryGetValue(CurrentIdentifier, out var token))
        {
            return token;
        }

        throw new InvalidOperationException("No token present");
    }

    public async Task<string?> GetOrUpdateToken(CancellationToken ct)
    {
        bool renewal = false;
        if (_tokenCache.TryGetValue(CurrentIdentifier, out var token))
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);
            if (jwtToken.ValidTo == DateTime.MinValue || jwtToken.ValidTo.Subtract(TimeSpan.FromMinutes(5)) > DateTime.UtcNow)
            {
                _logger.LogTrace("GetOrUpdate: Returning token from cache");
                return token;
            }

            renewal = true;
        }

        _logger.LogTrace("GetOrUpdate: Getting new token");
        return await GetNewToken(renewal, ct).ConfigureAwait(false);
    }
}