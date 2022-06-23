using System;
using System.IO;
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
                string dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "pluginConfigs", "FileCacheDebug.db");
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
