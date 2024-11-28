
using Microsoft.EntityFrameworkCore;

namespace MiniProjectDesigner.Models.Data
{
    public partial class MiniContext : DbContext
    {
        private static DbContextOptions<MiniContext> _defaultOptions;
        public MiniContext() : base(GetDefaultOptions())

        {


        }
        private static DbContextOptions<MiniContext> GetDefaultOptions()
        {
            if (_defaultOptions == null)
            {
                _defaultOptions = new DbContextOptionsBuilder<MiniContext>()
                    .UseSqlite("Data Source=localdatabase.db")
                    .Options;
            }
            return _defaultOptions;
        }

        public MiniContext(DbContextOptions<MiniContext> options) : base(options)
        {
        }

        public virtual DbSet<ProjectType> ProjectTypes { get; set; }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                // ≈‰‘«¡ „”«— ﬁ«⁄œ… «·»Ì«‰«  œ«Œ· „Ã·œ «· ÿ»Ìﬁ
                var databasePath = System.IO.Path.Combine(AppContext.BaseDirectory, "localdatabase.db");

                // ”·”·… « ’«· SQLite
                var connectionString = $"Data Source={databasePath}";

                optionsBuilder.UseSqlite(connectionString);
            }
        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ProjectType>(entity =>
            {
                entity.HasKey(e => e.Id); //  ⁄ÌÌ‰ «·‹ Primary Key

                entity.Property(e => e.TypeNameEn)
                    .IsRequired()
                    .HasMaxLength(255);

                entity.Property(e => e.TypeNameAr)
                    .IsRequired()
                    .HasMaxLength(255);

                entity.Property(e => e.IsActive)
                    .HasDefaultValue(true);

                entity.Property(e => e.CreatedDate)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP"); // √Ê GETDATE() ·‹ SQL Server
            });
        }

    }
}
