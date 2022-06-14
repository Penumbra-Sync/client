using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MareSynchronos.PenumbraMod
{
    [JsonObject(MemberSerialization.OptOut)]
    internal class Meta
    {
        public int FileVersion { get; set; } = 1;
        public string Name { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Version { get; set; } = "0";
        public string Website { get; set; } = string.Empty;
        public long ImportDate { get; set; } = DateTime.Now.Ticks;
    }
}
