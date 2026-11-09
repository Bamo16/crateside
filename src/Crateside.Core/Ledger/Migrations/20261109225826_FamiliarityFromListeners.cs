using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crateside.Core.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class FamiliarityFromListeners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR 0089 starts familiarity over: every row and the person's answers go, and every song is asked again.
            // The ledger-backups/before-20261109225826_FamiliarityFromListeners.db copy made before this runs keeps them.
            migrationBuilder.Sql("DELETE FROM \"Familiarities\"");

            migrationBuilder.DropColumn(
                name: "Artist",
                table: "Familiarities");

            migrationBuilder.DropColumn(
                name: "ArtistFans",
                table: "Familiarities");

            migrationBuilder.DropColumn(
                name: "SongRank",
                table: "Familiarities");

            migrationBuilder.AddColumn<int>(
                name: "Listeners",
                table: "Familiarities",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Pageviews",
                table: "Familiarities",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WikidataItem",
                table: "Familiarities",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Listeners",
                table: "Familiarities");

            migrationBuilder.DropColumn(
                name: "Pageviews",
                table: "Familiarities");

            migrationBuilder.DropColumn(
                name: "WikidataItem",
                table: "Familiarities");

            migrationBuilder.AddColumn<string>(
                name: "Artist",
                table: "Familiarities",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ArtistFans",
                table: "Familiarities",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SongRank",
                table: "Familiarities",
                type: "INTEGER",
                nullable: true);
        }
    }
}
