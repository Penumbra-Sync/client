using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MareSynchronos.API;

namespace MareSynchronos.Models
{
    [JsonObject(MemberSerialization.OptIn)]
    public class CharacterData
    {
        [JsonProperty]
        public Dictionary<ObjectKind, List<FileReplacement>> FileReplacements { get; set; } = new();

        [JsonProperty]
        public Dictionary<ObjectKind, string> GlamourerString { get; set; } = new();

        public bool IsReady => FileReplacements.SelectMany(k => k.Value).All(f => f.Computed);

        [JsonProperty]
        public string ManipulationString { get; set; } = string.Empty;

        public void AddFileReplacement(ObjectKind objectKind, FileReplacement fileReplacement)
        {
            if (!fileReplacement.HasFileReplacement) return;

            if (!FileReplacements.ContainsKey(objectKind)) FileReplacements.Add(objectKind, new List<FileReplacement>());

            var existingReplacement = FileReplacements[objectKind].SingleOrDefault(f => f.ResolvedPath == fileReplacement.ResolvedPath);
            if (existingReplacement != null)
            {
                existingReplacement.GamePaths.AddRange(fileReplacement.GamePaths.Where(e => !existingReplacement.GamePaths.Contains(e)));
            }
            else
            {
                FileReplacements[objectKind].Add(fileReplacement);
            }
        }

        public CharacterCacheDto ToCharacterCacheDto()
        {
            return new CharacterCacheDto()
            {
                FileReplacements = FileReplacements.ToDictionary(k => k.Key, k => k.Value.Where(f => f.HasFileReplacement).GroupBy(f => f.Hash).Select(g =>
                {
                    return new FileReplacementDto()
                    {
                        GamePaths = g.SelectMany(g => g.GamePaths).Distinct().ToArray(),
                        Hash = g.First().Hash
                    };
                }).ToList()),
                GlamourerData = GlamourerString.ToDictionary(d => d.Key, d => d.Value),
                ManipulationData = ManipulationString
            };
        }

        public override string ToString()
        {
            StringBuilder stringBuilder = new();
            foreach (var fileReplacement in FileReplacements.SelectMany(k => k.Value).OrderBy(a => a.GamePaths[0]))
            {
                stringBuilder.AppendLine(fileReplacement.ToString());
            }
            return stringBuilder.ToString();
        }
    }
}
