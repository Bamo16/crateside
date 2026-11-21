using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crateside.Core.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class DecidedByKept : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DecidedBy",
                table: "Runs",
                type: "TEXT",
                nullable: false,
                defaultValue: "Judge");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DecidedBy",
                table: "Runs");
        }
    }
}
