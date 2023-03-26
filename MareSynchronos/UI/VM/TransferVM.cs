using System.Collections.Concurrent;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI.Files;
using MareSynchronos.WebAPI.Files.Models;

namespace MareSynchronos.UI.VM;

public sealed class TransferVM : ImguiVM, IMediatorSubscriber, IDisposable
{
    private readonly FileUploadManager _fileUploadManager;

    public TransferVM(MareMediator mediator, FileUploadManager fileUploadManager)
    {
        Mediator = mediator;
        _fileUploadManager = fileUploadManager;
        Mediator.Subscribe<DownloadStartedMessage>(this, (msg) => CurrentDownloads[msg.DownloadId] = msg.DownloadStatus);
        Mediator.Subscribe<DownloadFinishedMessage>(this, (msg) => CurrentDownloads.TryRemove(msg.DownloadId, out _));
    }

    public ConcurrentDictionary<GameObjectHandler, Dictionary<string, FileDownloadStatus>> CurrentDownloads { get; private set; } = new();
    public List<FileTransfer> CurrentUploads => _fileUploadManager.CurrentUploads;
    public MareMediator Mediator { get; }

    public void Dispose()
    {
        Mediator.UnsubscribeAll(this);
    }
}