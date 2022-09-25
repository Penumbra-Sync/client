#nullable disable


namespace MareSynchronos.FileCacheDB
{
    public partial class FileCacheEntity
    {
        public string Hash { get; set; }
        public string Filepath { get; set; }
        public string LastModifiedDate { get; set; }
        public int Version { get; set; }
    }
}
