using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panopticon.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class VisitRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_remote_answers_kind",
                table: "remote_answers");

            migrationBuilder.AddColumn<string>(
                name: "url",
                table: "remote_answers",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_remote_answers_kind",
                table: "remote_answers",
                sql: "(kind = 'question' AND questions_json IS NOT NULL AND tool_name IS NULL AND outcome IS NULL AND url IS NULL) OR (kind = 'permission' AND tool_name IS NOT NULL AND outcome IS NOT NULL AND outcome IN ('allow', 'deny') AND questions_json IS NULL AND url IS NULL) OR (kind = 'visit' AND url IS NOT NULL AND outcome IS NOT NULL AND outcome IN ('done', 'cant') AND tool_name IS NULL AND questions_json IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_remote_answers_kind",
                table: "remote_answers");

            migrationBuilder.DropColumn(
                name: "url",
                table: "remote_answers");

            // The old constraint has no visit kind: visit answers can't survive a rollback.
            migrationBuilder.Sql("DELETE FROM remote_answers WHERE kind = 'visit';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_remote_answers_kind",
                table: "remote_answers",
                sql: "(kind = 'question' AND questions_json IS NOT NULL AND tool_name IS NULL AND outcome IS NULL) OR (kind = 'permission' AND tool_name IS NOT NULL AND outcome IN ('allow', 'deny') AND questions_json IS NULL)");
        }
    }
}
