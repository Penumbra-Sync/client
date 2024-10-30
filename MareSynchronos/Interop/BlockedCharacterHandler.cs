using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop;

public unsafe class BlockedCharacterHandler
{
    private sealed record CharaData(ulong AccId, ulong ContentId);
    private readonly Dictionary<CharaData, bool> _blockedCharacterCache = new();

    private enum BlockResultType
    {
        NotBlocked = 1,
        BlockedByAccountId = 2,
        BlockedByContentId = 3,
    }

    [Signature("48 83 EC 48 F6 81 ?? ?? ?? ?? ?? 75 ?? 33 C0 48 83 C4 48")]
    private readonly GetBlockResultTypeDelegate? _getBlockResultType = null;
    private readonly ILogger<BlockedCharacterHandler> _logger;

    private unsafe delegate BlockResultType GetBlockResultTypeDelegate(InfoProxyBlacklist* thisPtr, ulong accountId, ulong contentId);

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

        if (_getBlockResultType == null)
            return _blockedCharacterCache[combined] = false;

        firstTime = true;
        var infoProxy = InfoProxyBlacklist.Instance();
        var blockStatus = _getBlockResultType(infoProxy, combined.AccId, combined.ContentId);
        _logger.LogTrace("CharaPtr {ptr} is BlockStatus: {status}", ptr, blockStatus);
        return _blockedCharacterCache[combined] = blockStatus != BlockResultType.NotBlocked;
    }
}
