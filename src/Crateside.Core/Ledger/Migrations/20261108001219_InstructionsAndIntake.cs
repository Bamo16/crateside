using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crateside.Core.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class InstructionsAndIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Intake",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TrackKey = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    ChannelTitle = table.Column<string>(type: "TEXT", nullable: true),
                    AddedAt = table.Column<string>(type: "TEXT", nullable: true),
                    PremiumOutlook = table.Column<int>(type: "INTEGER", nullable: false),
                    IsUnavailable = table.Column<bool>(type: "INTEGER", nullable: false),
                    PageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Artist = table.Column<string>(type: "TEXT", nullable: true),
                    SongTitle = table.Column<string>(type: "TEXT", nullable: true),
                    Release = table.Column<string>(type: "TEXT", nullable: true),
                    RemovalAttemptedAt = table.Column<string>(type: "TEXT", nullable: true),
                    RemovalFailureReason = table.Column<string>(type: "TEXT", nullable: true),
                    RemovalFailureCanSucceedLater = table.Column<bool>(type: "INTEGER", nullable: false),
                    RemovedEntryId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Intake", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlaylistSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    PlaylistId = table.Column<string>(type: "TEXT", nullable: true),
                    RefreshMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    ConsumeOnArrival = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistSettings", x => x.Id);
                    table.CheckConstraint("CK_PlaylistSettings_Only", "Id = 1");
                });

            migrationBuilder.CreateTable(
                name: "TrackInstructions",
                columns: table => new
                {
                    TrackKey = table.Column<string>(type: "TEXT", nullable: false),
                    Plan = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: true),
                    AddedByUrl = table.Column<bool>(type: "INTEGER", nullable: false),
                    DifferentFrom = table.Column<string>(type: "TEXT", nullable: true),
                    AcceptedUpload = table.Column<string>(type: "TEXT", nullable: true),
                    CutRequestedAt = table.Column<string>(type: "TEXT", nullable: true),
                    Backfill = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackInstructions", x => x.TrackKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Intake_TrackKey",
                table: "Intake",
                column: "TrackKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Intake");

            migrationBuilder.DropTable(
                name: "PlaylistSettings");

            migrationBuilder.DropTable(
                name: "TrackInstructions");
        }
    }
}
