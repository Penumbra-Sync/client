using MareSynchronos.API.Dto.Group;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MareSynchronos.WebAPI.Utils
{
    internal class GroupPairDtoComparer : IEqualityComparer<GroupPairDto>
    {
        public bool Equals(GroupPairDto? x, GroupPairDto? y)
        {
            if (x == null || y == null) return false;
            return string.Equals(x.Group.GID, y.Group.GID, StringComparison.Ordinal) && string.Equals(x.User.UID, y.User.UID, StringComparison.Ordinal);
        }

        public int GetHashCode([DisallowNull] GroupPairDto obj)
        {
            return obj.GetHashCode();
        }
    }
}
