using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI.Files.Models;
using MareSynchronos.WebAPI.SignalR;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;

namespace MareSynchronos.WebAPI.Files;

public class FileTransferOrchestrator : DisposableMediatorSubscriberBase
{
    private readonly ConcurrentDictionary<Guid, bool> _downloadReady = new();
    private readonly HttpClient _httpClient;
    private readonly MareConfigService _mareConfig;
    private readonly object _semaphoreModificationLock = new();
    private readonly TokenProvider _tokenProvider;
    private int _availableDownloadSlots;
    private SemaphoreSlim _downloadSemaphore;

    public FileTransferOrchestrator(ILogger<FileTransferOrchestrator> logger, MareConfigService mareConfig,
        MareMediator mediator, TokenProvider tokenProvider) : base(logger, mediator)
    {
        _mareConfig = mareConfig;
        _tokenProvider = tokenProvider;
        _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(3000)
        };
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MareSynchronos", ver!.Major + "." + ver!.Minor + "." + ver!.Build));

        _availableDownloadSlots = mareConfig.Current.ParallelDownloads;
        _downloadSemaphore = new(_availableDownloadSlots);

        Mediator.Subscribe<ConnectedMessage>(this, (msg) =>
        {
            FilesCdnUri = msg.Connection.ServerInfo.FileServerAddress;
        });

        Mediator.Subscribe<DisconnectedMessage>(this, (msg) =>
        {
            FilesCdnUri = null;
        });
        Mediator.Subscribe<DownloadReadyMessage>(this, (msg) =>
        {
            _downloadReady[msg.RequestId] = true;
        });
    }

    public Uri? FilesCdnUri { private set; get; }
    public List<FileTransfer> ForbiddenTransfers { get; } = [];
    public bool IsInitialized => FilesCdnUri != null;

    public void ClearDownloadRequest(Guid guid)
    {
        _downloadReady.Remove(guid, out _);
    }

    public bool IsDownloadReady(Guid guid)
    {
        if (_downloadReady.TryGetValue(guid, out bool isReady) && isReady)
        {
            return true;
        }

        return false;
    }

    public void ReleaseDownloadSlot()
    {
        _downloadSemaphore.Release();
    }

    public async Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, Uri uri,
        CancellationToken? ct = null, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead)
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        return await SendRequestInternalAsync(requestMessage, ct, httpCompletionOption).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> SendRequestAsync<T>(HttpMethod method, Uri uri, T content, CancellationToken ct) where T : class
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        if (content is not ByteArrayContent)
            requestMessage.Content = JsonContent.Create(content);
        else
            requestMessage.Content = content as ByteArrayContent;
        return await SendRequestInternalAsync(requestMessage, ct).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> SendRequestStreamAsync(HttpMethod method, Uri uri, ProgressableStreamContent content, CancellationToken ct)
    {
        using var requestMessage = new HttpRequestMessage(method, uri);
        requestMessage.Content = content;
        return await SendRequestInternalAsync(requestMessage, ct).ConfigureAwait(false);
    }

    public async Task WaitForDownloadSlotAsync(CancellationToken token)
    {
        lock (_semaphoreModificationLock)
        {
            if (_availableDownloadSlots != _mareConfig.Current.ParallelDownloads && _availableDownloadSlots == _downloadSemaphore.CurrentCount)
            {
                _availableDownloadSlots = _mareConfig.Current.ParallelDownloads;
                _downloadSemaphore = new(_availableDownloadSlots);
            }
        }

        await _downloadSemaphore.WaitAsync(token).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendRequestInternalAsync(HttpRequestMessage requestMessage,
        CancellationToken? ct = null, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead)
    {
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _tokenProvider.GetOrUpdateToken(ct!.Value).ConfigureAwait(false));

        if (requestMessage.Content != null && requestMessage.Content is not StreamContent && requestMessage.Content is not ByteArrayContent)
        {
            var content = await ((JsonContent)requestMessage.Content).ReadAsStringAsync().ConfigureAwait(false);
            Logger.LogDebug("Sending {method} to {uri} (Content: {content})", requestMessage.Method, requestMessage.RequestUri, content);
        }
        else
        {
            Logger.LogDebug("Sending {method} to {uri}", requestMessage.Method, requestMessage.RequestUri);
        }

        try
        {
            if (ct != null)
                return await _httpClient.SendAsync(requestMessage, httpCompletionOption, ct.Value).ConfigureAwait(false);
            return await _httpClient.SendAsync(requestMessage, httpCompletionOption).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error during SendRequestInternal for {uri}", requestMessage.RequestUri);
            throw;
        }
    }
}