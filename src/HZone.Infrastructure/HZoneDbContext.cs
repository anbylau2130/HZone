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
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<Ledger> Ledgers => Set<Ledger>();
    public DbSet<LedgerMember> LedgerMembers => Set<LedgerMember>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<AiConversation> AiConversations => Set<AiConversation>();
    public DbSet<AiMessage> AiMessages => Set<AiMessage>();
    public DbSet<AiProviderSetting> AiProviderSettings => Set<AiProviderSetting>();
    public DbSet<AiSkill> AiSkills => Set<AiSkill>();
    public DbSet<AiMcpServer> AiMcpServers => Set<AiMcpServer>();
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
            // 上传幂等去重键：同 owner 同内容（哈希）命中未绑定资产即复用。
            // 过滤列（哈希可空）不能直接复合唯一——查询走 (Owner, Hash) 非唯一索引即可
            e.HasIndex(m => new { m.OwnerUserId, m.ContentHash });
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
            // 多歌单：条目按 (PlaylistId, SortOrder) 索引取序；引用的音频资产
            // 不随内容删除联动（Restrict）——资产被内容解绑不废列表项；
            // 归属歌单删除级联清掉条目（SQLite 迁移路径由端点手工级联兜底）。
            e.HasIndex(p => new { p.PlaylistId, p.SortOrder });
            e.HasOne(p => p.MediaAsset)
                .WithMany()
                .HasForeignKey(p => p.MediaAssetId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Playlist)
                .WithMany(l => l.Items)
                .HasForeignKey(p => p.PlaylistId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(p => p.Title).HasMaxLength(200);
            e.Property(p => p.Url).HasMaxLength(2048);
        });

        modelBuilder.Entity<Playlist>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(50);
            e.HasIndex(p => new { p.UserId, p.CreatedAt });
        });

        modelBuilder.Entity<Ledger>(e =>
        {
            e.Property(l => l.Name).HasMaxLength(50);
            e.HasIndex(l => l.OwnerUserId);
        });

        modelBuilder.Entity<LedgerMember>(e =>
        {
            // 成员唯一（同人不重复入本）；账本删成员随删
            e.HasIndex(m => new { m.LedgerId, m.UserId }).IsUnique();
            e.HasOne(m => m.Ledger)
                .WithMany(l => l.Members)
                .HasForeignKey(m => m.LedgerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LedgerEntry>(e =>
        {
            // 月度查询口径：(账本, 日期) 索引
            e.HasIndex(x => new { x.LedgerId, x.EntryDate });
            e.HasOne(x => x.Ledger)
                .WithMany(l => l.Entries)
                .HasForeignKey(x => x.LedgerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Recorder)
                .WithMany()
                .HasForeignKey(x => x.RecorderUserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Category).HasMaxLength(32);
            e.Property(x => x.Note).HasMaxLength(200);
        });

        // ---------- AI 对话 / 技能 / MCP ----------
        modelBuilder.Entity<AiConversation>(e =>
        {
            e.HasIndex(c => new { c.UserId, c.UpdatedAt });
            e.HasMany(c => c.Messages)
                .WithOne(m => m.Conversation)
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(c => c.Title).HasMaxLength(120);
        });

        modelBuilder.Entity<AiMessage>(e =>
        {
            // 会话内时间正序取历史
            e.HasIndex(m => new { m.ConversationId, m.CreatedAt });
            e.Property(m => m.Content).HasMaxLength(200_000);
            e.Property(m => m.ToolCallsJson).HasMaxLength(20_000);
        });

        modelBuilder.Entity<AiProviderSetting>(e =>
        {
            // 每用户一条：UserId 做主键
            e.HasKey(p => p.UserId);
            e.Property(p => p.BaseUrl).HasMaxLength(500);
            e.Property(p => p.ApiKey).HasMaxLength(500);
            e.Property(p => p.Model).HasMaxLength(100);
        });

        modelBuilder.Entity<AiSkill>(e =>
        {
            e.HasIndex(s => s.UserId);
            e.Property(s => s.Name).HasMaxLength(50);
            e.Property(s => s.Icon).HasMaxLength(8);
            e.Property(s => s.Prompt).HasMaxLength(10_000);
        });

        modelBuilder.Entity<AiMcpServer>(e =>
        {
            e.HasIndex(s => s.UserId);
            e.Property(s => s.Name).HasMaxLength(50);
            e.Property(s => s.Url).HasMaxLength(1000);
            e.Property(s => s.AuthHeader).HasMaxLength(1000);
        });
    }
}
