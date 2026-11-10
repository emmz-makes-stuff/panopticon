using Panopticon.Contracts;

namespace Panopticon.Cli.Tests;

// Spec tags, "Tag syntax" (tags D1). The CLI and the server share this parser, so it's tested once, here.
public class TagTextTests
{
    [Fact]
    public void A_key_and_value_are_trimmed_and_equal_without_regard_to_case()
    {
        Assert.True(TagText.TryParse("Area:  Onboarding ", out var tag, out _));

        Assert.Equal("Area", tag.Key);
        Assert.Equal("Onboarding", tag.Value);
        Assert.Equal("Area: Onboarding", tag.Text);
        Assert.True(TagText.TryParse("area: onboarding", out var lower, out _));
        Assert.Equal(lower, tag);
        Assert.Equal(lower.GetHashCode(), tag.GetHashCode());
    }

    [Fact]
    public void A_bare_key_has_no_value()
    {
        Assert.True(TagText.TryParse(" dev ", out var tag, out _));

        Assert.Equal("dev", tag.Key);
        Assert.Null(tag.Value);
        Assert.Equal(string.Empty, tag.ValueKey);
        Assert.Equal("dev", tag.Text);
    }

    [Fact]
    public void The_first_colon_splits_key_from_value()
    {
        Assert.True(TagText.TryParse("link: http://x", out var tag, out _));

        Assert.Equal("link", tag.Key);
        Assert.Equal("http://x", tag.Value);
    }

    [Fact]
    public void A_bare_key_and_the_same_key_with_a_value_differ()
    {
        Assert.True(TagText.TryParse("language", out var bare, out _));
        Assert.True(TagText.TryParse("language: csharp", out var valued, out _));

        Assert.NotEqual(bare, valued);
    }

    [Fact]
    public void Keys_take_letters_digits_dashes_underscores_and_dots()
    {
        Assert.True(TagText.TryParse("a-b_c.9", out var tag, out _));
        Assert.Equal("a-b_c.9", tag.Key);
    }

    [Theory]
    [InlineData("has space", "a key can only have letters, digits")]
    [InlineData(": value", "needs a key before the ':'")]
    [InlineData("key:", "has no value after the ':'")]
    [InlineData("key:   ", "has no value after the ':'")]
    [InlineData("", "can't be empty")]
    [InlineData("   ", "can't be empty")]
    [InlineData("café", "a key can only have letters, digits")]
    [InlineData("key: two\nlines", "can't contain a line break")]
    [InlineData("key: two\rlines", "can't contain a line break")]
    public void Malformed_tags_are_refused_with_a_reason(string text, string reason)
    {
        Assert.False(TagText.TryParse(text, out var tag, out var error));

        Assert.Null(tag);
        Assert.Contains(reason, error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_error_names_the_tag()
    {
        Assert.False(TagText.TryParse("has space", out _, out var error));
        Assert.StartsWith("\"has space\":", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_part_may_be_up_to_40_characters()
    {
        var forty = new string('k', 40);

        Assert.True(TagText.TryParse($"{forty}: {new string('v', 40)}", out _, out _));
        Assert.False(TagText.TryParse(forty + "k", out _, out var keyError));
        Assert.Contains("a key can be at most 40 characters", keyError, StringComparison.Ordinal);
        Assert.False(TagText.TryParse($"k: {new string('v', 41)}", out _, out var valueError));
        Assert.Contains("a value can be at most 40 characters", valueError, StringComparison.Ordinal);
    }

    [Fact]
    public void Parsing_a_list_collapses_duplicates_keeping_the_first_casing()
    {
        Assert.True(TagText.TryParseAll(["Dev", "dev", "urgent"], TagText.MaxPersonalTags, out var tags, out _));

        Assert.Equal(["Dev", "urgent"], tags.Select(t => t.Text));
    }

    [Fact]
    public void Parsing_a_list_refuses_one_malformed_tag()
    {
        Assert.False(TagText.TryParseAll(["dev", "has space"], TagText.MaxPersonalTags, out var tags, out var error));

        Assert.Null(tags);
        Assert.Contains("\"has space\"", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parsing_a_list_refuses_more_than_the_limit()
    {
        var texts = Enumerable.Range(1, 21).Select(i => $"t{i}");

        Assert.False(TagText.TryParseAll(texts, TagText.MaxPersonalTags, out _, out var error));
        Assert.Contains("at most 20", error, StringComparison.Ordinal);
    }
}
