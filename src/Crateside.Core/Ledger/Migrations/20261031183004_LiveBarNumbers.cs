using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crateside.Core.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class LiveBarNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bars stored so far counted from the clip start; the seconds beside them stay, and bars are read off the warp again (ADR 0072).
            migrationBuilder.Sql(
                "UPDATE Windows SET FirstBar = NULL, LastBar = NULL WHERE FirstBar IS NOT NULL OR LastBar IS NOT NULL;"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
