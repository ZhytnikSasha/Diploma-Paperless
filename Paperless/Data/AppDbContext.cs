using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Paperless.Models;

namespace Paperless.Data
{
    public class AppDbContext : IdentityDbContext<ApplicationUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        public DbSet<Document> Documents { get; set; }
        public DbSet<DocumentHistory> DocumentHistories { get; set; }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<ApplicationUser>().ToTable("Users");
            builder.Entity<IdentityRole>().ToTable("Roles");
            builder.Entity<IdentityUserRole<string>>().ToTable("UserRoles");
            builder.Entity<IdentityUserClaim<string>>().ToTable("UserClaims");
            builder.Entity<IdentityUserLogin<string>>().ToTable("UserLogins");
            builder.Entity<IdentityRoleClaim<string>>().ToTable("RoleClaims");
            builder.Entity<IdentityUserToken<string>>().ToTable("UserTokens");

            builder.Entity<Document>()
                .HasOne(d => d.Author)
                .WithMany()
                .HasForeignKey(d => d.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<DocumentHistory>()
                .HasOne(h => h.Document)
                .WithMany()
                .HasForeignKey(h => h.DocumentId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<DocumentHistory>()
                .HasOne(h => h.User)
                .WithMany()
                .HasForeignKey(h => h.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}