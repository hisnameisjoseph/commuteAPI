namespace CommuteApi.Models;

/// <summary>How an event was matched to a place. Stored as text in <c>MatchLogs.Method</c>.</summary>
public enum MatchMethod
{
    None,
    Location,
    Title,
    Llm,
}
