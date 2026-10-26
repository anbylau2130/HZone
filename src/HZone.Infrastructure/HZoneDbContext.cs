using HZone.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace HZone.Infrastructure;

public class HZoneDbContext : DbContext
{
    public HZoneDbContext(DbContextOptions<HZoneDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<Content> Contents => Set<Content>();
    public DbSet<Say> Says => Set<Say>();
    public DbSet<Journal> Journals => Set<Journal>();
    public DbSet<ContentRecipient> ContentRecipients => Set<ContentRecipient>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<ContentTag> ContentTags => Set<ContentTag>();
    public DbSet<ContentLike> ContentLikes => Set<ContentLike>();
    public DbSet<ShareLink> ShareLinks => Set<ShareLink>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<InviteCode> InviteCodes => Set<InviteCode>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<PersonalAccessToken> PersonalAccessTokens => Set<PersonalAccessToken>();
    public DbSet<PlaylistItem> PlaylistItems => Set<PlaylistItem>();
    public DbSet<SystemConfig> SystemConfigs => Set<SystemConfig>();
    public DbSet<EmailToken> EmailTokens => Set<EmailToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).HasMaxLength(320);
            e.Property(u => u.Signature).HasMaxLength(100);
            e.Property(u => u.Preferences).HasMaxLength(4096);
        });

        modelBuilder.Entity<Friendship>(e =>
        {
            e.HasIndex(f => new { f.RequesterId, f.AddresseeId }).IsUnique();
        });

        modelBuilder.Entity<Content>(e =>
        {
            e.ToTable("Contents");
            e.HasDiscriminator<string>("Discriminator")
                .HasValue<Say>("Say")
                .HasValue<Journal>("Journal");

            e.HasMany(c => c.Recipients)
                .WithOne(r => r.Content)
                .HasForeignKey(r => r.ContentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ContentRecipient>(e =>
        {
            e.HasIndex(r => new { r.ContentId, r.RecipientUserId }).IsUnique();
            e.HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.RecipientUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Comment>(e =>
        {
            e.HasOne(c => c.Content)
                .WithMany()
                .HasForeignKey(c => c.ContentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Author)
                .WithMany()
                .HasForeignKey(c => c.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ContentTag>(e =>
        {
            // 每内容每标签唯一；随内容级联删除（内容没了标签无意义）。
            // WithMany(c => c.Tags) 显式配对双向导航——漏配会生成第二个 FK 列
            //（ContentId1），查询 join 错列恒空（实测踩中）
            e.HasIndex(t => new { t.ContentId, t.Tag }).IsUnique();
            e.HasOne(t => t.Content)
                .WithMany(c => c.Tags)
                .HasForeignKey(t => t.ContentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(t => t.Tag).HasMaxLength(32);
        });

        modelBuilder.Entity<ContentLike>(e =>
        {
            // 每人每内容至多一赞；随内容级联删除
            e.HasIndex(l => new { l.ContentId, l.UserId }).IsUnique();
            e.HasOne(l => l.Content)
                .WithMany()
                .HasForeignKey(l => l.ContentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ShareLink>(e =>
        {
            // token 按哈希查询（唯一索引）；随内容级联删除
            e.HasIndex(s => s.TokenHash).IsUnique();
            e.HasOne(s => s.Content)
                .WithMany()
                .HasForeignKey(s => s.ContentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(s => s.TokenHash).HasMaxLength(64);
        });

        modelBuilder.Entity<InboxMessage>(e =>
        {
            e.HasOne(m => m.Content)
                .WithMany()
                .HasForeignKey(m => m.ContentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaAsset>(e =>
        {
            e.HasOne(m => m.Content)
                .WithMany()
                .HasForeignKey(m => m.ContentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<InviteCode>(e =>
        {
            e.HasIndex(i => i.Code).IsUnique();
        });

        modelBuilder.Entity<SystemConfig>(e =>
        {
            e.HasKey(s => s.Key);
        });

        modelBuilder.Entity<EmailToken>(e =>
        {
            e.HasIndex(t => t.Token).IsUnique();
        });

        modelBuilder.Entity<PersonalAccessToken>(e =>
        {
            e.HasIndex(p => p.TokenHash).IsUnique();
        });

        modelBuilder.Entity<PlaylistItem>(e =>
        {
            // 每用户一张列表：按 (UserId, SortOrder) 索引取序；列表项引用的音频资产
            // 不随内容删除联动（Restrict）——资产被内容解绑不废列表项。
            e.HasIndex(p => new { p.UserId, p.SortOrder });
            e.HasOne(p => p.MediaAsset)
                .WithMany()
                .HasForeignKey(p => p.MediaAssetId)
                .OnDelete(DeleteBehavior.Restrict);
            e.Property(p => p.Title).HasMaxLength(200);
            e.Property(p => p.Url).HasMaxLength(2048);
        });
    }
}
