using MareSynchronos.API.Dto.User;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI.Handlers
{
    public class TagHandler
    {
        private readonly Configuration _configuration;

        public TagHandler(Configuration configuration)
        {
            _configuration = configuration;
        }

        public void AddTag(string tag)
        {
            GetAvailableTagsForCurrentServer().Add(tag);
            _configuration.Save();
        }

        public void RemoveTag(string tag)
        {
            // First remove the tag from teh available pair tags
            GetAvailableTagsForCurrentServer().Remove(tag);
            // Then also clean up the tag in all the pairs
            GetUidTagDictionaryForCurrentServer().Keys
                .ToList()
                .ForEach(otherUid => RemoveTagFromPairedUid(otherUid, tag));
            _configuration.Save();
        }

        public void SetTagOpen(string tag, bool open)
        {
            if (open)
            {
                _configuration.OpenPairTags.Add(tag);
            }
            else
            {
                _configuration.OpenPairTags.Remove(tag);
            }
            _configuration.Save();
        }
        
        /// <summary>
        /// Is this tag opened in the paired clients UI?
        /// </summary>
        /// <param name="tag">the tag</param>
        /// <returns>open true/false</returns>
        public bool IsTagOpen(string tag)
        {
            return _configuration.OpenPairTags.Contains(tag);
        }

        public List<string> GetAllTagsSorted()
        {
            return GetAvailableTagsForCurrentServer()
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public HashSet<string> GetOtherUidsForTag(string tag)
        {
            return GetUidTagDictionaryForCurrentServer()
                .Where(pair => pair.Value.Contains(tag, StringComparer.Ordinal))
                .Select(pair => pair.Key)
                .ToHashSet(StringComparer.Ordinal);
        }

        public void AddTagToPairedUid(UserPairDto pair, string tagName)
        {
            var tagDictionary = GetUidTagDictionaryForCurrentServer();
            var tagsForPair = tagDictionary.GetValueOrDefault(pair.User.UID, new List<string>());
            tagsForPair.Add(tagName);
            tagDictionary[pair.User.UID] = tagsForPair;
            _configuration.Save();
        }
        
        public void RemoveTagFromPairedUid(UserPairDto pair, string tagName)
        {
            RemoveTagFromPairedUid(pair.User.UID, tagName);
            _configuration.Save();
        }

        public bool HasTag(UserPairDto pair, string tagName)
        {
            var tagsForPair = GetUidTagDictionaryForCurrentServer().GetValueOrDefault(pair.User.UID, new List<string>());
            return tagsForPair.Contains(tagName, StringComparer.Ordinal);
        }

        public bool HasAnyTag(UserPairDto pair)
        {
            return GetUidTagDictionaryForCurrentServer().ContainsKey(pair.User.UID);
        }
        
        private void RemoveTagFromPairedUid(string otherUid, string tagName)
        {
            var tagsForPair = GetUidTagDictionaryForCurrentServer().GetValueOrDefault(otherUid, new List<string>());
            tagsForPair.Remove(tagName);
            if (!tagsForPair.Any())
            {
                // No more entries in list -> we can kick out that entry completely
                GetUidTagDictionaryForCurrentServer().Remove(otherUid);
            }
            else
            {
                GetUidTagDictionaryForCurrentServer()[otherUid] = tagsForPair;
            }
        }

        private Dictionary<string, List<string>> GetUidTagDictionaryForCurrentServer()
        {
            if (!_configuration.UidServerPairedUserTags.ContainsKey(_configuration.ApiUri))
            {
                _configuration.UidServerPairedUserTags.Add(_configuration.ApiUri, new(StringComparer.Ordinal));
            }

            return _configuration.UidServerPairedUserTags[_configuration.ApiUri];
        }
        
        private HashSet<string> GetAvailableTagsForCurrentServer()
        {
            if (!_configuration.ServerAvailablePairTags.ContainsKey(_configuration.ApiUri))
            {
                _configuration.ServerAvailablePairTags.Add(_configuration.ApiUri, new(StringComparer.Ordinal));
            }

            return _configuration.ServerAvailablePairTags[_configuration.ApiUri];
        }
    }
}