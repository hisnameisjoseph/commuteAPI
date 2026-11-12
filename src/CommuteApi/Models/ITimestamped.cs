namespace CommuteApi.Models;

/// <summary>Entities whose audit timestamps are set by <see cref="Data.TimestampInterceptor"/>.</summary>
public interface ITimestamped
{
    DateTime CreatedAtUtc { get; set; }

    DateTime UpdatedAtUtc { get; set; }
}
