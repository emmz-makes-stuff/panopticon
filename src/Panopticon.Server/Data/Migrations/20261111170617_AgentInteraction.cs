using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panopticon.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AgentInteraction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "away_since",
                table: "members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "remote_answers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    change_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    answered_by_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    answered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    tool_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    outcome = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    questions_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_remote_answers", x => x.id);
                    table.CheckConstraint("ck_remote_answers_kind", "(kind = 'question' AND questions_json IS NOT NULL AND tool_name IS NULL AND outcome IS NULL) OR (kind = 'permission' AND tool_name IS NOT NULL AND outcome IN ('allow', 'deny') AND questions_json IS NULL)");
                    table.ForeignKey(
                        name: "fk_remote_answers_changes_change_id",
                        column: x => x.change_id,
                        principalTable: "changes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_remote_answers_members_answered_by_member_id",
                        column: x => x.answered_by_member_id,
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_remote_answers_members_session_member_id",
                        column: x => x.session_member_id,
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_remote_answers_answered_by_member_id",
                table: "remote_answers",
                column: "answered_by_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_remote_answers_change_id_answered_at",
                table: "remote_answers",
                columns: new[] { "change_id", "answered_at" });

            migrationBuilder.CreateIndex(
                name: "ix_remote_answers_session_member_id",
                table: "remote_answers",
                column: "session_member_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "remote_answers");

            migrationBuilder.DropColumn(
                name: "away_since",
                table: "members");
        }
    }
}
