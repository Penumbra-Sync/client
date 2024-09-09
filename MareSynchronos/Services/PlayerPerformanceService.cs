using MareSynchronos.API.Data;
using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services.Events;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI;
using MareSynchronos.WebAPI.Files.Models;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

public class PlayerPerformanceService
{
    private readonly FileCacheManager _fileCacheManager;
    private readonly XivDataAnalyzer _xivDataAnalyzer;
    private readonly ILogger<PlayerPerformanceService> _logger;
    private readonly MareMediator _mediator;
    private readonly PlayerPerformanceConfigService _playerPerformanceConfigService;

    public PlayerPerformanceService(ILogger<PlayerPerformanceService> logger, MareMediator mediator,
        PlayerPerformanceConfigService playerPerformanceConfigService, FileCacheManager fileCacheManager,
        XivDataAnalyzer xivDataAnalyzer)
    {
        _logger = logger;
        _mediator = mediator;
        _playerPerformanceConfigService = playerPerformanceConfigService;
        _fileCacheManager = fileCacheManager;
        _xivDataAnalyzer = xivDataAnalyzer;
    }

    public async Task<bool> CheckTriangleUsageThresholds(PairHandler pairHandler, CharacterData charaData)
    {
        var config = _playerPerformanceConfigService.Current;
        var pair = pairHandler.Pair;

        long triUsage = 0;

        if (!charaData.FileReplacements.TryGetValue(API.Data.Enum.ObjectKind.Player, out List<FileReplacementData>? playerReplacements))
        {
            pair.LastAppliedDataTris = 0;
            return true;
        }

        var moddedModelHashes = playerReplacements.Where(p => string.IsNullOrEmpty(p.FileSwapPath) && p.GamePaths.Any(g => g.EndsWith("mdl", StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Hash)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var hash in moddedModelHashes)
        {
            triUsage += await _xivDataAnalyzer.GetTrianglesByHash(hash).ConfigureAwait(false);
        }

        pair.LastAppliedDataTris = triUsage;

        _logger.LogDebug("Calculated VRAM usage for {p}", pairHandler);

        // no warning of any kind on ignored pairs
        if (config.UIDsToIgnore
            .Exists(uid => string.Equals(uid, pair.UserData.Alias, StringComparison.Ordinal) || string.Equals(uid, pair.UserData.UID, StringComparison.Ordinal)))
            return true;

        bool isPrefPerm = pair.UserPair.OwnPermissions.HasFlag(API.Data.Enum.UserPermissions.Sticky);

        // now check auto pause
        if (CheckForThreshold(config.AutoPausePlayersExceedingThresholds, config.TrisAutoPauseThresholdThousands * 1000,
            triUsage, config.AutoPausePlayersWithPreferredPermissionsExceedingThresholds, isPrefPerm))
        {
            _mediator.Publish(new NotificationMessage($"{pair.PlayerName} ({pair.UserData.AliasOrUID}) automatically paused",
                $"Player {pair.PlayerName} exceeded your configured triangle auto pause threshold (" +
                $"{triUsage}/{config.TrisAutoPauseThresholdThousands * 1000} triangles)" +
                $" and has been automatically paused.",
                MareConfiguration.Models.NotificationType.Warning));

            _mediator.Publish(new EventMessage(new Event(pair.PlayerName, pair.UserData, nameof(PlayerPerformanceService), EventSeverity.Warning,
                $"Exceeds triangle threshold: automatically paused ({triUsage}/{triUsage * 1000} triangles)")));

            _mediator.Publish(new PauseMessage(pair.UserData));

            return false;
        }

        // and fucking warnings
        if (CheckForThreshold(config.WarnOnExceedingThresholds, config.TrisWarningThresholdThousands * 1000,
            triUsage, config.WarnOnPreferredPermissionsExceedingThresholds, isPrefPerm))
        {
            _mediator.Publish(new NotificationMessage($"{pair.PlayerName} ({pair.UserData.AliasOrUID}) exceeds performance threshold",
                $"Player {pair.PlayerName} exceeds your configured triangle warning threshold (" +
                $"{triUsage}/{triUsage * 1000} triangles).",
                MareConfiguration.Models.NotificationType.Warning));

            _mediator.Publish(new EventMessage(new Event(pair.PlayerName, pair.UserData, nameof(PlayerPerformanceService), EventSeverity.Warning,
                $"Exceeds triangle threshold: ({triUsage}/{triUsage * 1000} triangles)")));
        }

        return true;
    }

    public bool CheckVRAMUsageThresholds(PairHandler pairHandler, CharacterData charaData, List<DownloadFileTransfer> toDownloadFiles)
    {
        var config = _playerPerformanceConfigService.Current;
        var pair = pairHandler.Pair;

        long vramUsage = 0;

        if (!charaData.FileReplacements.TryGetValue(API.Data.Enum.ObjectKind.Player, out List<FileReplacementData>? playerReplacements))
        {
            pair.LastAppliedApproximateVRAMBytes = 0;
            return true;
        }

        var moddedTextureHashes = playerReplacements.Where(p => string.IsNullOrEmpty(p.FileSwapPath) && p.GamePaths.Any(g => g.EndsWith(".tex", StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Hash)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var hash in moddedTextureHashes)
        {
            long fileSize = 0;

            var download = toDownloadFiles.Find(f => string.Equals(hash, f.Hash, StringComparison.OrdinalIgnoreCase));
            if (download != null)
            {
                fileSize = download.TotalRaw;
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

        pair.LastAppliedApproximateVRAMBytes = vramUsage;

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

            _mediator.Publish(new PauseMessage(pair.UserData));

            _mediator.Publish(new EventMessage(new Event(pair.PlayerName, pair.UserData, nameof(PlayerPerformanceService), EventSeverity.Warning,
                $"Exceeds VRAM threshold: automatically paused ({UiSharedService.ByteToString(vramUsage, addSuffix: true)}/{config.VRAMSizeAutoPauseThresholdMiB}MiB)")));

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

            _mediator.Publish(new EventMessage(new Event(pair.PlayerName, pair.UserData, nameof(PlayerPerformanceService), EventSeverity.Warning,
                $"Exceeds triangle threshold: ({UiSharedService.ByteToString(vramUsage, addSuffix: true)}/{config.VRAMSizeAutoPauseThresholdMiB}MiB)")));
        }

        return true;
    }

    private static bool CheckForThreshold(bool thresholdEnabled, long threshold, long value, bool checkForPrefPerm, bool isPrefPerm) =>
        thresholdEnabled && threshold > 0 && threshold < value && ((checkForPrefPerm && isPrefPerm) || !isPrefPerm);
}