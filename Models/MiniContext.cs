
using Microsoft.EntityFrameworkCore;

namespace MiniProjectDesigner.Models.Data
{
    public class MiniContext : DbContext
    {
        public MiniContext(DbContextOptions<MiniContext> options) : base(options) { }

        public DbSet<ProjectType> ProjectTypes { get; set; }
    }
}
