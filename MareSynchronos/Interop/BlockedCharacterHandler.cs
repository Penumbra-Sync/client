using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop;

public unsafe class BlockedCharacterHandler
{
    private sealed record CharaData(ulong AccId, ulong ContentId);
    private readonly Dictionary<CharaData, bool> _blockedCharacterCache = new();

    private readonly ILogger<BlockedCharacterHandler> _logger;

    public BlockedCharacterHandler(ILogger<BlockedCharacterHandler> logger, IGameInteropProvider gameInteropProvider)
    {
        gameInteropProvider.InitializeFromAttributes(this);
        _logger = logger;
    }

    private static CharaData GetIdsFromPlayerPointer(nint ptr)
    {
        if (ptr == nint.Zero) return new(0, 0);
        var castChar = ((BattleChara*)ptr);
        return new(castChar->Character.AccountId, castChar->Character.ContentId);
    }

    public bool IsCharacterBlocked(nint ptr, out bool firstTime)
    {
        firstTime = false;
        var combined = GetIdsFromPlayerPointer(ptr);
        if (_blockedCharacterCache.TryGetValue(combined, out var isBlocked))
            return isBlocked;

        firstTime = true;
        var blockStatus = InfoProxyBlacklist.Instance()->GetBlockResultType(combined.AccId, combined.ContentId);
        _logger.LogTrace("CharaPtr {ptr} is BlockStatus: {status}", ptr, blockStatus);
        if ((int)blockStatus == 0)
            return false;
        return _blockedCharacterCache[combined] = blockStatus != InfoProxyBlacklist.BlockResultType.NotBlocked;
    }
}
