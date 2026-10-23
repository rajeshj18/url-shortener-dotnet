using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrlShortener.Infrastructure.Persistence.Links.Migrations
{
    /// <inheritdoc />
    public partial class InitialLinksSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "id_blocks",
                columns: table => new
                {
                    block_id = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    region_index = table.Column<short>(type: "smallint", nullable: false),
                    range_start = table.Column<long>(type: "bigint", nullable: false),
                    range_end = table.Column<long>(type: "bigint", nullable: false),
                    next_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_id_blocks", x => x.block_id);
                    table.CheckConstraint("ck_id_blocks_next_within_range", "range_start <= next_id AND next_id <= range_end");
                    table.CheckConstraint("ck_id_blocks_status", "status IN ('active', 'reserved', 'exhausted')");
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                columns: table => new
                {
                    owner_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    idem_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    request_hash = table.Column<string>(type: "char(64)", nullable: false),
                    response_body = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_keys", x => new { x.owner_id, x.idem_key });
                });

            migrationBuilder.CreateTable(
                name: "short_urls",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    long_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    owner_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    is_custom = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_short_urls", x => x.code);
                });

            migrationBuilder.CreateIndex(
                name: "ux_id_blocks_one_active",
                table: "id_blocks",
                column: "region_index",
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_created",
                table: "idempotency_keys",
                column: "created_at");

            // ---- Hand-written (TSK-06): the Redirect read contract and least-privilege grants (ADR-0009, LLD §4.4) ----
            // The view runs with its OWNER's privileges (the migrator), so Redirect can read these 3 columns without
            // any right on short_urls. NEVER add "WITH (security_invoker = true)": the view would then run with the
            // caller's rights, and Redirect would need (and get) table access. A Testcontainers test proves the denial.
            migrationBuilder.Sql(@"
CREATE VIEW redirect_lookup AS
    SELECT code, long_url, deleted_at
    FROM short_urls;");

            // Roles are created by the platform (local init script / IaC), because they need passwords (R3).
            // Grants are versioned here with the schema. Nothing for PUBLIC; each role gets only what it needs.
            migrationBuilder.Sql(@"
REVOKE ALL ON short_urls, id_blocks, idempotency_keys, redirect_lookup FROM PUBLIC;
GRANT SELECT, INSERT, UPDATE, DELETE ON short_urls, id_blocks, idempotency_keys TO urlshortener_api;
GRANT SELECT ON redirect_lookup TO urlshortener_redirect;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS redirect_lookup;");

            migrationBuilder.DropTable(
                name: "id_blocks");

            migrationBuilder.DropTable(
                name: "idempotency_keys");

            migrationBuilder.DropTable(
                name: "short_urls");
        }
    }
}
