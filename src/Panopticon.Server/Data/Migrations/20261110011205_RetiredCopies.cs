using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panopticon.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class RetiredCopies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "retired_to_project_id",
                table: "changes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_changes_retired_to_project_id",
                table: "changes",
                column: "retired_to_project_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_changes_retired_unfoldered",
                table: "changes",
                sql: "retired_to_project_id IS NULL OR (archived_at IS NOT NULL AND archive_folder IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_changes_projects_retired_to_project_id",
                table: "changes",
                column: "retired_to_project_id",
                principalTable: "projects",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_changes_projects_retired_to_project_id",
                table: "changes");

            migrationBuilder.DropIndex(
                name: "ix_changes_retired_to_project_id",
                table: "changes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_changes_retired_unfoldered",
                table: "changes");

            migrationBuilder.DropColumn(
                name: "retired_to_project_id",
                table: "changes");
        }
    }
}
