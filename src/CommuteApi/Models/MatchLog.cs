namespace CommuteApi.Models;

/// <summary>
/// One matching decision. Holds no event content: only a SHA-256 hash of the Graph event ID,
/// so the log can show how often each method wins without storing anything personal.
/// </summary>
public class MatchLog
{
    public int Id { get; set; }

    public required string EventHash { get; set; }

    public DateTime EventStartUtc { get; set; }

    public int? PlaceId { get; set; }

    public Place? Place { get; set; }

    public MatchMethod Method { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
