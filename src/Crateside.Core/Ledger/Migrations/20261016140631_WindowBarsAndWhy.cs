using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crateside.Core.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class WindowBarsAndWhy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FirstBar",
                table: "Windows",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastBar",
                table: "Windows",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Why",
                table: "Windows",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstBar",
                table: "Windows");

            migrationBuilder.DropColumn(
                name: "LastBar",
                table: "Windows");

            migrationBuilder.DropColumn(
                name: "Why",
                table: "Windows");
        }
    }
}
