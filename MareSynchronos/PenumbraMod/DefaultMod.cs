using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MareSynchronos.PenumbraMod
{
    [JsonObject(MemberSerialization.OptOut)]
    internal class DefaultMod
    {
        public string Name { get; set; } = "Default";
        public int Priority { get; set; } = 0;
        public Dictionary<string, string> Files { get; set; } = new();
        public Dictionary<string, string> FileSwaps { get; set; } = new();
        public List<string> Manipulations { get; set; } = new();
    }
}
