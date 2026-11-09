using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crateside.Core.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class GenresFromAbleton : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR 0088 starts genre over: every answer and learned genre goes, and every song is asked again.
            // The ledger-backupsefore-20261109223108_GenresFromAbleton.db copy made before this runs keeps them.
            migrationBuilder.DropColumn(
                name: "Genre",
                table: "Genres");

            migrationBuilder.DropColumn(
                name: "IsSettled",
                table: "Genres");

            migrationBuilder.DropColumn(
                name: "Suggested",
                table: "Genres");

            migrationBuilder.AddColumn<string>(
                name: "Learned",
                table: "Genres",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecordingMbid",
                table: "Genres",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mine",
                table: "Genres",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Tagged",
                table: "Genres",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.Sql("UPDATE \"Genres\" SET \"LearnedFrom\" = NULL, \"LookedUpAt\" = NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Learned",
                table: "Genres");

            migrationBuilder.DropColumn(
                name: "RecordingMbid",
                table: "Genres");

            migrationBuilder.DropColumn(
                name: "Mine",
                table: "Genres");

            migrationBuilder.DropColumn(
                name: "Tagged",
                table: "Genres");

            migrationBuilder.AddColumn<string>(
                name: "Genre",
                table: "Genres",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSettled",
                table: "Genres",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Suggested",
                table: "Genres",
                type: "TEXT",
                nullable: true);
        }
    }
}
