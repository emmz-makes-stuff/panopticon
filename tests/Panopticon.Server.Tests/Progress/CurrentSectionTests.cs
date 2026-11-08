using Panopticon.Server.Progress;

namespace Panopticon.Server.Tests.Progress;

// The Current section scenarios in specs/task-progress/spec.md.
public class CurrentSectionTests
{
    private static readonly ParsedTasks _fourSections = TasksParser.Parse("""
        ## 1. A
        - [x] a
        ## 2. B
        - [ ] b
        ## 3. C
        - [ ] c
        ## 4. D
        - [ ] d
        """);

    [Fact]
    public void Reported_section_wins()
    {
        var current = CurrentSection.Select(_fourSections, reportedSection: 3);

        Assert.Equal(3, current?.Number);
    }

    [Theory]
    [InlineData("## 1. Setup\n- [x] a\n## Notes\n- [ ] n\n## 2. Core\n- [ ] c\n")]
    [InlineData("## 2. Core\n- [ ] c\n## Notes\n- [ ] n\n")]
    public void Explicit_number_is_preferred_over_a_position_number(string markdown)
    {
        var tasks = TasksParser.Parse(markdown);

        var current = CurrentSection.Select(tasks, reportedSection: 2);

        Assert.Equal((2, "Core", true), (current?.Number, current?.Title, current?.IsNumbered));
    }

    [Fact]
    public void Position_numbered_section_is_still_found_by_its_position()
    {
        var tasks = TasksParser.Parse("## 1. Setup\n- [x] a\n## Workflow follow-up\n- [ ] w\n");

        var current = CurrentSection.Select(tasks, reportedSection: 2);

        Assert.Equal((2, "Workflow follow-up", false), (current?.Number, current?.Title, current?.IsNumbered));
    }

    [Fact]
    public void Unknown_reported_section_falls_back_to_first_unfinished()
    {
        var current = CurrentSection.Select(_fourSections, reportedSection: 9);

        Assert.Equal(2, current?.Number);
    }

    [Fact]
    public void Nothing_reported_picks_first_unfinished()
    {
        var current = CurrentSection.Select(_fourSections, reportedSection: null);

        Assert.Equal(2, current?.Number);
    }

    [Fact]
    public void All_done_picks_last_section_with_tasks()
    {
        var parsed = TasksParser.Parse("""
            ## 1. A
            - [x] a
            ## 2. B
            - [x] b
            ## Workflow follow-up
            - Archive the change.
            """);

        var current = CurrentSection.Select(parsed, reportedSection: null);

        Assert.Equal(2, current?.Number);
    }

    [Fact]
    public void Section_without_tasks_is_skipped_when_choosing_first_unfinished()
    {
        var parsed = TasksParser.Parse("""
            ## 1. Notes
            Some prose.
            ## 2. Work
            - [ ] a
            """);

        var current = CurrentSection.Select(parsed, reportedSection: null);

        Assert.Equal(2, current?.Number);
    }

    [Fact]
    public void No_tasks_anywhere_has_no_current_section()
    {
        var parsed = TasksParser.Parse("## 1. Empty");

        Assert.Null(CurrentSection.Select(parsed, reportedSection: null));
    }
}
