using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crateside.Core.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class MeasuringExclusions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MeasuringExclusions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Song = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    StartSeconds = table.Column<double>(type: "REAL", nullable: true),
                    EndSeconds = table.Column<double>(type: "REAL", nullable: true),
                    Typed = table.Column<string>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasuringExclusions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MeasuringExclusions_Song",
                table: "MeasuringExclusions",
                column: "Song");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MeasuringExclusions");
        }
    }
}
