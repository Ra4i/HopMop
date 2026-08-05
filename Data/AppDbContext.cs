using Microsoft.EntityFrameworkCore;
using HopMop.Models;

namespace HopMop.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<AdminUser> AdminUsers { get; set; }
        public DbSet<PhotoPair> PhotoPairs { get; set; }
        public DbSet<Inquiry> Inquiries { get; set; }
    }
}
