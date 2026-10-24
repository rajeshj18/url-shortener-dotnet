using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using UrlShortener.Core.Links;

#nullable disable

namespace UrlShortener.Core.Links.Migrations
{
    /// <summary>
    /// Hand-written (TSK-SIMPLE): the analytics store of LLD §4.2 as an <c>analytics</c> schema, expand-only. EF maps no
    /// entities to it (the worker and the stats use plain Npgsql), so the model snapshot doesn't change.
    /// Rights (LLD §4.4): the worker reads and writes analytics only; the Api only reads it, for the stats endpoint.
    /// </summary>
    [DbContext(typeof(LinksDbContext))]
    [Migration("20261024090000_AddAnalytics")]
    public partial class AddAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE SCHEMA analytics;

CREATE TABLE analytics.click_rollups_hourly (
    code        varchar(30)  NOT NULL,
    bucket_hour timestamptz  NOT NULL,
    clicks      bigint       NOT NULL,
    CONSTRAINT pk_click_rollups_hourly PRIMARY KEY (code, bucket_hour));

CREATE TABLE analytics.click_rollups_referrer (
    code          varchar(30)  NOT NULL,
    bucket_hour   timestamptz  NOT NULL,
    referrer_host varchar(255) NOT NULL,
    clicks        bigint       NOT NULL,
    CONSTRAINT pk_click_rollups_referrer PRIMARY KEY (code, bucket_hour, referrer_host));

CREATE TABLE analytics.click_rollups_device (
    code        varchar(30)  NOT NULL,
    bucket_hour timestamptz  NOT NULL,
    device      varchar(16)  NOT NULL,
    clicks      bigint       NOT NULL,
    CONSTRAINT pk_click_rollups_device PRIMARY KEY (code, bucket_hour, device));

-- Kafka position per partition, committed in the same transaction as the rollups (exactly once, LLD §6.5).
CREATE TABLE analytics.consumer_offsets (
    consumer_group varchar(100) NOT NULL,
    topic          varchar(200) NOT NULL,
    partition_id   int          NOT NULL,
    next_offset    bigint       NOT NULL,
    updated_at     timestamptz  NOT NULL,
    CONSTRAINT pk_consumer_offsets PRIMARY KEY (consumer_group, topic, partition_id));

REVOKE ALL ON ALL TABLES IN SCHEMA analytics FROM PUBLIC;
GRANT USAGE ON SCHEMA analytics TO urlshortener_worker, urlshortener_api;
GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA analytics TO urlshortener_worker;
GRANT SELECT ON ALL TABLES IN SCHEMA analytics TO urlshortener_api;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS analytics CASCADE;");
        }
    }
}
