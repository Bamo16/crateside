using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crateside.Core.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class PersonsDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Familiarities",
                columns: table => new
                {
                    Song = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Artist = table.Column<string>(type: "TEXT", nullable: true),
                    ArtistFans = table.Column<long>(type: "INTEGER", nullable: true),
                    SongRank = table.Column<int>(type: "INTEGER", nullable: true),
                    LookedUpAt = table.Column<string>(type: "TEXT", nullable: true),
                    LookupVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Settled = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Familiarities", x => x.Song);
                });

            migrationBuilder.CreateTable(
                name: "Genres",
                columns: table => new
                {
                    Song = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Genre = table.Column<string>(type: "TEXT", nullable: true),
                    IsSettled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LearnedFrom = table.Column<string>(type: "TEXT", nullable: true),
                    Suggested = table.Column<string>(type: "TEXT", nullable: true),
                    Heard = table.Column<string>(type: "TEXT", nullable: false),
                    LookedUpAt = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Genres", x => x.Song);
                });

            migrationBuilder.CreateTable(
                name: "PageChoices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TrackKey = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Page = table.Column<string>(type: "TEXT", nullable: false),
                    At = table.Column<string>(type: "TEXT", nullable: false),
                    UndoneAt = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageChoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SourceRemovals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TrackKey = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    At = table.Column<string>(type: "TEXT", nullable: false),
                    UndoneAt = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceRemovals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackMarks",
                columns: table => new
                {
                    Song = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Mark = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackMarks", x => new { x.Song, x.Mark });
                });

            migrationBuilder.CreateIndex(
                name: "IX_PageChoices_TrackKey",
                table: "PageChoices",
                column: "TrackKey");

            migrationBuilder.CreateIndex(
                name: "IX_SourceRemovals_TrackKey",
                table: "SourceRemovals",
                column: "TrackKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Familiarities");

            migrationBuilder.DropTable(
                name: "Genres");

            migrationBuilder.DropTable(
                name: "PageChoices");

            migrationBuilder.DropTable(
                name: "SourceRemovals");

            migrationBuilder.DropTable(
                name: "TrackMarks");
        }
    }
}
