using System.IO;
using MareSynchronos.Utils;
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
            string oldDbPath = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "FileCacheDebug.db");
            if (!Directory.Exists(Plugin.PluginInterface.ConfigDirectory.FullName))
            {
                Directory.CreateDirectory(Plugin.PluginInterface.ConfigDirectory.FullName);
            }
            var veryOldDbPath = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "..", "FileCacheDebug.db");
            if (File.Exists(veryOldDbPath))
            {
                Logger.Debug("Migrated old path to new path");
                File.Move(veryOldDbPath, oldDbPath, true);
            }
            if (File.Exists(oldDbPath))
            {
                File.Move(oldDbPath, DbPath, true);
            }

            Database.EnsureCreated();
        }

        public FileCacheContext(DbContextOptions<FileCacheContext> options)
            : base(options)
        {
        }

        public virtual DbSet<FileCache> FileCaches { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite("Data Source=" + DbPath);
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FileCache>(entity =>
            {
                entity.HasKey(e => new { e.Hash, e.Filepath });

                entity.ToTable("FileCache");

                entity.Property(c => c.Version).HasDefaultValue(0).IsRowVersion();
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
