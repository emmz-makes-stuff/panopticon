using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Panopticon.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class TeamAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_agents_client_name_key_machine_name_key",
                table: "agents");

            migrationBuilder.AddColumn<Guid>(
                name: "member_id",
                table: "agents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    email_key = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    invited_by_member_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_members_members_invited_by_member_id",
                        column: x => x.invited_by_member_id,
                        principalTable: "members",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "api_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    machine_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_api_tokens_members_member_id",
                        column: x => x.member_id,
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_sessions_members_member_id",
                        column: x => x.member_id,
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sign_in_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sign_in_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_sign_in_links_members_member_id",
                        column: x => x.member_id,
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "device_logins",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    user_code = table.Column<string>(type: "character(8)", fixedLength: true, maxLength: 8, nullable: false),
                    machine_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_polled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    denied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    token_delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_logins", x => x.id);
                    table.ForeignKey(
                        name: "fk_device_logins_api_tokens_token_id",
                        column: x => x.token_id,
                        principalTable: "api_tokens",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_device_logins_members_approved_by_member_id",
                        column: x => x.approved_by_member_id,
                        principalTable: "members",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_agents_member_id_client_name_key_machine_name_key",
                table: "agents",
                columns: new[] { "member_id", "client_name_key", "machine_name_key" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_api_tokens_member_id",
                table: "api_tokens",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_tokens_secret_hash",
                table: "api_tokens",
                column: "secret_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_logins_approved_by_member_id",
                table: "device_logins",
                column: "approved_by_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_logins_device_secret_hash",
                table: "device_logins",
                column: "device_secret_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_logins_token_id",
                table: "device_logins",
                column: "token_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_logins_user_code",
                table: "device_logins",
                column: "user_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_members_email_key",
                table: "members",
                column: "email_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_members_invited_by_member_id",
                table: "members",
                column: "invited_by_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_member_id",
                table: "sessions",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_secret_hash",
                table: "sessions",
                column: "secret_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sign_in_links_member_id",
                table: "sign_in_links",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "ix_sign_in_links_secret_hash",
                table: "sign_in_links",
                column: "secret_hash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_agents_members_member_id",
                table: "agents",
                column: "member_id",
                principalTable: "members",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_agents_members_member_id",
                table: "agents");

            migrationBuilder.DropTable(
                name: "device_logins");

            migrationBuilder.DropTable(
                name: "sessions");

            migrationBuilder.DropTable(
                name: "sign_in_links");

            migrationBuilder.DropTable(
                name: "api_tokens");

            migrationBuilder.DropTable(
                name: "members");

            migrationBuilder.DropIndex(
                name: "ix_agents_member_id_client_name_key_machine_name_key",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "member_id",
                table: "agents");

            migrationBuilder.CreateIndex(
                name: "ix_agents_client_name_key_machine_name_key",
                table: "agents",
                columns: new[] { "client_name_key", "machine_name_key" },
                unique: true);
        }
    }
}
