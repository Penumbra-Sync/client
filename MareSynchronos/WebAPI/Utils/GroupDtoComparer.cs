using MareSynchronos.API.Dto.Group;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MareSynchronos.WebAPI.Utils
{
    internal class GroupDtoComparer : IEqualityComparer<GroupDto>
    {
        public bool Equals(GroupDto? x, GroupDto? y)
        {
            if (x == null || y == null) return false;
            return string.Equals(x?.Group.GID, y?.Group.GID, StringComparison.Ordinal);
        }

        public int GetHashCode([DisallowNull] GroupDto obj)
        {
            return obj.GetHashCode();
        }
    }
}
