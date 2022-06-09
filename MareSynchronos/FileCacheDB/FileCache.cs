using System;
using System.Collections.Generic;

#nullable disable

namespace MareSynchronos.FileCacheDB
{
    public partial class FileCache
    {
        public string Hash { get; set; }
        public string Filepath { get; set; }
        public string LastModifiedDate { get; set; }
    }
}
