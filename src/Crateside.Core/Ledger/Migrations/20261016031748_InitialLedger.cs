using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crateside.Core.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class InitialLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Runs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecordedAt = table.Column<string>(type: "TEXT", nullable: false),
                    Song = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Artist = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: true),
                    LocalSource = table.Column<bool>(type: "INTEGER", nullable: false),
                    InputPath = table.Column<string>(type: "TEXT", nullable: true),
                    InputKind = table.Column<string>(type: "TEXT", nullable: true),
                    HasVocals = table.Column<bool>(type: "INTEGER", nullable: true),
                    Confidence = table.Column<string>(type: "TEXT", nullable: false),
                    Tonic = table.Column<string>(type: "TEXT", nullable: true),
                    Mode = table.Column<string>(type: "TEXT", nullable: true),
                    Mixed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Direction = table.Column<string>(type: "TEXT", nullable: true),
                    TonicCheck = table.Column<string>(type: "TEXT", nullable: true),
                    Reasoning = table.Column<string>(type: "TEXT", nullable: true),
                    Evidence = table.Column<string>(type: "TEXT", nullable: true),
                    OldBpm = table.Column<double>(type: "REAL", nullable: true),
                    OldBpmSource = table.Column<string>(type: "TEXT", nullable: true),
                    OldBpmBeatdetect = table.Column<double>(type: "REAL", nullable: true),
                    KeytagLine = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runs", x => x.Id);
                    table.CheckConstraint("CK_Runs_Centre", "(Tonic IS NULL) = (Mode IS NULL)");
                    table.CheckConstraint("CK_Runs_Source", "SourceUrl IS NOT NULL OR LocalSource = 1");
                });

            migrationBuilder.CreateTable(
                name: "Restatements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecordedAt = table.Column<string>(type: "TEXT", nullable: false),
                    RunId = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: false),
                    KeytagLine = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Restatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Restatements_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sections",
                columns: table => new
                {
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    RunId = table.Column<long>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    Tonic = table.Column<string>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sections", x => new { x.RunId, x.Position });
                    table.ForeignKey(
                        name: "FK_Sections_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sources",
                columns: table => new
                {
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    RunId = table.Column<long>(type: "INTEGER", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: true),
                    Via = table.Column<string>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    States = table.Column<string>(type: "TEXT", nullable: true),
                    StatesKey = table.Column<string>(type: "TEXT", nullable: true),
                    StatesMode = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Sections = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sources", x => new { x.RunId, x.Position });
                    table.ForeignKey(
                        name: "FK_Sources_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Windows",
                columns: table => new
                {
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    RunId = table.Column<long>(type: "INTEGER", nullable: false),
                    Start = table.Column<double>(type: "REAL", nullable: false),
                    End = table.Column<double>(type: "REAL", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: true),
                    AskedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Windows", x => new { x.RunId, x.Position });
                    table.ForeignKey(
                        name: "FK_Windows_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Restatements_KeytagLine",
                table: "Restatements",
                column: "KeytagLine",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Restatements_RunId",
                table: "Restatements",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_Runs_KeytagLine",
                table: "Runs",
                column: "KeytagLine",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Runs_Song_RecordedAt",
                table: "Runs",
                columns: new[] { "Song", "RecordedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Restatements");

            migrationBuilder.DropTable(
                name: "Sections");

            migrationBuilder.DropTable(
                name: "Sources");

            migrationBuilder.DropTable(
                name: "Windows");

            migrationBuilder.DropTable(
                name: "Runs");
        }
    }
}
