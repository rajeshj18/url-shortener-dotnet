using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrlShortener.Core.Links
{
    /// <summary>
    /// EF Core context for the <c>links</c> database (LLD §4.1), owned by the Api service (ADR-0009).
    /// Expiry columns are added by TSK-14 (refinement R1).
    /// </summary>
    public sealed class LinksDbContext : DbContext
    {
        /// <summary>Creates the context.</summary>
        /// <param name="options">Options configured by the composition root or the design-time factory.</param>
        public LinksDbContext(DbContextOptions<LinksDbContext> options)
            : base(options)
        {
        }

        /// <summary>Short links.</summary>
        public DbSet<ShortUrlRecord> ShortUrls => Set<ShortUrlRecord>();

        /// <summary>Region ID blocks (ADR-0005).</summary>
        public DbSet<IdBlockRecord> IdBlocks => Set<IdBlockRecord>();

        /// <summary>Idempotency keys (ADR-0010).</summary>
        public DbSet<IdempotencyKeyRecord> IdempotencyKeys => Set<IdempotencyKeyRecord>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);

            modelBuilder.Entity<ShortUrlRecord>(ConfigureShortUrls);
            modelBuilder.Entity<IdBlockRecord>(ConfigureIdBlocks);
            modelBuilder.Entity<IdempotencyKeyRecord>(ConfigureIdempotencyKeys);
        }

        private static void ConfigureShortUrls(EntityTypeBuilder<ShortUrlRecord> entity)
        {
            entity.ToTable("short_urls");
            entity.HasKey(x => x.Code).HasName("pk_short_urls");
            entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(30);
            entity.Property(x => x.Target).HasColumnName("long_url").HasMaxLength(2048).IsRequired();
            entity.Property(x => x.OwnerId).HasColumnName("owner_id").HasMaxLength(64).IsRequired();
            entity.Property(x => x.IsCustom).HasColumnName("is_custom");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.DeletedAt).HasColumnName("deleted_at");
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        }

        private static void ConfigureIdBlocks(EntityTypeBuilder<IdBlockRecord> entity)
        {
            entity.ToTable("id_blocks", table =>
            {
                table.HasCheckConstraint("ck_id_blocks_next_within_range", "range_start <= next_id AND next_id <= range_end");
                table.HasCheckConstraint("ck_id_blocks_status", "status IN ('active', 'reserved', 'exhausted')");
            });
            entity.HasKey(x => x.BlockId).HasName("pk_id_blocks");
            entity.Property(x => x.BlockId).HasColumnName("block_id").HasMaxLength(10);
            entity.Property(x => x.RegionIndex).HasColumnName("region_index");
            entity.Property(x => x.RangeStart).HasColumnName("range_start");
            entity.Property(x => x.RangeEnd).HasColumnName("range_end");
            entity.Property(x => x.NextId).HasColumnName("next_id");
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(10).IsRequired();

            // At most one active block per region, so the lease statement never has to choose (LLD §6.4).
            entity.HasIndex(x => x.RegionIndex)
                .IsUnique()
                .HasFilter("status = 'active'")
                .HasDatabaseName("ux_id_blocks_one_active");
        }

        private static void ConfigureIdempotencyKeys(EntityTypeBuilder<IdempotencyKeyRecord> entity)
        {
            entity.ToTable("idempotency_keys");
            entity.HasKey(x => new { x.OwnerId, x.IdemKey }).HasName("pk_idempotency_keys");
            entity.Property(x => x.OwnerId).HasColumnName("owner_id").HasMaxLength(64);
            entity.Property(x => x.IdemKey).HasColumnName("idem_key").HasMaxLength(128);
            entity.Property(x => x.RequestHash).HasColumnName("request_hash").HasColumnType("char(64)").IsRequired();
            entity.Property(x => x.ResponseBody).HasColumnName("response_body").HasColumnType("jsonb");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");

            // Used by the cleanup job that deletes keys older than 24 h (LLD §6.6).
            entity.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_idempotency_keys_created");
        }
    }
}
