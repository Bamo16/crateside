using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Crateside.Core.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class InheritedLateReading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A marker older than its run came with the previous run's evidence; the judge saw every reading.
            // One with no time is the 24 October stamp, which only runs from before 22:21 UTC that day carry.
            migrationBuilder.Sql(
                """
                UPDATE Runs SET Evidence = json_remove(Evidence, '$.read_after_verdict')
                WHERE json_valid(Evidence)
                  AND json_type(Evidence, '$.read_after_verdict') IS NOT NULL
                  AND (
                    julianday(json_extract(Evidence, '$.read_after_verdict.at')) < julianday(RecordedAt)
                    OR (
                      json_extract(Evidence, '$.read_after_verdict.at') IS NULL
                      AND julianday(RecordedAt) >= julianday('2026-10-24T22:21:00+00:00')
                    )
                  );
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
