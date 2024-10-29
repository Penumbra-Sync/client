using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop;

public unsafe class BlockedCharacterHandler
{
    private readonly Dictionary<(ulong, ulong), bool> _blockedCharacterCache = new();

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

    private static ulong GetAccIdFromPlayerPointer(nint ptr)
    {
        if (ptr == nint.Zero) return 0;
        return ((BattleChara*)ptr)->Character.AccountId;
    }

    private static ulong GetContentIdFromPlayerPointer(nint ptr)
    {
        if (ptr == nint.Zero) return 0;
        return ((BattleChara*)ptr)->Character.ContentId;
    }

    public bool IsCharacterBlocked(nint ptr)
    {
        (ulong AccId, ulong ContentId) combined = (GetAccIdFromPlayerPointer(ptr), GetContentIdFromPlayerPointer(ptr));
        if (_blockedCharacterCache.TryGetValue(combined, out var isBlocked))
            return isBlocked;

        if (_getBlockResultType == null)
            return _blockedCharacterCache[combined] = false;

        var infoProxy = InfoProxyBlacklist.Instance();
        var blockStatus = _getBlockResultType(infoProxy, combined.AccId, combined.ContentId);
        _logger.LogTrace("CharaPtr {ptr} is BlockStatus: {status}", ptr, blockStatus);
        return _blockedCharacterCache[combined] = blockStatus != BlockResultType.NotBlocked;
    }
}
