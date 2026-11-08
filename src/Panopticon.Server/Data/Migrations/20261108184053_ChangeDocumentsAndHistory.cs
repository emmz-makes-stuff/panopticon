using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panopticon.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChangeDocumentsAndHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_changes_repo_id_name_key",
                table: "changes");

            migrationBuilder.AddColumn<string>(
                name: "archive_folder",
                table: "changes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at",
                table: "changes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "change_documents",
                columns: table => new
                {
                    change_id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_change_documents", x => new { x.change_id, x.path });
                    table.ForeignKey(
                        name: "fk_change_documents_changes_change_id",
                        column: x => x.change_id,
                        principalTable: "changes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_documents",
                columns: table => new
                {
                    report_event_id = table.Column<long>(type: "bigint", nullable: false),
                    path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_documents", x => new { x.report_event_id, x.path });
                    table.ForeignKey(
                        name: "fk_report_documents_report_events_report_event_id",
                        column: x => x.report_event_id,
                        principalTable: "report_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "change_documents");

            migrationBuilder.DropTable(
                name: "report_documents");

            migrationBuilder.DropIndex(
                name: "ix_changes_repo_id_archive_folder",
                table: "changes");

            migrationBuilder.DropIndex(
                name: "ix_changes_repo_id_name_key",
                table: "changes");

            migrationBuilder.DropColumn(
                name: "archive_folder",
                table: "changes");

            migrationBuilder.DropColumn(
                name: "archived_at",
                table: "changes");

            migrationBuilder.CreateIndex(
                name: "ix_changes_repo_id_name_key",
                table: "changes",
                columns: new[] { "repo_id", "name_key" },
                unique: true);
        }
    }
}
