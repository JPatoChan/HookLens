using HookLens.Models;
using Microsoft.EntityFrameworkCore;

namespace HookLens.Data;

public sealed class HookLensDbContext : DbContext
{
    public HookLensDbContext(DbContextOptions<HookLensDbContext> options)
        : base(options)
    {
    }

    public DbSet<CapturedRequestEntity> CapturedRequests => Set<CapturedRequestEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CapturedRequestEntity>(entity =>
        {
            entity.HasKey(request => request.Id);
            entity.Property(request => request.Id).HasMaxLength(64);
            entity.Property(request => request.Source).IsRequired();
            entity.Property(request => request.ReceivedAtUtc).IsRequired();
            entity.Property(request => request.HeadersJson).IsRequired();
            entity.Property(request => request.Body).IsRequired();
        });
    }
}

public sealed class CapturedRequestEntity
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; }
    public string HeadersJson { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}
