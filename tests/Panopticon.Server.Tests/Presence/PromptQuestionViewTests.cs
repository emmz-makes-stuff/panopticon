using Panopticon.Contracts;
using Panopticon.Server.Presence;

namespace Panopticon.Server.Tests.Presence;

// Spec remote-answers: "Safe rendering". An option's preview reaches the dashboard rendered as documents are.
public sealed class PromptQuestionViewTests
{
    private static PromptOptionView Rendered(string preview) =>
        Assert.Single(PromptQuestionView.For(new PromptQuestion("Q", "H", [new PromptOption("A", "a", preview)])).Options);

    [Fact]
    public void A_script_in_a_preview_is_text_and_runs_nothing()
    {
        var html = Rendered("<script>alert(1)</script>").PreviewHtml!;

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_renders_and_unsafe_links_lose_their_target()
    {
        var html = Rendered("**Bold** and [bad](javascript:alert(1)) and ![img](https://example.com/x.png)").PreviewHtml!;

        Assert.Contains("<strong>Bold</strong>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript:", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_question_its_options_and_choice_kind_carry_over_and_no_preview_means_none()
    {
        var view = PromptQuestionView.For(new PromptQuestion("Which parts?", "Parts", [new PromptOption("Hooks"), new PromptOption("Web", "The dashboard")], MultiSelect: true));

        Assert.Equal(("Which parts?", "Parts", true), (view.Question, view.Header, view.MultiSelect));
        Assert.Equal([new PromptOptionView("Hooks"), new PromptOptionView("Web", "The dashboard")], view.Options);
    }
}
