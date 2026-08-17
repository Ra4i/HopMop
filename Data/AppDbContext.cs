using Microsoft.EntityFrameworkCore;
using HopMop.Models;

namespace HopMop.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }

        public DbSet<PhotoPair> PhotoPairs { get; set; }

        public DbSet<Inquiry> Inquiries { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Backstop for the duplicate-email check in UsersController: two
            // requests racing each other could both pass the check and then both
            // insert. The database refuses the second one.
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            // The admin lists are always sorted newest-first, and the inquiry
            // views filter on IsResolved.
            modelBuilder.Entity<Inquiry>()
                .HasIndex(i => new { i.IsResolved, i.CreatedAt });

            modelBuilder.Entity<PhotoPair>()
                .HasIndex(p => p.CreatedAt);
        }
    }
}
