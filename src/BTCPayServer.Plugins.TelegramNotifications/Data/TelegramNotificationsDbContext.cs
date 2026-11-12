using BTCPayServer.Abstractions.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.TelegramNotifications.Data;

[PluginDatabase("TelegramNotificationsMigrations")]
public sealed class TelegramNotificationsDbContext(DbContextOptions<TelegramNotificationsDbContext> options)
    : BasePluginDbContext<TelegramNotificationsDbContext>(options)
{
    public TelegramNotificationsDbContext() : this(new DbContextOptions<TelegramNotificationsDbContext>())
    {
    }

    public DbSet<NotificationEventRecord> NotificationEvents => Set<NotificationEventRecord>();
    public DbSet<TelegramActionRecord> Actions => Set<TelegramActionRecord>();
    public DbSet<TelegramStateRecord> State => Set<TelegramStateRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationEventRecord>(entity =>
        {
            entity.ToTable("TelegramNotificationEvents");
            entity.HasKey(x => x.EventKey);
            entity.Property(x => x.EventKey).HasMaxLength(300);
            entity.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<TelegramActionRecord>(entity =>
        {
            entity.ToTable("TelegramNotificationActions");
            entity.HasKey(x => x.Token);
            entity.Property(x => x.Token).HasMaxLength(40);
            entity.Property(x => x.InvoiceId).HasMaxLength(100);
            entity.Property(x => x.StoreId).HasMaxLength(100);
            entity.Property(x => x.Action).HasMaxLength(20);
            entity.Property(x => x.TelegramUsername).HasMaxLength(200);
            entity.Property(x => x.Result).HasMaxLength(500);
            entity.HasIndex(x => x.ExpiresAt);
        });

        modelBuilder.Entity<TelegramStateRecord>(entity =>
        {
            entity.ToTable("TelegramNotificationState");
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Key).HasMaxLength(100);
            entity.Property(x => x.Value).HasMaxLength(500);
        });
    }
}

public sealed class NotificationEventRecord
{
    public string EventKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TelegramActionRecord
{
    public string Token { get; set; } = string.Empty;
    public string InvoiceId { get; set; } = string.Empty;
    public string StoreId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public long ChatId { get; set; }
    public int? MessageId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public long? TelegramUserId { get; set; }
    public string? TelegramUsername { get; set; }
    public string? PreviousStatus { get; set; }
    public string? Result { get; set; }
}

public sealed class TelegramStateRecord
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
