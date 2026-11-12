using CommuteApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommuteApi.Data.Configurations;

public sealed class MatchLogConfiguration : IEntityTypeConfiguration<MatchLog>
{
    public void Configure(EntityTypeBuilder<MatchLog> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("MatchLogs");

        // Hex SHA-256: always 64 ASCII characters.
        builder.Property(m => m.EventHash)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsUnicode(false);
        builder.HasIndex(m => m.EventHash);

        builder.Property(m => m.Method)
            .HasConversion<string>()
            .HasMaxLength(20);

        // Deleting a place keeps its history; the log just loses the link.
        builder.HasOne(m => m.Place)
            .WithMany()
            .HasForeignKey(m => m.PlaceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
