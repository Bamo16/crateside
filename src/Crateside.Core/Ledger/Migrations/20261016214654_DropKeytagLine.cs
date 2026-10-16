using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crateside.Core.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class DropKeytagLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Runs_KeytagLine",
                table: "Runs");

            migrationBuilder.DropIndex(
                name: "IX_Restatements_KeytagLine",
                table: "Restatements");

            migrationBuilder.DropColumn(
                name: "KeytagLine",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "KeytagLine",
                table: "Restatements");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KeytagLine",
                table: "Runs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeytagLine",
                table: "Restatements",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Runs_KeytagLine",
                table: "Runs",
                column: "KeytagLine",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Restatements_KeytagLine",
                table: "Restatements",
                column: "KeytagLine",
                unique: true);
        }
    }
}
