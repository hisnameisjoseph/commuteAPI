using System.ComponentModel.DataAnnotations;
using CommuteApi.Data.Configurations;

namespace CommuteApi.Models;

/// <summary>
/// What the Create and Edit forms bind to. Kept separate from <see cref="Place"/> so a form
/// post can never set Id or the audit timestamps (over-posting).
/// </summary>
public sealed class PlaceForm
{
    [Required]
    [StringLength(PlaceConfiguration.NameMaxLength)]
    public string Name { get; set; } = "";

    [StringLength(PlaceConfiguration.AliasesMaxLength)]
    [Display(Name = "Aliases", Description = "Comma separated, for example: Telstra, Highpoint Shopping Centre")]
    public string? Aliases { get; set; }

    [Required]
    [EnumDataType(typeof(TransportMode), ErrorMessage = "Choose train or bus. ptv-display shows no other modes.")]
    [Display(Name = "Mode")]
    public TransportMode? RouteType { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Enter the PTV stop ID.")]
    [Display(Name = "Stop ID")]
    public int? StopId { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Enter the PTV route ID.")]
    [Display(Name = "Route ID")]
    public int? RouteId { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Direction ID cannot be negative.")]
    [Display(Name = "Direction ID (optional)")]
    public int? DirectionId { get; set; }

    [Range(0, PlaceConfiguration.LeadMinutesMax)]
    [Display(Name = "Lead time (minutes)")]
    public int LeadMinutes { get; set; }

    public static PlaceForm From(Place place)
    {
        ArgumentNullException.ThrowIfNull(place);

        return new PlaceForm
        {
            Name = place.Name,
            Aliases = place.Aliases,
            RouteType = place.RouteType,
            StopId = place.StopId,
            RouteId = place.RouteId,
            DirectionId = place.DirectionId,
            LeadMinutes = place.LeadMinutes,
        };
    }

    /// <summary>Copies validated values onto an entity. Call only when ModelState is valid.</summary>
    public void ApplyTo(Place place)
    {
        ArgumentNullException.ThrowIfNull(place);

        place.Name = Name.Trim();
        place.Aliases = NormaliseAliases(Aliases);
        place.RouteType = RouteType!.Value;
        place.StopId = StopId!.Value;
        place.RouteId = RouteId!.Value;
        place.DirectionId = DirectionId;
        place.LeadMinutes = LeadMinutes;
    }

    /// <summary>"  Telstra ,, Highpoint SC " becomes "Telstra, Highpoint SC"; blank becomes null.</summary>
    public static string? NormaliseAliases(string? aliases)
    {
        if (string.IsNullOrWhiteSpace(aliases))
        {
            return null;
        }

        var parts = aliases
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var joined = string.Join(", ", parts);
        return joined.Length == 0 ? null : joined;
    }
}
