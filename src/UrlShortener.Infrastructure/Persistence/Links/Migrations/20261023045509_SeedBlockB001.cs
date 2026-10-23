using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrlShortener.Infrastructure.Persistence.Links.Migrations
{
    /// <inheritdoc />
    public partial class SeedBlockB001 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- Hand-written (TSK-07): block B-001 for region 0, exactly as in config/id-blocks.json (ADR-0005).
            // An integration test checks that this row matches the registry. New blocks are added by new migrations
            // (LLD §6.4 runbook); never edit this one.
            migrationBuilder.Sql(@"
INSERT INTO id_blocks (block_id, region_index, range_start, range_end, next_id, status)
VALUES ('B-001', 0, 0, 440201825776, 0, 'active');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM id_blocks WHERE block_id = 'B-001';");
        }
    }
}
