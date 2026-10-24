using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrlShortener.Core.Links.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                table: "short_urls",
                type: "timestamp with time zone",
                nullable: true);

            // ---- Hand-written (TSK-14): expand-only. Appending a column to the view is allowed by CREATE OR REPLACE
            // and keeps its grants. Old instances keep working: they never select expires_at. NEVER add
            // "WITH (security_invoker = true)" (see InitialLinksSchema).
            migrationBuilder.Sql(@"
CREATE OR REPLACE VIEW redirect_lookup AS
    SELECT code, long_url, deleted_at, expires_at
    FROM short_urls;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A view can't lose a column through CREATE OR REPLACE, so recreate it (and its grants) as before.
            migrationBuilder.Sql(@"
DROP VIEW redirect_lookup;
CREATE VIEW redirect_lookup AS
    SELECT code, long_url, deleted_at
    FROM short_urls;
REVOKE ALL ON redirect_lookup FROM PUBLIC;
GRANT SELECT ON redirect_lookup TO urlshortener_redirect;");

            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "short_urls");
        }
    }
}
