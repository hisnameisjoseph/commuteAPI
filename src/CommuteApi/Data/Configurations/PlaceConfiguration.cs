using CommuteApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommuteApi.Data.Configurations;

public sealed class PlaceConfiguration : IEntityTypeConfiguration<Place>
{
    public const int NameMaxLength = 100;
    public const int AliasesMaxLength = 500;
    public const int LeadMinutesMax = 180;

    public void Configure(EntityTypeBuilder<Place> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Places", table =>
        {
            // Defence in depth: the form already limits these, but the database is the last word.
            table.HasCheckConstraint("CK_Places_RouteType", "[RouteType] IN (0, 2)");
            table.HasCheckConstraint("CK_Places_LeadMinutes", $"[LeadMinutes] BETWEEN 0 AND {LeadMinutesMax}");
        });

        builder.Property(p => p.Name).HasMaxLength(NameMaxLength);
        builder.HasIndex(p => p.Name).IsUnique();

        builder.Property(p => p.Aliases).HasMaxLength(AliasesMaxLength);

        // Stored as the PTV route_type integer, not as text, so the column means the same
        // thing here as it does in PTV and ptv-display.
        builder.Property(p => p.RouteType).HasConversion<int>();

        builder.Property(p => p.LeadMinutes).HasDefaultValue(0);
    }
}
