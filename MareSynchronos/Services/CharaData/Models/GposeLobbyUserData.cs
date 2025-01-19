using Dalamud.Utility;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.Utils;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace MareSynchronos.Services.CharaData.Models;

public sealed record GposeLobbyUserData(UserData UserData)
{
    public void Reset()
    {
        HasWorldDataUpdate = WorldData != null;
        HasPoseDataUpdate = ApplicablePoseData != null;
        SpawnedVfxId = null;
        LastAppliedCharaDataDate = DateTime.MinValue;
    }

    private WorldData? _worldData;
    public WorldData? WorldData
    {
        get => _worldData; set
        {
            _worldData = value;
            HasWorldDataUpdate = true;
        }
    }

    public bool HasWorldDataUpdate { get; set; } = false;

    private PoseData? _fullPoseData;
    private PoseData? _deltaPoseData;

    public PoseData? FullPoseData
    {
        get => _fullPoseData;
        set
        {
            _fullPoseData = value;
            ApplicablePoseData = CombinePoseData();
            HasPoseDataUpdate = true;
        }
    }

    public PoseData? DeltaPoseData
    {
        get => _deltaPoseData;
        set
        {
            _deltaPoseData = value;
            ApplicablePoseData = CombinePoseData();
            HasPoseDataUpdate = true;
        }
    }

    public PoseData? ApplicablePoseData { get; private set; }
    public bool HasPoseDataUpdate { get; set; } = false;
    public Guid? SpawnedVfxId { get; set; }
    public Vector3? LastWorldPosition { get; set; }
    public Vector3? TargetWorldPosition { get; set; }
    public DateTime? UpdateStart { get; set; }
    private CharaDataDownloadDto? _charaData;
    public CharaDataDownloadDto? CharaData
    {
        get => _charaData; set
        {
            _charaData = value;
            LastUpdatedCharaData = _charaData?.UpdatedDate ?? DateTime.MaxValue;
        }
    }

    public DateTime LastUpdatedCharaData { get; private set; } = DateTime.MaxValue;
    public DateTime LastAppliedCharaDataDate { get; set; } = DateTime.MinValue;
    public nint Address { get; set; }
    public string AssociatedCharaName { get; set; } = string.Empty;

    private PoseData? CombinePoseData()
    {
        if (DeltaPoseData == null && FullPoseData != null) return FullPoseData;
        if (FullPoseData == null) return null;

        PoseData output = FullPoseData!.Value.DeepClone();
        PoseData delta = DeltaPoseData!.Value;

        foreach (var bone in FullPoseData!.Value.Bones)
        {
            if (!delta.Bones.TryGetValue(bone.Key, out var data)) continue;
            if (!data.Exists)
            {
                output.Bones.Remove(bone.Key);
            }
            else
            {
                output.Bones[bone.Key] = data;
            }
        }

        foreach (var bone in FullPoseData!.Value.MainHand)
        {
            if (!delta.MainHand.TryGetValue(bone.Key, out var data)) continue;
            if (!data.Exists)
            {
                output.MainHand.Remove(bone.Key);
            }
            else
            {
                output.MainHand[bone.Key] = data;
            }
        }

        foreach (var bone in FullPoseData!.Value.OffHand)
        {
            if (!delta.OffHand.TryGetValue(bone.Key, out var data)) continue;
            if (!data.Exists)
            {
                output.OffHand.Remove(bone.Key);
            }
            else
            {
                output.OffHand[bone.Key] = data;
            }
        }

        return output;
    }

    public string WorldDataDescriptor { get; private set; } = string.Empty;
    public Vector2 MapCoordinates { get; private set; }
    public Lumina.Excel.Sheets.Map Map { get; private set; }
    public HandledCharaDataEntry? HandledChara { get; set; }

    public async Task SetWorldDataDescriptor(DalamudUtilService dalamudUtilService)
    {
        if (WorldData == null)
        {
            WorldDataDescriptor = "No World Data found";
        }

        var worldData = WorldData!.Value;
        MapCoordinates = await dalamudUtilService.RunOnFrameworkThread(() =>
                MapUtil.WorldToMap(new Vector2(worldData.PositionX, worldData.PositionY), dalamudUtilService.MapData.Value[worldData.LocationInfo.MapId].Map))
            .ConfigureAwait(false);
        Map = dalamudUtilService.MapData.Value[worldData.LocationInfo.MapId].Map;

        StringBuilder sb = new();
        sb.AppendLine("Server: " + dalamudUtilService.WorldData.Value[(ushort)worldData.LocationInfo.ServerId]);
        sb.AppendLine("Territory: " + dalamudUtilService.TerritoryData.Value[worldData.LocationInfo.TerritoryId]);
        sb.AppendLine("Map: " + dalamudUtilService.MapData.Value[worldData.LocationInfo.MapId].MapName);

        if (worldData.LocationInfo.WardId != 0)
            sb.AppendLine("Ward #: " + worldData.LocationInfo.WardId);
        if (worldData.LocationInfo.DivisionId != 0)
        {
            sb.AppendLine("Subdivision: " + worldData.LocationInfo.DivisionId switch
            {
                1 => "No",
                2 => "Yes",
                _ => "-"
            });
        }
        if (worldData.LocationInfo.HouseId != 0)
        {
            sb.AppendLine("House #: " + (worldData.LocationInfo.HouseId == 100 ? "Apartments" : worldData.LocationInfo.HouseId.ToString()));
        }
        if (worldData.LocationInfo.RoomId != 0)
        {
            sb.AppendLine("Apartment #: " + worldData.LocationInfo.RoomId);
        }
        sb.AppendLine("Coordinates: X: " + MapCoordinates.X.ToString("0.0", CultureInfo.InvariantCulture) + ", Y: " + MapCoordinates.Y.ToString("0.0", CultureInfo.InvariantCulture));
        WorldDataDescriptor = sb.ToString();
    }
}
