namespace Panopticon.Server.Tests.Infrastructure;

/// <summary>Reads a dashboard event stream line by line, as a browser's <c>EventSource</c> would.</summary>
public static class SseReading
{
    /// <summary>Reads up to the next event of the type, returning its data line.</summary>
    public static async Task<string> NextEventAsync(StreamReader reader, string type, CancellationToken cancellationToken)
    {
        var lines = await LinesUpToEventAsync(reader, type, cancellationToken);
        return lines.Last(l => l.StartsWith("data: ", StringComparison.Ordinal))["data: ".Length..];
    }

    /// <summary>Every line read, up to and including the end of the next event of the type.</summary>
    public static async Task<IReadOnlyList<string>> LinesUpToEventAsync(StreamReader reader, string type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var lines = new List<string>();
        string? eventType = null;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                if (eventType == type)
                {
                    return lines;
                }

                eventType = null;
                continue;
            }

            lines.Add(line);
            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                eventType = line["event: ".Length..];
            }
        }

        throw new InvalidOperationException("The stream ended.");
    }
}
