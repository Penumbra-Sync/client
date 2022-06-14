using Dalamud.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MareSynchronos.Models
{
    [JsonObject(MemberSerialization.OptIn)]
    public class CharacterCache
    {
        [JsonProperty]
        public List<FileReplacement> AllReplacements =>
            FileReplacements.Where(x => x.HasFileReplacement)
            .Concat(FileReplacements.SelectMany(f => f.Associated).Where(f => f.HasFileReplacement))
            .Concat(FileReplacements.SelectMany(f => f.Associated).SelectMany(f => f.Associated).Where(f => f.HasFileReplacement))
            .Distinct().OrderBy(f => f.GamePath)
            .ToList();

        public List<FileReplacement> FileReplacements { get; set; } = new List<FileReplacement>();

        [JsonProperty]
        public string GlamourerString { get; private set; } = string.Empty;

        public bool IsReady => FileReplacements.All(f => f.Computed);

        [JsonProperty]
        public string CacheHash { get; set; } = string.Empty;

        [JsonProperty]
        public uint JobId { get; set; } = 0;
        public void AddAssociatedResource(FileReplacement resource, FileReplacement mdlParent, FileReplacement mtrlParent)
        {
            try
            {
                if (resource == null) return;
                if (mdlParent == null)
                {
                    resource.IsInUse = true;
                    FileReplacements.Add(resource);
                    return;
                }

                FileReplacement replacement;

                if (mtrlParent == null && (replacement = FileReplacements.SingleOrDefault(f => f == mdlParent)!) != null)
                {
                    replacement.AddAssociated(resource);
                }

                if ((replacement = FileReplacements.SingleOrDefault(f => f == mdlParent)?.Associated.SingleOrDefault(f => f == mtrlParent)!) != null)
                {
                    replacement.AddAssociated(resource);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Debug(ex.Message);
            }
        }

        public void Invalidate(List<FileReplacement>? fileReplacements = null)
        {
            try
            {
                var fileReplacement = fileReplacements ?? FileReplacements.ToList();
                foreach (var item in fileReplacement)
                {
                    item.IsInUse = false;
                    Invalidate(item.Associated);
                    if (FileReplacements.Contains(item))
                    {
                        FileReplacements.Remove(item);
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Debug(ex.Message);
            }
        }

        public void SetGlamourerData(string glamourerString)
        {
            GlamourerString = glamourerString;
        }
        public override string ToString()
        {
            StringBuilder stringBuilder = new StringBuilder();
            foreach (var fileReplacement in FileReplacements.OrderBy(a => a.GamePath))
            {
                stringBuilder.AppendLine(fileReplacement.ToString());
            }
            return stringBuilder.ToString();
        }
    }
}
