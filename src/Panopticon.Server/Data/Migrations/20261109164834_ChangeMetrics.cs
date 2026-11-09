using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panopticon.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChangeMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "current_section",
                table: "report_events",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "overall_done",
                table: "report_events",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "overall_total",
                table: "report_events",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sections",
                table: "report_events",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_report_events_progress_pending",
                table: "report_events",
                column: "id",
                filter: "overall_total IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_report_events_progress_pending",
                table: "report_events");

            migrationBuilder.DropColumn(
                name: "current_section",
                table: "report_events");

            migrationBuilder.DropColumn(
                name: "overall_done",
                table: "report_events");

            migrationBuilder.DropColumn(
                name: "overall_total",
                table: "report_events");

            migrationBuilder.DropColumn(
                name: "sections",
                table: "report_events");
        }
    }
}
