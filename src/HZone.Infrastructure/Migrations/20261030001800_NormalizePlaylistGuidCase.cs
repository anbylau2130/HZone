using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HZone.Infrastructure.Migrations
{
    /// <summary>
    /// GUID 大小写统一（SQLite TEXT 二进制比较）：AddMultiPlaylists 回填用 lower(hex(...))
    /// 生成小写 Id，而 EF/Sqlite 参数绑定为大写——往回填歌单加曲会 FK 约束失败。
    /// 本迁移把 Playlists/PlaylistItems 既有行统一为大写。
    /// </summary>
    [DbContext(typeof(HZoneDbContext))]
    [Migration("20261030001800_NormalizePlaylistGuidCase")]
    public partial class NormalizePlaylistGuidCase : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE Playlists
                SET Id = UPPER(Id), UserId = UPPER(UserId)
                WHERE Id != UPPER(Id) OR UserId != UPPER(UserId);

                UPDATE PlaylistItems
                SET Id = UPPER(Id),
                    UserId = UPPER(UserId),
                    PlaylistId = UPPER(PlaylistId),
                    MediaAssetId = UPPER(MediaAssetId)
                WHERE Id != UPPER(Id) OR UserId != UPPER(UserId)
                   OR PlaylistId != UPPER(PlaylistId) OR MediaAssetId != UPPER(MediaAssetId);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 大小写规范化不可逆（原始大小写未保留），Down 为空操作
        }
    }
}
