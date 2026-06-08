using EHealth.Lab.Models;
using Microsoft.EntityFrameworkCore;

namespace EHealth.Lab.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<LabResult> LabResults => Set<LabResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LabResult>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Formula).HasConversion<string>();
        });
    }
}
