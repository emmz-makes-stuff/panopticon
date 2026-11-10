using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panopticon.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Tags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "change_agent_tags",
                columns: table => new
                {
                    change_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    value_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    value = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change_agent_tags", x => new { x.change_id, x.agent_id, x.key_key, x.value_key });
                    table.ForeignKey(
                        name: "fk_change_agent_tags_change_agents_change_id_agent_id",
                        columns: x => new { x.change_id, x.agent_id },
                        principalTable: "change_agents",
                        principalColumns: new[] { "change_id", "agent_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    value = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    key_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    value_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_tags", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_tags_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "change_project_tags",
                columns: table => new
                {
                    change_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change_project_tags", x => new { x.change_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_change_project_tags_changes_change_id",
                        column: x => x.change_id,
                        principalTable: "changes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_change_project_tags_project_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "project_tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_change_agent_tags_key_key_value_key",
                table: "change_agent_tags",
                columns: new[] { "key_key", "value_key" });

            migrationBuilder.CreateIndex(
                name: "ix_change_project_tags_tag_id",
                table: "change_project_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_tags_project_id_key_key_value_key",
                table: "project_tags",
                columns: new[] { "project_id", "key_key", "value_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "change_agent_tags");

            migrationBuilder.DropTable(
                name: "change_project_tags");

            migrationBuilder.DropTable(
                name: "project_tags");
        }
    }
}
