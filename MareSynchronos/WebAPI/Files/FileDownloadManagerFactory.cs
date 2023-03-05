using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.WebAPI.Files;

public class FileDownloadManagerFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly MareMediator _mediator;
    private readonly FileTransferOrchestrator _orchestrator;
    private readonly FileCacheManager _fileCacheManager;

    public FileDownloadManagerFactory(ILoggerFactory loggerFactory, MareMediator mediator, FileTransferOrchestrator orchestrator, FileCacheManager fileCacheManager)
    {
        _loggerFactory = loggerFactory;
        _mediator = mediator;
        _orchestrator = orchestrator;
        _fileCacheManager = fileCacheManager;
    }

    public FileDownloadManager Create(string id)
    {
        return new FileDownloadManager(_loggerFactory.CreateLogger<FileDownloadManager>(), _mediator, _orchestrator, _fileCacheManager, id);
    }
}
