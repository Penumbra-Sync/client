using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

public class PlayerPerformanceService : IHostedService
{
    private readonly ILogger<PlayerPerformanceService> _logger;
    private readonly MareMediator _mediator;
    private readonly PlayerPerformanceConfigService _playerPerformanceConfigService;
    private readonly FileCacheManager _fileCacheManager;
    private readonly PairManager _pairManager;
    private readonly ApiController _apiController;

    public PlayerPerformanceService(ILogger<PlayerPerformanceService> logger, MareMediator mediator,
        PlayerPerformanceConfigService playerPerformanceConfigService, FileCacheManager fileCacheManager,
        PairManager pairManager, ApiController apiController)
    {
        _logger = logger;
        _mediator = mediator;
        _playerPerformanceConfigService = playerPerformanceConfigService;
        _fileCacheManager = fileCacheManager;
        _pairManager = pairManager;
        _apiController = apiController;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public bool TryCalculateVRAMUsage(PairHandler pairHandler, CharacterData charaData, List<DownloadFileTransfer> toDownloadFiles, out long vramUsage)
    {
        var config = _playerPerformanceConfigService.Current;
        var pair = _pairManager.GetOnlineUserPairs().First(p => string.Equals(p.UserData.UID, pairHandler.OnlineUser.User.UID, StringComparison.OrdinalIgnoreCase));

        vramUsage = 0;

        if (!charaData.FileReplacements.TryGetValue(API.Data.Enum.ObjectKind.Player, out List<FileReplacementData>? playerReplacements))
            return true;

        var moddedTextureHashes = playerReplacements.Where(p => string.IsNullOrEmpty(p.FileSwapPath) && p.GamePaths.Any(g => g.EndsWith("tex", StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Hash)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var hash in moddedTextureHashes.ToList())
        {
            long fileSize = 0;

            var download = toDownloadFiles.Find(f => string.Equals(hash, f.Hash, StringComparison.OrdinalIgnoreCase));
            if (download != null)
            {
                fileSize = download.Total;
            }
            else
            {
                var fileEntry = _fileCacheManager.GetFileCacheByHash(hash);
                if (fileEntry == null) continue;

                if (fileEntry.Size == null)
                {
                    fileEntry.Size = new FileInfo(fileEntry.ResolvedFilepath).Length;
                    _fileCacheManager.UpdateHashedFile(fileEntry, computeProperties: true);
                }

                fileSize = fileEntry.Size.Value;
            }

            vramUsage += fileSize;
        }

        _logger.LogDebug("Calculated VRAM usage for {p}", pairHandler);

        // no warning of any kind on ignored pairs
        if (config.UIDsToIgnore
            .Exists(uid => string.Equals(uid, pair.UserData.Alias, StringComparison.Ordinal) || string.Equals(uid, pair.UserData.UID, StringComparison.Ordinal)))
            return true;

        bool isPrefPerm = pair.UserPair.OwnPermissions.HasFlag(API.Data.Enum.UserPermissions.Sticky);

        // now check auto pause
        if (CheckForThreshold(config.AutoPausePlayersExceedingThresholds, config.VRAMSizeAutoPauseThresholdMiB * 1024 * 1024,
            vramUsage, config.AutoPausePlayersWithPreferredPermissionsExceedingThresholds, isPrefPerm))
        {
            _mediator.Publish(new NotificationMessage($"{pair.PlayerName} ({pair.UserData.AliasOrUID}) automatically paused",
                $"Player {pair.PlayerName} exceeded your configured VRAM auto pause threshold (" +
                $"{UiSharedService.ByteToString(vramUsage, addSuffix: true)}/{config.VRAMSizeAutoPauseThresholdMiB}MiB)" +
                $" and has been automatically paused.",
                MareConfiguration.Models.NotificationType.Warning));

            // pause
            var perm = pair.UserPair.OwnPermissions.DeepClone();
            perm.SetPaused(paused: true);
            _ = _apiController.SetBulkPermissions(new(
                new(StringComparer.Ordinal) { { pair.UserData.UID, perm } },
                new(StringComparer.Ordinal)));

            return false;
        }

        // and fucking warnings
        if (CheckForThreshold(config.WarnOnExceedingThresholds, config.VRAMSizeWarningThresholdMiB * 1024 * 1024,
            vramUsage, config.WarnOnPreferredPermissionsExceedingThresholds, isPrefPerm))
        {
            _mediator.Publish(new NotificationMessage($"{pair.PlayerName} ({pair.UserData.AliasOrUID}) exceeds performance threshold",
                $"Player {pair.PlayerName} exceeds your configured VRAM warning threshold (" +
                $"{UiSharedService.ByteToString(vramUsage, true)}/{config.VRAMSizeWarningThresholdMiB}MiB).",
                MareConfiguration.Models.NotificationType.Warning));
            return true;
        }

        return true;
    }

    private static bool CheckForThreshold(bool thresholdEnabled, long threshold, long value, bool checkForPrefPerm, bool isPrefPerm) =>
        thresholdEnabled && threshold > 0 && threshold < value && ((checkForPrefPerm && isPrefPerm) || !isPrefPerm);
}