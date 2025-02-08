using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.CharaData;

namespace MareSynchronos.Services.CharaData.Models;

public sealed record CharaDataExtendedUpdateDto : CharaDataUpdateDto
{
    private readonly CharaDataFullDto _charaDataFullDto;

    public CharaDataExtendedUpdateDto(CharaDataUpdateDto dto, CharaDataFullDto charaDataFullDto) : base(dto)
    {
        _charaDataFullDto = charaDataFullDto;
        _userList = charaDataFullDto.AllowedUsers.ToList();
        _groupList = charaDataFullDto.AllowedGroups.ToList();
        _poseList = charaDataFullDto.PoseData.Select(k => new PoseEntry(k.Id)
        {
            Description = k.Description,
            PoseData = k.PoseData,
            WorldData = k.WorldData
        }).ToList();
    }

    public CharaDataUpdateDto BaseDto => new(Id)
    {
        AllowedUsers = AllowedUsers,
        AllowedGroups = AllowedGroups,
        AccessType = base.AccessType,
        CustomizeData = base.CustomizeData,
        Description = base.Description,
        ExpiryDate = base.ExpiryDate,
        FileGamePaths = base.FileGamePaths,
        FileSwaps = base.FileSwaps,
        GlamourerData = base.GlamourerData,
        ShareType = base.ShareType,
        ManipulationData = base.ManipulationData,
        Poses = Poses
    };

    public new string ManipulationData
    {
        get
        {
            return base.ManipulationData ?? _charaDataFullDto.ManipulationData;
        }
        set
        {
            base.ManipulationData = value;
            if (string.Equals(base.ManipulationData, _charaDataFullDto.ManipulationData, StringComparison.Ordinal))
            {
                base.ManipulationData = null;
            }
        }
    }

    public new string Description
    {
        get
        {
            return base.Description ?? _charaDataFullDto.Description;
        }
        set
        {
            base.Description = value;
            if (string.Equals(base.Description, _charaDataFullDto.Description, StringComparison.Ordinal))
            {
                base.Description = null;
            }
        }
    }

    public new DateTime ExpiryDate
    {
        get
        {
            return base.ExpiryDate ?? _charaDataFullDto.ExpiryDate;
        }
        private set
        {
            base.ExpiryDate = value;
            if (Equals(base.ExpiryDate, _charaDataFullDto.ExpiryDate))
            {
                base.ExpiryDate = null;
            }
        }
    }

    public new AccessTypeDto AccessType
    {
        get
        {
            return base.AccessType ?? _charaDataFullDto.AccessType;
        }
        set
        {
            base.AccessType = value;

            if (Equals(base.AccessType, _charaDataFullDto.AccessType))
            {
                base.AccessType = null;
            }
        }
    }

    public new ShareTypeDto ShareType
    {
        get
        {
            return base.ShareType ?? _charaDataFullDto.ShareType;
        }
        set
        {
            base.ShareType = value;

            if (Equals(base.ShareType, _charaDataFullDto.ShareType))
            {
                base.ShareType = null;
            }
        }
    }

    public new List<GamePathEntry>? FileGamePaths
    {
        get
        {
            return base.FileGamePaths ?? _charaDataFullDto.FileGamePaths;
        }
        set
        {
            base.FileGamePaths = value;
            if (!(base.FileGamePaths ?? []).Except(_charaDataFullDto.FileGamePaths).Any()
                && !_charaDataFullDto.FileGamePaths.Except(base.FileGamePaths ?? []).Any())
            {
                base.FileGamePaths = null;
            }
        }
    }

    public new List<GamePathEntry>? FileSwaps
    {
        get
        {
            return base.FileSwaps ?? _charaDataFullDto.FileSwaps;
        }
        set
        {
            base.FileSwaps = value;
            if (!(base.FileSwaps ?? []).Except(_charaDataFullDto.FileSwaps).Any()
                && !_charaDataFullDto.FileSwaps.Except(base.FileSwaps ?? []).Any())
            {
                base.FileSwaps = null;
            }
        }
    }

    public new string? GlamourerData
    {
        get
        {
            return base.GlamourerData ?? _charaDataFullDto.GlamourerData;
        }
        set
        {
            base.GlamourerData = value;
            if (string.Equals(base.GlamourerData, _charaDataFullDto.GlamourerData, StringComparison.Ordinal))
            {
                base.GlamourerData = null;
            }
        }
    }

    public new string? CustomizeData
    {
        get
        {
            return base.CustomizeData ?? _charaDataFullDto.CustomizeData;
        }
        set
        {
            base.CustomizeData = value;
            if (string.Equals(base.CustomizeData, _charaDataFullDto.CustomizeData, StringComparison.Ordinal))
            {
                base.CustomizeData = null;
            }
        }
    }

    public IEnumerable<UserData> UserList => _userList;
    private readonly List<UserData> _userList;

    public IEnumerable<GroupData> GroupList => _groupList;
    private readonly List<GroupData> _groupList;

    public IEnumerable<PoseEntry> PoseList => _poseList;
    private readonly List<PoseEntry> _poseList;

    public void AddUserToList(string user)
    {
        _userList.Add(new(user, null));
        UpdateAllowedUsers();
    }

    public void AddGroupToList(string group)
    {
        _groupList.Add(new(group, null));
        UpdateAllowedGroups();
    }

    private void UpdateAllowedUsers()
    {
        AllowedUsers = [.. _userList.Select(u => u.UID)];
        if (!AllowedUsers.Except(_charaDataFullDto.AllowedUsers.Select(u => u.UID), StringComparer.Ordinal).Any()
            && !_charaDataFullDto.AllowedUsers.Select(u => u.UID).Except(AllowedUsers, StringComparer.Ordinal).Any())
        {
            AllowedUsers = null;
        }
    }

    private void UpdateAllowedGroups()
    {
        AllowedGroups = [.. _groupList.Select(u => u.GID)];
        if (!AllowedGroups.Except(_charaDataFullDto.AllowedGroups.Select(u => u.GID), StringComparer.Ordinal).Any()
            && !_charaDataFullDto.AllowedGroups.Select(u => u.GID).Except(AllowedGroups, StringComparer.Ordinal).Any())
        {
            AllowedGroups = null;
        }
    }

    public void RemoveUserFromList(string user)
    {
        _userList.RemoveAll(u => string.Equals(u.UID, user, StringComparison.Ordinal));
        UpdateAllowedUsers();
    }

    public void RemoveGroupFromList(string group)
    {
        _groupList.RemoveAll(u => string.Equals(u.GID, group, StringComparison.Ordinal));
        UpdateAllowedGroups();
    }

    public void AddPose()
    {
        _poseList.Add(new PoseEntry(null));
        UpdatePoseList();
    }

    public void RemovePose(PoseEntry entry)
    {
        if (entry.Id != null)
        {
            entry.Description = null;
            entry.WorldData = null;
            entry.PoseData = null;
        }
        else
        {
            _poseList.Remove(entry);
        }

        UpdatePoseList();
    }

    public void UpdatePoseList()
    {
        Poses = [.. _poseList];
        if (!Poses.Except(_charaDataFullDto.PoseData).Any() && !_charaDataFullDto.PoseData.Except(Poses).Any())
        {
            Poses = null;
        }
    }

    public void SetExpiry(bool expiring)
    {
        if (expiring)
        {
            var date = DateTime.UtcNow.AddDays(7);
            SetExpiry(date.Year, date.Month, date.Day);
        }
        else
        {
            ExpiryDate = DateTime.MaxValue;
        }
    }

    public void SetExpiry(int year, int month, int day)
    {
        int daysInMonth = DateTime.DaysInMonth(year, month);
        if (day > daysInMonth) day = 1;
        ExpiryDate = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
    }

    internal void UndoChanges()
    {
        base.Description = null;
        base.AccessType = null;
        base.ShareType = null;
        base.GlamourerData = null;
        base.FileSwaps = null;
        base.FileGamePaths = null;
        base.CustomizeData = null;
        base.ManipulationData = null;
        AllowedUsers = null;
        AllowedGroups = null;
        Poses = null;
        _poseList.Clear();
        _poseList.AddRange(_charaDataFullDto.PoseData.Select(k => new PoseEntry(k.Id)
        {
            Description = k.Description,
            PoseData = k.PoseData,
            WorldData = k.WorldData
        }));
    }

    internal void RevertDeletion(PoseEntry pose)
    {
        if (pose.Id == null) return;
        var oldPose = _charaDataFullDto.PoseData.Find(p => p.Id == pose.Id);
        if (oldPose == null) return;
        pose.Description = oldPose.Description;
        pose.PoseData = oldPose.PoseData;
        pose.WorldData = oldPose.WorldData;
        UpdatePoseList();
    }

    internal bool PoseHasChanges(PoseEntry pose)
    {
        if (pose.Id == null) return false;
        var oldPose = _charaDataFullDto.PoseData.Find(p => p.Id == pose.Id);
        if (oldPose == null) return false;
        return !string.Equals(pose.Description, oldPose.Description, StringComparison.Ordinal)
            || !string.Equals(pose.PoseData, oldPose.PoseData, StringComparison.Ordinal)
            || pose.WorldData != oldPose.WorldData;
    }

    public bool HasChanges =>
                base.Description != null
                || base.ExpiryDate != null
                || base.AccessType != null
                || base.ShareType != null
                || AllowedUsers != null
                || AllowedGroups != null
                || base.GlamourerData != null
                || base.FileSwaps != null
                || base.FileGamePaths != null
                || base.CustomizeData != null
                || base.ManipulationData != null
                || Poses != null;

    public bool IsAppearanceEqual =>
        string.Equals(GlamourerData, _charaDataFullDto.GlamourerData, StringComparison.Ordinal)
        && string.Equals(CustomizeData, _charaDataFullDto.CustomizeData, StringComparison.Ordinal)
        && FileGamePaths == _charaDataFullDto.FileGamePaths
        && FileSwaps == _charaDataFullDto.FileSwaps
        && string.Equals(ManipulationData, _charaDataFullDto.ManipulationData, StringComparison.Ordinal);
}
