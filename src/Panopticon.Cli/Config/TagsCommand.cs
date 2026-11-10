using Panopticon.Contracts;

namespace Panopticon.Cli.Config;

/// <summary>
/// <c>panopticon tags</c>, <c>tags add</c> and <c>tags remove</c> (tags D8; spec cli-bridge, Personal tags in the
/// config). Each edit is all or nothing: a malformed tag, a duplicate, the 21st tag or removing one that isn't saved
/// exits 1 with the reason, and the file is left as it was. The saved login is kept.
/// </summary>
internal sealed class TagsCommand(CliConfigFile config, TextWriter stdout, TextWriter stderr)
{
    private const string Usage = "Usage: panopticon tags [add <tag>... | remove <tag>...]";

    public int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        try
        {
            switch (args)
            {
                case []:
                    return List();
                case ["add", _, ..]:
                    return Edit(saved => Add(saved, args[1..]));
                case ["remove", _, ..]:
                    return Edit(saved => Remove(saved, args[1..]));
                default:
                    stderr.WriteLine(Usage);
                    return 1;
            }
        }
        catch (InvalidDataException ex)
        {
            stderr.WriteLine($"panopticon: {ex.Message}");
            return 1;
        }
        catch (TagEditException ex)
        {
            stderr.WriteLine($"panopticon: {ex.Message} Nothing was changed.");
            return 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"panopticon: saving {config.Path} failed: {ex.Message}");
            return 1;
        }
    }

    private int List()
    {
        var tags = config.ReadStrict()?.Tags ?? [];
        if (tags.Count == 0)
        {
            stdout.WriteLine("No personal tags.");
        }

        foreach (var tag in tags)
        {
            stdout.WriteLine(tag);
        }

        return 0;
    }

    private int Edit(Func<IReadOnlyList<string>, IReadOnlyList<string>> change)
    {
        var written = config.Update(saved => saved with { Tags = change(saved.Tags ?? []) }, startOverIfUnreadable: false);
        foreach (var tag in written.Tags ?? [])
        {
            stdout.WriteLine(tag);
        }

        return 0;
    }

    private static List<string> Add(IReadOnlyList<string> saved, IEnumerable<string> texts)
    {
        var tags = saved.ToList();
        foreach (var text in texts)
        {
            if (!TagText.TryParse(text, out var tag, out var error))
            {
                throw new TagEditException(error);
            }

            if (tags.Any(t => Same(t, tag)))
            {
                throw new TagEditException($"\"{tag.Text}\" is already one of your personal tags.");
            }

            tags.Add(tag.Text);
        }

        if (tags.Count > TagText.MaxPersonalTags)
        {
            throw new TagEditException($"That would make {tags.Count} personal tags, and at most {TagText.MaxPersonalTags} are allowed.");
        }

        return tags;
    }

    private static List<string> Remove(IReadOnlyList<string> saved, IEnumerable<string> texts)
    {
        var tags = saved.ToList();
        foreach (var text in texts)
        {
            // A malformed text can still remove a hand-edited entry that matches it exactly, so a bad file can be fixed.
            var index = TagText.TryParse(text, out var tag, out var error)
                ? tags.FindIndex(t => Same(t, tag))
                : tags.FindIndex(t => string.Equals(t.Trim(), text.Trim(), StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                throw new TagEditException(error ?? $"\"{tag!.Text}\" isn't one of your personal tags.");
            }

            tags.RemoveAt(index);
        }

        return tags;
    }

    // Tags compare without regard to case; a saved text that isn't a tag matches nothing.
    private static bool Same(string saved, Tag tag) => TagText.TryParse(saved, out var parsed, out _) && parsed.Equals(tag);

    private sealed class TagEditException(string message) : Exception(message);
}
