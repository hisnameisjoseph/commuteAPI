using System.Text.RegularExpressions;

namespace CommuteApi.Tests.Infrastructure;

public static partial class FormPosting
{
    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="([^"]+)"|value="([^"]+)"[^>]*name="__RequestVerificationToken" """)]
    private static partial Regex TokenPattern();

    /// <summary>
    /// GETs a form page and posts back to <paramref name="postUrl"/> the way a browser would:
    /// with the antiforgery cookie (kept by the client) and the hidden form token.
    /// </summary>
    public static async Task<HttpResponseMessage> PostFormAsync(
        this HttpClient client,
        string formUrl,
        string postUrl,
        IDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(fields);

        var html = await client.GetStringAsync(new Uri(formUrl, UriKind.Relative));
        var match = TokenPattern().Match(html);
        Assert.True(match.Success, $"No antiforgery token found on {formUrl}.");

        var token = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        var body = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = token };

        using var content = new FormUrlEncodedContent(body);
        return await client.PostAsync(new Uri(postUrl, UriKind.Relative), content);
    }
}
