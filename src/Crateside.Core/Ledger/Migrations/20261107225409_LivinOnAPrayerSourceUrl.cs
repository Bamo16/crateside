using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crateside.Core.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class LivinOnAPrayerSourceUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE Sources SET Url = 'https://www.overthinkingit.com/2009/07/24/bon-jovi-livin-on-a-prayer-music-theory/' WHERE Url = 'https://www.overthinkingit.com/2009/07/24/bon-jovi-livin-on-a-prayer-music-theory/amp/';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE Sources SET Url = 'https://www.overthinkingit.com/2009/07/24/bon-jovi-livin-on-a-prayer-music-theory/amp/' WHERE Url = 'https://www.overthinkingit.com/2009/07/24/bon-jovi-livin-on-a-prayer-music-theory/';");
        }
    }
}
