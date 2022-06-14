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
        public List<FileReplacement> FileReplacements { get; set; } = new List<FileReplacement>();

        [JsonProperty]
        public List<FileReplacement> AllReplacements =>
            FileReplacements.Where(x => x.HasFileReplacement)
            .Concat(FileReplacements.SelectMany(f => f.Associated).Where(f => f.HasFileReplacement))
            .Concat(FileReplacements.SelectMany(f => f.Associated).SelectMany(f => f.Associated).Where(f => f.HasFileReplacement))
            .ToList();

        public CharacterCache()
        {

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
