using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crateside.Core.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class LiveAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Rejected",
                table: "Genres",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "RemovalsSeen",
                table: "Genres",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Rejected",
                table: "Familiarities",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "RemovalsSeen",
                table: "Familiarities",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Tagged",
                table: "Familiarities",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            // What the files already carry was written by crateside, so none of it reads as an answer given in Live (ADR 0087).
            migrationBuilder.Sql(
                """
                UPDATE "Familiarities"
                SET "Tagged" = '["' || COALESCE(
                    "Settled",
                    CASE
                        WHEN COALESCE("Listeners", 0) >= 3500 AND COALESCE("Pageviews", 0) >= 20000 THEN 'Mainstream'
                        WHEN COALESCE("Listeners", 0) < 3500 AND COALESCE("Pageviews", 0) < 20000 THEN 'Obscure'
                        ELSE 'Niche'
                    END
                ) || '"]'
                WHERE "Settled" IS NOT NULL OR "LookedUpAt" IS NOT NULL
                """);

            migrationBuilder.CreateTable(
                name: "VocalAnswers",
                columns: table => new
                {
                    Stem = table.Column<string>(type: "TEXT", nullable: false),
                    Answer = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VocalAnswers", x => x.Stem);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VocalAnswers");

            migrationBuilder.DropColumn(
                name: "Rejected",
                table: "Genres");

            migrationBuilder.DropColumn(
                name: "RemovalsSeen",
                table: "Genres");

            migrationBuilder.DropColumn(
                name: "Rejected",
                table: "Familiarities");

            migrationBuilder.DropColumn(
                name: "RemovalsSeen",
                table: "Familiarities");

            migrationBuilder.DropColumn(
                name: "Tagged",
                table: "Familiarities");
        }
    }
}
