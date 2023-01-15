using System;
using System.Collections.Generic;
using System.Linq;
using MareSynchronos.API;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI.Handlers
{
    public class TagHandler
    {
        private readonly Configuration _configuration;
        private readonly ApiController _apiController;

        public TagHandler(Configuration configuration)
        {
            _configuration = configuration;
        }

        public void AddTag(string tag)
        {
            _configuration.AvailablePairTags.Add(tag);
            _configuration.Save();
        }

        public void RemoveTag(string tag)
        {
            // First remove the tag from teh available pair tags
            _configuration.AvailablePairTags.Remove(tag);
            // Then also clean up the tag in all the pairs
            _configuration.UidPairedUserTags.Keys
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
            var result = _configuration.AvailablePairTags.ToList();
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        public HashSet<string> GetOtherUidsForTag(string tag)
        {
            return _configuration.UidPairedUserTags
                .Where(pair => pair.Value.Contains(tag, StringComparer.Ordinal))
                .Select(pair => pair.Key)
                .ToHashSet(StringComparer.Ordinal);
        }

        public void AddTagToPairedUid(ClientPairDto pair, string tagName)
        {
            var tagsForPair = _configuration.UidPairedUserTags.GetValueOrDefault(pair.OtherUID, new List<string>());
            tagsForPair.Add(tagName);
            _configuration.UidPairedUserTags[pair.OtherUID] = tagsForPair;
            _configuration.Save();
        }
        
        public void RemoveTagFromPairedUid(ClientPairDto pair, string tagName)
        {
            RemoveTagFromPairedUid(pair.OtherUID, tagName);
            _configuration.Save();
        }

        public bool HasTag(ClientPairDto pair, string tagName)
        {
            var tagsForPair = _configuration.UidPairedUserTags.GetValueOrDefault(pair.OtherUID, new List<string>());
            return tagsForPair.Contains(tagName, StringComparer.Ordinal);
        }

        public bool HasAnyTag(ClientPairDto pair)
        {
            return _configuration.UidPairedUserTags.ContainsKey(pair.OtherUID);
        }
        
        private void RemoveTagFromPairedUid(string otherUid, string tagName)
        {
            var tagsForPair = _configuration.UidPairedUserTags.GetValueOrDefault(otherUid, new List<string>());
            tagsForPair.Remove(tagName);
            if (tagsForPair.Count <= 0)
            {
                _configuration.UidPairedUserTags.Remove(otherUid);
            }
            else
            {
                _configuration.UidPairedUserTags[otherUid] = tagsForPair;
            }
        }
    }
}