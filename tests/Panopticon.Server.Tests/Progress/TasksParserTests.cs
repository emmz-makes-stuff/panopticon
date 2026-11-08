using Panopticon.Server.Progress;

namespace Panopticon.Server.Tests.Progress;

// One test per scenario in specs/task-progress/spec.md, plus edge cases of the checkbox rule.
public class TasksParserTests
{
    // --- Requirement: Sections ---

    [Fact]
    public void Numbered_headings_become_numbered_sections()
    {
        var parsed = TasksParser.Parse("""
            # Tasks

            ## 1. Setup

            - [ ] 1.1 a

            ## 2. Core Implementation

            - [ ] 2.1 b
            """);

        Assert.Collection(
            parsed.Sections,
            s => Assert.Equal((1, "Setup"), (s.Number, s.Title)),
            s => Assert.Equal((2, "Core Implementation"), (s.Number, s.Title)));
    }

    [Fact]
    public void Unnumbered_heading_is_numbered_by_position()
    {
        var parsed = TasksParser.Parse("""
            ## 1. Setup
            ## 2. Core
            ## Workflow follow-up
            """);

        var third = parsed.Sections[2];
        Assert.Equal(3, third.Number);
        Assert.Equal("Workflow follow-up", third.Title);
    }

    [Fact]
    public void Level_one_and_three_headings_do_not_start_sections()
    {
        var parsed = TasksParser.Parse("""
            # Tasks
            ## 1. Only
            ### Sub-heading
            - [x] a
            """);

        var only = Assert.Single(parsed.Sections);
        Assert.Equal(new TaskProgress(1, 1), only.Progress);
    }

    [Fact]
    public void Heading_with_inline_code_keeps_its_text()
    {
        var parsed = TasksParser.Parse("## 3. Build the `web/` app");

        Assert.Equal("Build the web/ app", Assert.Single(parsed.Sections).Title);
    }

    // --- Requirement: Task counting ---

    [Fact]
    public void Done_markers_count_lowercase_uppercase_and_spaced_x()
    {
        var parsed = TasksParser.Parse("""
            ## 1. S
            - [x] a
            - [X] b
            - [ x] c
            - [ ] d
            """);

        Assert.Equal(new TaskProgress(3, 4), parsed.Sections[0].Progress);
    }

    [Fact]
    public void Other_markers_are_unfinished()
    {
        var parsed = TasksParser.Parse("""
            ## 1. S
            - [~] a
            - [-] b
            - [] c
            """);

        Assert.Equal(new TaskProgress(0, 3), parsed.Sections[0].Progress);
    }

    [Fact]
    public void Plain_bullets_are_not_tasks()
    {
        var parsed = TasksParser.Parse("""
            ## 1. S
            - [ ] real task
            - Archive the change
            """);

        Assert.Equal(new TaskProgress(0, 1), parsed.Sections[0].Progress);
    }

    [Theory]
    [InlineData("- [docs](https://example.com) are linked")]
    [InlineData("- [TODO] not a checkbox")]
    [InlineData("- [x]no-space-after-box")]
    public void Brackets_that_are_not_checkboxes_are_not_tasks(string line)
    {
        var parsed = TasksParser.Parse("## 1. S\n" + line);

        Assert.Equal(TaskProgress.Empty, parsed.Sections[0].Progress);
    }

    [Fact]
    public void Nested_checkbox_items_are_counted()
    {
        var parsed = TasksParser.Parse("""
            ## 1. S
            - [x] parent
              - [ ] child
            """);

        Assert.Equal(new TaskProgress(1, 2), parsed.Sections[0].Progress);
    }

    [Fact]
    public void Ordered_list_checkboxes_are_counted()
    {
        var parsed = TasksParser.Parse("""
            ## 1. S
            1. [x] first
            2. [ ] second
            """);

        Assert.Equal(new TaskProgress(1, 2), parsed.Sections[0].Progress);
    }

    // --- Requirement: Overall progress ---

    [Fact]
    public void Overall_sums_every_section()
    {
        var parsed = TasksParser.Parse("""
            ## 1. A
            - [x] a
            - [x] b
            - [x] c
            - [x] d
            ## 2. B
            - [x] e
            - [ ] f
            - [ ] g
            - [ ] h
            - [ ] i
            - [ ] j
            """);

        Assert.Equal(new TaskProgress(5, 10), parsed.Overall);
    }

    [Fact]
    public void Tasks_before_the_first_section_count_only_overall()
    {
        var parsed = TasksParser.Parse("""
            - [x] preamble task
            ## 1. A
            - [ ] a
            """);

        Assert.Equal(new TaskProgress(1, 2), parsed.Overall);
        Assert.Equal(new TaskProgress(0, 1), parsed.Sections[0].Progress);
    }

    [Fact]
    public void No_tasks_is_zero_of_zero_not_an_error()
    {
        var parsed = TasksParser.Parse("# Tasks\n\nNothing to do yet.\n");

        Assert.Equal(TaskProgress.Empty, parsed.Overall);
        Assert.True(parsed.Overall.IsEmpty);
    }

    [Fact]
    public void Empty_document_parses()
    {
        var parsed = TasksParser.Parse(string.Empty);

        Assert.Empty(parsed.Sections);
        Assert.Equal(TaskProgress.Empty, parsed.Overall);
    }

    // --- Requirement: Untrusted content ---

    [Fact]
    public void Html_in_a_task_is_counted_and_does_not_leak()
    {
        var parsed = TasksParser.Parse("""
            ## 1. S
            - [ ] <script>alert(1)</script>
            - [x] fine
            """);

        Assert.Equal(new TaskProgress(1, 2), parsed.Sections[0].Progress);
        // Task text never reaches the parsed result; section titles are the only strings carried.
        Assert.DoesNotContain(parsed.Sections, s => s.Title.Contains("<script", StringComparison.Ordinal));
    }

    [Fact]
    public void Html_in_a_heading_stays_literal_text()
    {
        var parsed = TasksParser.Parse("## 1. <b>Bold</b> move");

        // HTML is disabled: tags are kept as plain text, which views render as text, never markup.
        Assert.Equal("<b>Bold</b> move", Assert.Single(parsed.Sections).Title);
    }

    [Fact]
    public void Oversized_file_is_rejected_with_the_limit()
    {
        var oversized = new string('a', TasksParser.MaxBytes + 1);

        var ex = Assert.Throws<TasksDocumentTooLargeException>(() => TasksParser.Parse(oversized));

        Assert.Contains("1 MiB", ex.Message, StringComparison.Ordinal);
        Assert.Equal(TasksParser.MaxBytes + 1, ex.ActualBytes);
    }

    [Fact]
    public void Exactly_one_mebibyte_is_accepted()
    {
        var atLimit = new string('a', TasksParser.MaxBytes);

        var parsed = TasksParser.Parse(atLimit);

        Assert.Equal(TaskProgress.Empty, parsed.Overall);
    }

    [Fact]
    public void Limit_counts_utf8_bytes_not_characters()
    {
        // 'é' is two bytes in UTF-8, so half a MiB of them plus one is over the limit.
        var multibyte = new string('é', (TasksParser.MaxBytes / 2) + 1);

        Assert.Throws<TasksDocumentTooLargeException>(() => TasksParser.Parse(multibyte));
    }
}
