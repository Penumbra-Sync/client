using System.IO;
using Microsoft.EntityFrameworkCore;

#nullable disable

namespace MareSynchronos.FileCacheDB
{
    public partial class FileCacheContext : DbContext
    {
        private string DbPath { get; set; }
        public FileCacheContext()
        {
            DbPath = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "FileCache.db");
            Database.EnsureCreated();
        }

        public FileCacheContext(DbContextOptions<FileCacheContext> options)
            : base(options)
        {
        }

        public virtual DbSet<FileCacheEntity> FileCaches { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite("Data Source=" + DbPath+";Cache=Shared");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FileCacheEntity>(entity =>
            {
                entity.HasKey(e => new { e.Hash, e.Filepath });

                entity.ToTable("FileCache");
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
