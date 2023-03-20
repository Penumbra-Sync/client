using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using ImGuiNET;
using ImGuiScene;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.User;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

public class EditProfileUi : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly FileDialogManager _fileDialogManager;
    private readonly MareProfileManager _mareProfileManager;
    private readonly UiBuilder _uiBuilder;
    private readonly UiSharedService _uiSharedService;
    private string _descriptionText = string.Empty;
    private bool _loadedPrior = false;
    private TextureWrap? _pfpTextureWrap;
    private bool _showFileDialogError = false;
    private bool _wasOpen;

    public EditProfileUi(ILogger<EditProfileUi> logger, MareMediator mediator,
        ApiController apiController, UiBuilder uiBuilder, UiSharedService uiSharedService,
        FileDialogManager fileDialogManager, MareProfileManager mareProfileManager) : base(logger, mediator, "Mare Synchronos Edit Profile###MareSynchronosEditProfileUI")
    {
        IsOpen = false;
        this.SizeConstraints = new()
        {
            MinimumSize = new(768, 512),
            MaximumSize = new(2000, 2000)
        };
        _apiController = apiController;
        _uiBuilder = uiBuilder;
        _uiSharedService = uiSharedService;
        _fileDialogManager = fileDialogManager;
        _mareProfileManager = mareProfileManager;

        Mediator.Subscribe<GposeStartMessage>(this, (_) => { _wasOpen = IsOpen; IsOpen = false; });
        Mediator.Subscribe<GposeEndMessage>(this, (_) => IsOpen = _wasOpen);
        Mediator.Subscribe<DisconnectedMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<ClearProfileDataMessage>(this, (msg) =>
        {
            if (msg.UserData != null || string.Equals(msg.UserData.UID, _apiController.UID, StringComparison.Ordinal))
            {
                _pfpTextureWrap?.Dispose();
                _pfpTextureWrap = null;
            }
        });
    }

    public override void Draw()
    {
        _uiSharedService.BigText("Current Profile");

        var (loaded, profile) = _mareProfileManager.GetMareProfile(new API.Data.UserData(_apiController.UID));

        if (!loaded)
        {
            _loadedPrior = false;
            _descriptionText = string.Empty;
            ImGui.TextUnformatted("Loading profile...");
            return;
        }
        else
        {
            if (profile.IsFlagged)
            {
                UiSharedService.ColorTextWrapped(profile.Description, ImGuiColors.DalamudRed);
                return;
            }

            if (!_loadedPrior)
            {
                _descriptionText = profile.Description;
                _loadedPrior = true;
                _pfpTextureWrap?.Dispose();
                _pfpTextureWrap = _uiBuilder.LoadImage(Convert.FromBase64String(profile.Base64ProfilePicture));
            }
        }

        if (_pfpTextureWrap != null)
        {
            ImGui.Image(_pfpTextureWrap.ImGuiHandle, new System.Numerics.Vector2(_pfpTextureWrap.Width, _pfpTextureWrap.Height));
        }

        ImGui.SameLine(256);
        var posX = ImGui.GetCursorPosX();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var width = UiSharedService.GetWindowContentRegionWidth() - posX + spacing;
        if (ImGui.BeginChildFrame(100, new System.Numerics.Vector2(width, 256)))
        {
            var nsfw = profile.IsNSFW;
            ImGui.BeginDisabled();
            ImGui.Checkbox("Is NSFW", ref nsfw);
            ImGui.EndDisabled();
            UiSharedService.TextWrapped("Description:" + Environment.NewLine + profile.Description);
        }
        ImGui.EndChildFrame();

        ImGui.Separator();
        _uiSharedService.BigText("Notes and Rules for Profiles");

        ImGui.TextWrapped($"- All users that are paired and unpaused with you will be able to see your profile picture and description.{Environment.NewLine}" +
            $"- Other users have the possibility to report your profile for breaking the rules.{Environment.NewLine}" +
            $"- !!! AVOID: anything as profile image that can be considered highly illegal or obscene (bestiality, anything that could be considered a sexual act with a minor (that includes Lalafells), etc.){Environment.NewLine}" +
            $"- !!! AVOID: slurs of any kind in the description that can be considered highly offensive{Environment.NewLine}" +
            $"- In case of valid reports from other users this can lead to disabling your profile forever or terminating your Mare account indefinitely.{Environment.NewLine}" +
            $"- Judgement of your profile validity from reports through staff is not up to debate and the decisions to disable your profile/account permanent.{Environment.NewLine}" +
            $"- If your profile picture or profile description could be considered NSFW, enable the toggle below.");
        ImGui.Separator();
        _uiSharedService.BigText("Profile Settings");

        if (UiSharedService.IconTextButton(FontAwesomeIcon.FileUpload, "Upload new profile image"))
        {
            _fileDialogManager.OpenFileDialog("Select new Profile picture", ".png", (success, file) =>
            {
                if (!success) return;
                Task.Run(async () =>
                {
                    var fileContent = File.ReadAllBytes(file);
                    using MemoryStream ms = new(fileContent);
                    var format = await Image.DetectFormatAsync(ms).ConfigureAwait(false);
                    if (!format.FileExtensions.Contains("png", StringComparer.OrdinalIgnoreCase))
                    {
                        _showFileDialogError = true;
                        return;
                    }
                    using var image = Image.Load<Rgba32>(fileContent);

                    if (image.Width > 256 || image.Height > 256 || (fileContent.Length > 250 * 1024))
                    {
                        _showFileDialogError = true;
                        return;
                    }

                    _showFileDialogError = false;
                    await _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), false, null, Convert.ToBase64String(fileContent), null))
                        .ConfigureAwait(false);
                });
            });
        }
        UiSharedService.AttachToolTip("Select and upload a new profile picture");
        ImGui.SameLine();
        if (UiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Clear uploaded profile picture"))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), false, null, "", null));
        }
        UiSharedService.AttachToolTip("Clear your currently uploaded profile picture");
        if (_showFileDialogError)
        {
            UiSharedService.ColorTextWrapped("The profile picture must be a PNG file with a maximum height and width of 256px and 250KiB size", ImGuiColors.DalamudRed);
        }
        var isNsfw = profile.IsNSFW;
        if (ImGui.Checkbox("Profile is NSFW", ref isNsfw))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), false, isNsfw, null, null));
        }
        UiSharedService.DrawHelpText("If your profile description or image can be considered NSFW, toggle this to ON");
        var widthTextBox = UiSharedService.GetWindowContentRegionWidth() - posX + spacing;
        ImGui.TextUnformatted($"Description {_descriptionText.Length}/750");
        ImGui.InputTextMultiline("##description", ref _descriptionText, 750, new System.Numerics.Vector2(widthTextBox, 200));
        if (UiSharedService.IconTextButton(FontAwesomeIcon.Save, "Save Description"))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), false, null, null, _descriptionText));
        }
        UiSharedService.AttachToolTip("Sets your profile description text");
        ImGui.SameLine();
        if (UiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Clear Description"))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), false, null, null, ""));
        }
        UiSharedService.AttachToolTip("Clears your profile description text");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _pfpTextureWrap?.Dispose();
    }
}