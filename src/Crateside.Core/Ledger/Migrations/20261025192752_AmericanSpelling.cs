using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crateside.Core.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class AmericanSpelling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Sources SET Kind = 'ChordRecognizer' WHERE Kind = 'ChordRecogniser';");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Runs_Centre",
                table: "Runs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Runs_Center",
                table: "Runs",
                sql: "(Tonic IS NULL) = (Mode IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Sources SET Kind = 'ChordRecogniser' WHERE Kind = 'ChordRecognizer';");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Runs_Center",
                table: "Runs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Runs_Centre",
                table: "Runs",
                sql: "(Tonic IS NULL) = (Mode IS NULL)");
        }
    }
}
