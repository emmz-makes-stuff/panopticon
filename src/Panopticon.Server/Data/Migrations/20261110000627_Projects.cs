using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panopticon.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Projects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_changes_repo_id_archive_folder",
                table: "changes");

            migrationBuilder.DropIndex(
                name: "ix_changes_repo_id_name_key",
                table: "changes");

            migrationBuilder.AddColumn<Guid>(
                name: "owner_member_id",
                table: "changes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                table: "changes",
                type: "uuid",
                nullable: true);

            // projects D1: an existing change becomes the personal work of the member behind its earliest report that
            // has one. A change with no member's report (Development bootstrap, or from before team-auth) keeps both
            // scope columns null, which every signed-in member sees.
            migrationBuilder.Sql("""
                UPDATE changes AS c
                SET owner_member_id = first_member.member_id
                FROM (
                    SELECT DISTINCT ON (e.change_id) e.change_id, a.member_id
                    FROM report_events AS e
                    JOIN agents AS a ON a.id = e.agent_id
                    WHERE a.member_id IS NOT NULL
                    ORDER BY e.change_id, e.received_at, e.id
                ) AS first_member
                WHERE first_member.change_id = c.id;
                """);

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    code_key = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_member_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
                    table.ForeignKey(
                        name: "fk_projects_members_created_by_member_id",
                        column: x => x.created_by_member_id,
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_members",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    invited_by_member_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_members", x => new { x.project_id, x.member_id });
                    table.CheckConstraint("ck_project_members_role", "role IN ('owner', 'editor', 'viewer')");
                    table.ForeignKey(
                        name: "fk_project_members_members_invited_by_member_id",
                        column: x => x.invited_by_member_id,
                        principalTable: "members",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_project_members_members_member_id",
                        column: x => x.member_id,
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_project_members_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_changes_owner_member_id",
                table: "changes",
                column: "owner_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_changes_project_id",
                table: "changes",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_changes_repo_id_archive_folder_project_id_owner_member_id",
                table: "changes",
                columns: new[] { "repo_id", "archive_folder", "project_id", "owner_member_id" },
                unique: true,
                filter: "archive_folder IS NOT NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_changes_repo_id_name_key_project_id_owner_member_id",
                table: "changes",
                columns: new[] { "repo_id", "name_key", "project_id", "owner_member_id" },
                unique: true,
                filter: "archived_at IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_changes_one_scope",
                table: "changes",
                sql: "project_id IS NULL OR owner_member_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_invited_by_member_id",
                table: "project_members",
                column: "invited_by_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_member_id",
                table: "project_members",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_code_key",
                table: "projects",
                column: "code_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_projects_created_by_member_id",
                table: "projects",
                column: "created_by_member_id");

            migrationBuilder.AddForeignKey(
                name: "fk_changes_members_owner_member_id",
                table: "changes",
                column: "owner_member_id",
                principalTable: "members",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_changes_projects_project_id",
                table: "changes",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_changes_members_owner_member_id",
                table: "changes");

            migrationBuilder.DropForeignKey(
                name: "fk_changes_projects_project_id",
                table: "changes");

            migrationBuilder.DropTable(
                name: "project_members");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropIndex(
                name: "ix_changes_owner_member_id",
                table: "changes");

            migrationBuilder.DropIndex(
                name: "ix_changes_project_id",
                table: "changes");

            migrationBuilder.DropIndex(
                name: "ix_changes_repo_id_archive_folder_project_id_owner_member_id",
                table: "changes");

            migrationBuilder.DropIndex(
                name: "ix_changes_repo_id_name_key_project_id_owner_member_id",
                table: "changes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_changes_one_scope",
                table: "changes");

            // Before the scope-free indexes come back, changes that differ only by scope would collide. Keep the oldest
            // of each colliding set and delete the rest with everything that hangs off them. Acceptable only because
            // this rolls back a change made before going live (projects design, Migration Plan).
            migrationBuilder.Sql("""
                CREATE TEMP TABLE doomed_changes ON COMMIT DROP AS
                SELECT id FROM (
                    SELECT id, row_number() OVER (PARTITION BY repo_id, name_key ORDER BY last_reported_at, id) AS n
                    FROM changes WHERE archived_at IS NULL
                ) AS active WHERE n > 1
                UNION
                SELECT id FROM (
                    SELECT id, row_number() OVER (PARTITION BY repo_id, archive_folder ORDER BY last_reported_at, id) AS n
                    FROM changes WHERE archive_folder IS NOT NULL
                ) AS archived WHERE n > 1;
                DELETE FROM report_documents WHERE report_event_id IN
                    (SELECT id FROM report_events WHERE change_id IN (SELECT id FROM doomed_changes));
                DELETE FROM report_events WHERE change_id IN (SELECT id FROM doomed_changes);
                DELETE FROM change_agents WHERE change_id IN (SELECT id FROM doomed_changes);
                DELETE FROM change_documents WHERE change_id IN (SELECT id FROM doomed_changes);
                DELETE FROM changes WHERE id IN (SELECT id FROM doomed_changes);
                """);

            migrationBuilder.DropColumn(
                name: "owner_member_id",
                table: "changes");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "changes");

            migrationBuilder.CreateIndex(
                name: "ix_changes_repo_id_archive_folder",
                table: "changes",
                columns: new[] { "repo_id", "archive_folder" },
                unique: true,
                filter: "archive_folder IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_changes_repo_id_name_key",
                table: "changes",
                columns: new[] { "repo_id", "name_key" },
                unique: true,
                filter: "archived_at IS NULL");
        }
    }
}
