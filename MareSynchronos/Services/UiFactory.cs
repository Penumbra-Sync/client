using MareSynchronos.API.Dto.Group;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI;
using MareSynchronos.UI.Components.Popup;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

public class UiFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly MareMediator _mareMediator;
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiSharedService;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverConfigManager;
    private readonly MareProfileManager _mareProfileManager;
    private readonly PerformanceCollectorService _performanceCollectorService;

    public UiFactory(ILoggerFactory loggerFactory, MareMediator mareMediator, ApiController apiController,
        UiSharedService uiSharedService, PairManager pairManager, ServerConfigurationManager serverConfigManager,
        MareProfileManager mareProfileManager, PerformanceCollectorService performanceCollectorService)
    {
        _loggerFactory = loggerFactory;
        _mareMediator = mareMediator;
        _apiController = apiController;
        _uiSharedService = uiSharedService;
        _pairManager = pairManager;
        _serverConfigManager = serverConfigManager;
        _mareProfileManager = mareProfileManager;
        _performanceCollectorService = performanceCollectorService;
    }

    public SyncshellAdminUI CreateSyncshellAdminUi(GroupFullInfoDto dto)
    {
        return new SyncshellAdminUI(_loggerFactory.CreateLogger<SyncshellAdminUI>(), _mareMediator,
            _apiController, _uiSharedService, _pairManager, dto, _performanceCollectorService);
    }

    public StandaloneProfileUi CreateStandaloneProfileUi(Pair pair)
    {
        return new StandaloneProfileUi(_loggerFactory.CreateLogger<StandaloneProfileUi>(), _mareMediator,
            _uiSharedService, _serverConfigManager, _mareProfileManager, _pairManager, pair, _performanceCollectorService);
    }

    public PermissionWindowUI CreatePermissionPopupUi(Pair pair)
    {
        return new PermissionWindowUI(_loggerFactory.CreateLogger<PermissionWindowUI>(), pair,
            _mareMediator, _uiSharedService, _apiController, _performanceCollectorService);
    }
}
