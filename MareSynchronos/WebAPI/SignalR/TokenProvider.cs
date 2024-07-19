using MareSynchronos.API.Routes;
using MareSynchronos.MareConfiguration.Models;
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
        Mediator.Subscribe<DalamudLogoutMessage>(this, (_) =>
        {
            _lastJwtIdentifier = null;
            _tokenCache.Clear();
        });
        Mediator.Subscribe<DalamudLoginMessage>(this, (_) =>
        {
            _lastJwtIdentifier = null;
            _tokenCache.Clear();
        });
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MareSynchronos", ver!.Major + "." + ver!.Minor + "." + ver!.Build));
    }

    public MareMediator Mediator { get; }

    private JwtIdentifier? _lastJwtIdentifier;

    public void Dispose()
    {
        Mediator.UnsubscribeAll(this);
        _httpClient.Dispose();
    }

    public async Task<string> GetNewToken(bool isRenewal, JwtIdentifier identifier, CancellationToken token)
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
                var secretKey = _serverManager.GetSecretKey(out _)!;
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
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenCache[identifier]);
                result = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
            }

            response = await result.Content.ReadAsStringAsync().ConfigureAwait(false);
            result.EnsureSuccessStatusCode();
            _tokenCache[identifier] = response;
        }
        catch (HttpRequestException ex)
        {
            _tokenCache.TryRemove(identifier, out _);

            _logger.LogError(ex, "GetNewToken: Failure to get token");

            if (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                if (isRenewal)
                    Mediator.Publish(new NotificationMessage("Error refreshing token", "Your authentication token could not be renewed. Try reconnecting to Mare manually.",
                    NotificationType.Error));
                else
                    Mediator.Publish(new NotificationMessage("Error generating token", "Your authentication token could not be generated. Check Mares main UI to see the error message.",
                    NotificationType.Error));
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
        var dateTimeMinus10 = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10));
        var dateTimePlus10 = DateTime.UtcNow.Add(TimeSpan.FromMinutes(10));
        var tokenTime = jwtToken.ValidTo.Subtract(TimeSpan.FromHours(6));
        if (tokenTime <= dateTimeMinus10 || tokenTime >= dateTimePlus10)
        {
            _tokenCache.TryRemove(identifier, out _);
            Mediator.Publish(new NotificationMessage("Invalid system clock", "The clock of your computer is invalid. " +
                "Mare will not function properly if the time zone is not set correctly. " +
                "Please set your computers time zone correctly and keep your clock synchronized with the internet.",
                NotificationType.Error));
            throw new InvalidOperationException($"JwtToken is behind DateTime.UtcNow, DateTime.UtcNow is possibly wrong. DateTime.UtcNow is {DateTime.UtcNow}, JwtToken.ValidTo is {jwtToken.ValidTo}");
        }
        return response;
    }

    private JwtIdentifier? GetIdentifier()
    {
        JwtIdentifier jwtIdentifier;
        try
        {
            jwtIdentifier = new(_serverManager.CurrentApiUrl,
                                _dalamudUtil.GetPlayerNameHashedAsync().GetAwaiter().GetResult(),
                                _serverManager.GetSecretKey(out _)!);
            _lastJwtIdentifier = jwtIdentifier;
        }
        catch (Exception ex)
        {
            if (_lastJwtIdentifier == null)
            {
                _logger.LogError("GetOrUpdate: No last identifier found, aborting");
                return null;
            }

            _logger.LogWarning(ex, "GetOrUpdate: Could not get JwtIdentifier for some reason or another, reusing last identifier {identifier}", _lastJwtIdentifier);
            jwtIdentifier = _lastJwtIdentifier;
        }

        _logger.LogDebug("GetOrUpdate: Using identifier {identifier}", jwtIdentifier);
        return jwtIdentifier;
    }

    public string? GetToken()
    {
        JwtIdentifier? jwtIdentifier = GetIdentifier();
        if (jwtIdentifier == null) return null;

        if (_tokenCache.TryGetValue(jwtIdentifier, out var token))
        {
            return token;
        }

        throw new InvalidOperationException("No token present");
    }

    public async Task<string?> GetOrUpdateToken(CancellationToken ct)
    {
        JwtIdentifier? jwtIdentifier = GetIdentifier();
        if (jwtIdentifier == null) return null;

        bool renewal = false;
        if (_tokenCache.TryGetValue(jwtIdentifier, out var token))
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            if (jwt.ValidTo == DateTime.MinValue || jwt.ValidTo.Subtract(TimeSpan.FromMinutes(5)) > DateTime.UtcNow)
            {
                _logger.LogTrace("GetOrUpdate: Returning token from cache");
                return token;
            }

            _logger.LogDebug("GetOrUpdate: Cached token requires renewal, token valid to: {valid}, UtcTime is {utcTime}", jwt.ValidTo, DateTime.UtcNow);
            renewal = true;
        }
        else
        {
            _logger.LogDebug("GetOrUpdate: Did not find token in cache, requesting a new one");
        }

        _logger.LogTrace("GetOrUpdate: Getting new token");
        return await GetNewToken(renewal, jwtIdentifier, ct).ConfigureAwait(false);
    }
}