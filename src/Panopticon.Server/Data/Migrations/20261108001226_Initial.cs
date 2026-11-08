using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Panopticon.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_name = table.Column<string>(type: "text", nullable: false),
                    machine_name = table.Column<string>(type: "text", nullable: false),
                    client_name_key = table.Column<string>(type: "text", nullable: false),
                    machine_name_key = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "repos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host = table.Column<string>(type: "text", nullable: false),
                    owner = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    host_key = table.Column<string>(type: "text", nullable: false),
                    owner_key = table.Column<string>(type: "text", nullable: false),
                    name_key = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    repo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    name_key = table.Column<string>(type: "text", nullable: false),
                    last_reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    snapshot = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_changes_repos_repo_id",
                        column: x => x.repo_id,
                        principalTable: "repos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "change_agents",
                columns: table => new
                {
                    change_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change_agents", x => new { x.change_id, x.agent_id });
                    table.ForeignKey(
                        name: "fk_change_agents_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_change_agents_changes_change_id",
                        column: x => x.change_id,
                        principalTable: "changes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    change_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_section = table.Column<int>(type: "integer", nullable: true),
                    tasks_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_report_events_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_report_events_changes_change_id",
                        column: x => x.change_id,
                        principalTable: "changes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agents_client_name_key_machine_name_key",
                table: "agents",
                columns: new[] { "client_name_key", "machine_name_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_change_agents_agent_id",
                table: "change_agents",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_changes_last_reported_at",
                table: "changes",
                column: "last_reported_at");

            migrationBuilder.CreateIndex(
                name: "ix_changes_repo_id_name_key",
                table: "changes",
                columns: new[] { "repo_id", "name_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_report_events_agent_id",
                table: "report_events",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_report_events_change_id_received_at",
                table: "report_events",
                columns: new[] { "change_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_repos_host_key_owner_key_name_key",
                table: "repos",
                columns: new[] { "host_key", "owner_key", "name_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "change_agents");

            migrationBuilder.DropTable(
                name: "report_events");

            migrationBuilder.DropTable(
                name: "agents");

            migrationBuilder.DropTable(
                name: "changes");

            migrationBuilder.DropTable(
                name: "repos");
        }
    }
}
