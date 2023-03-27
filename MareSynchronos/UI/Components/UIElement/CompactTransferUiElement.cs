using Dalamud.Interface;
using ImGuiNET;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.VM;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI.Components.UIElement;

public class CompactTransferUiElement : WindowElementVMBase<ImguiVM>
{
    private readonly TransferVM _transferVM;

    public CompactTransferUiElement(TransferVM transferVM, ILogger<CompactTransferUiElement> logger, MareMediator mediator) : base(transferVM, logger, mediator)
    {
        _transferVM = VM.As<TransferVM>();
    }

    public void Draw(float contentWidth)
    {
        var currentUploads = _transferVM.CurrentUploads.ToList();
        UiSharedService.Icon(FontAwesomeIcon.Upload);
        ImGui.SameLine(35 * ImGuiHelpers.GlobalScale);

        if (currentUploads.Any())
        {
            var totalUploads = currentUploads.Count;

            var doneUploads = currentUploads.Count(c => c.IsTransferred);
            var totalUploaded = currentUploads.Sum(c => c.Transferred);
            var totalToUpload = currentUploads.Sum(c => c.Total);

            ImGui.Text($"{doneUploads}/{totalUploads}");
            var uploadText = $"({UiSharedService.ByteToString(totalUploaded)}/{UiSharedService.ByteToString(totalToUpload)})";
            var textSize = ImGui.CalcTextSize(uploadText);
            ImGui.SameLine(contentWidth - textSize.X);
            ImGui.Text(uploadText);
        }
        else
        {
            ImGui.Text("No uploads in progress");
        }

        var currentDownloads = _transferVM.CurrentDownloads.SelectMany(d => d.Value.Values).ToList();
        UiSharedService.Icon(FontAwesomeIcon.Download);
        ImGui.SameLine(35 * ImGuiHelpers.GlobalScale);

        if (currentDownloads.Any())
        {
            var totalDownloads = currentDownloads.Sum(c => c.TotalFiles);
            var doneDownloads = currentDownloads.Sum(c => c.TransferredFiles);
            var totalDownloaded = currentDownloads.Sum(c => c.TransferredBytes);
            var totalToDownload = currentDownloads.Sum(c => c.TotalBytes);

            ImGui.Text($"{doneDownloads}/{totalDownloads}");
            var downloadText =
                $"({UiSharedService.ByteToString(totalDownloaded)}/{UiSharedService.ByteToString(totalToDownload)})";
            var textSize = ImGui.CalcTextSize(downloadText);
            ImGui.SameLine(contentWidth - textSize.X);
            ImGui.Text(downloadText);
        }
        else
        {
            ImGui.Text("No downloads in progress");
        }
        ImGui.SameLine();
    }
}