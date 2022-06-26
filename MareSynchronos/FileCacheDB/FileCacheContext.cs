using System;
using System.IO;
using Dalamud.Logging;
using MareSynchronos.Utils;
using Microsoft.EntityFrameworkCore;

#nullable disable

namespace MareSynchronos.FileCacheDB
{
    public partial class FileCacheContext : DbContext
    {
        public FileCacheContext()
        {
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
                string dbPath = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "FileCacheDebug.db");
                if(!Directory.Exists(Plugin.PluginInterface.ConfigDirectory.FullName))
                {
                    Directory.CreateDirectory(Plugin.PluginInterface.ConfigDirectory.FullName);
                }
                var oldDbPath = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "..", "FileCacheDebug.db");
                if (File.Exists(oldDbPath))
                {
                    Logger.Debug("Migrated old path to new path");
                    File.Move(oldDbPath, dbPath, true);
                }
                //PluginLog.Debug("Using Database " + dbPath);
                optionsBuilder.UseSqlite("Data Source=" + dbPath);
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
