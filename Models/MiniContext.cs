
using Microsoft.EntityFrameworkCore;

namespace MiniProjectDesigner.Models.Data
{
    public partial class MiniContext : DbContext
    {
        public MiniContext()
        {
        }

        public MiniContext(DbContextOptions<MiniContext> options)
            : base(options)
        {
        }
    }
}
