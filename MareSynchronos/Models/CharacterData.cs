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
        public List<FileReplacement> FileReplacements { get; set; } = new();

        [JsonProperty]
        public string GlamourerString { get; set; } = string.Empty;

        public bool IsReady => FileReplacements.All(f => f.Computed);

        [JsonProperty]
        public int JobId { get; set; } = 0;

        public string ManipulationString { get; set; } = string.Empty;

        public void AddFileReplacement(FileReplacement fileReplacement)
        {
            if (!fileReplacement.HasFileReplacement) return;

            var existingReplacement = FileReplacements.SingleOrDefault(f => f.ResolvedPath == fileReplacement.ResolvedPath);
            if (existingReplacement != null)
            {
                existingReplacement.GamePaths.AddRange(fileReplacement.GamePaths.Where(e => !existingReplacement.GamePaths.Contains(e)));
            }
            else
            {
                FileReplacements.Add(fileReplacement);
            }
        }

        public CharacterCacheDto ToCharacterCacheDto()
        {
            return new CharacterCacheDto()
            {
                FileReplacements = FileReplacements.Where(f => f.HasFileReplacement).GroupBy(f => f.Hash).Select(g =>
                {
                    return new FileReplacementDto()
                    {
                        GamePaths = g.SelectMany(g => g.GamePaths).Distinct().ToArray(),
                        Hash = g.First().Hash
                    };
                }).ToList(),
                GlamourerData = GlamourerString,
                JobId = JobId,
                ManipulationData = ManipulationString
            };
        }

        public override string ToString()
        {
            StringBuilder stringBuilder = new();
            foreach (var fileReplacement in FileReplacements.OrderBy(a => a.GamePaths[0]))
            {
                stringBuilder.AppendLine(fileReplacement.ToString());
            }
            return stringBuilder.ToString();
        }
    }
}
