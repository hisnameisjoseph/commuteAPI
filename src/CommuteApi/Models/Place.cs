namespace CommuteApi.Models;

/// <summary>A saved destination, mapped by hand to the PTV stop and route ptv-display should pin.</summary>
public class Place : ITimestamped
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Comma separated alternative names, for example "Telstra, Highpoint Shopping Centre".</summary>
    public string? Aliases { get; set; }

    /// <summary>PTV <c>stop_id</c>, the identifier ptv-display uses for a card.</summary>
    public int StopId { get; set; }

    /// <summary>PTV route ID. ptv-display filters the pinned card to this route.</summary>
    public int RouteId { get; set; }

    /// <summary>PTV direction ID. Stored for later use; ptv-display splits directions itself.</summary>
    public int? DirectionId { get; set; }

    public TransportMode RouteType { get; set; }

    /// <summary>Travel time allowance, used by the "leave by" feature.</summary>
    public int LeadMinutes { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
