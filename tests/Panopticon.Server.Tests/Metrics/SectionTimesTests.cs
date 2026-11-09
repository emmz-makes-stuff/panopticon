using Panopticon.Server.Data;
using Panopticon.Server.Metrics;
using Panopticon.Server.Progress;

namespace Panopticon.Server.Tests.Metrics;

// Spec change-metrics: Time to archive, Time per section, Sections not measured.
public class SectionTimesTests
{
    private static readonly DateTimeOffset _day = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Archived_change_took_its_first_report_to_its_archive()
    {
        var first = new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);
        var archived = new DateTimeOffset(2026, 11, 8, 15, 0, 0, TimeSpan.Zero);

        Assert.Equal(new TimeSpan(5, 6, 0, 0), SectionTimes.ToArchive(first, archived, now: archived.AddDays(3)));
    }

    [Fact]
    public void Active_change_has_run_from_its_first_report_until_now()
    {
        var now = _day.AddDays(10);

        Assert.Equal(TimeSpan.FromDays(2), SectionTimes.ToArchive(now.AddDays(-2), archivedAt: null, now));
    }

    [Fact]
    public void A_finished_section_runs_from_being_current_to_staying_all_ticked()
    {
        var times = SectionTimes.Compute([
            Event(At(9, 0), "## 1. Setup\n- [x] a\n## 2. Core\n- [ ] b\n- [ ] c", reportedSection: 1),
            Event(At(10, 0), "## 1. Setup\n- [x] a\n## 2. Core\n- [ ] b\n- [ ] c", reportedSection: 2),
            Event(At(12, 0), "## 1. Setup\n- [x] a\n## 2. Core\n- [x] b\n- [ ] c", reportedSection: 2),
            Event(At(13, 30), "## 1. Setup\n- [x] a\n## 2. Core\n- [x] b\n- [x] c", reportedSection: 2),
            Event(At(15, 0), "## 1. Setup\n- [x] a\n## 2. Core\n- [x] b\n- [x] c", reportedSection: null),
        ]);

        var core = Assert.Single(times, t => t.Number == 2);
        Assert.Equal(("Core", At(10, 0), At(13, 30)), (core.Title, core.StartedAt, core.FinishedAt));
        Assert.False(core.InProgress);
        Assert.Equal(new TimeSpan(3, 30, 0), core.Duration(now: At(23, 0)));
    }

    [Fact]
    public void A_started_unfinished_section_is_in_progress_with_its_time_so_far()
    {
        var now = At(16, 0);
        var times = SectionTimes.Compute([
            Event(At(15, 0), "## 3. Web\n- [x] a\n- [ ] b\n- [ ] c\n- [ ] d", reportedSection: 3),
            Event(At(15, 40), "## 3. Web\n- [x] a\n- [x] b\n- [ ] c\n- [ ] d", reportedSection: 3),
        ]);

        var web = Assert.Single(times);
        Assert.True(web.InProgress);
        Assert.Null(web.FinishedAt);
        Assert.Equal(TimeSpan.FromHours(1), web.Duration(now));
    }

    [Fact]
    public void A_ticked_task_starts_a_section_that_was_never_current()
    {
        var times = SectionTimes.Compute([
            Event(At(9, 0), "## 1. Setup\n- [ ] a\n## 2. Core\n- [ ] b", reportedSection: 1),
            Event(At(9, 30), "## 1. Setup\n- [ ] a\n## 2. Core\n- [x] b", reportedSection: 1),
        ]);

        var core = Assert.Single(times, t => t.Number == 2);
        Assert.Equal((At(9, 30), At(9, 30)), (core.StartedAt, core.FinishedAt));
    }

    [Fact]
    public void A_reopened_section_finishes_when_it_is_all_ticked_for_good()
    {
        var times = SectionTimes.Compute([
            Event(At(10, 0), "## 1. Setup\n- [x] a\n- [ ] b", reportedSection: 1),
            Event(At(11, 0), "## 1. Setup\n- [x] a\n- [x] b", reportedSection: 1),
            Event(At(12, 0), "## 1. Setup\n- [x] a\n- [ ] b", reportedSection: 1),
            Event(At(14, 0), "## 1. Setup\n- [x] a\n- [x] b", reportedSection: 1),
            Event(At(15, 0), "## 1. Setup\n- [x] a\n- [x] b", reportedSection: null),
        ]);

        Assert.Equal(At(14, 0), Assert.Single(times).FinishedAt);
    }

    [Fact]
    public void A_section_unticked_in_the_last_report_is_back_in_progress()
    {
        var times = SectionTimes.Compute([
            Event(At(10, 0), "## 1. Setup\n- [x] a", reportedSection: 1),
            Event(At(11, 0), "## 1. Setup\n- [x] a\n- [ ] b", reportedSection: 1),
        ]);

        Assert.True(Assert.Single(times).InProgress);
    }

    [Fact]
    public void A_section_never_started_or_with_no_tasks_has_no_time()
    {
        var times = SectionTimes.Compute([
            Event(At(9, 0), "## 1. Setup\n- [x] a\n## 4. Notes\n## 5. Later\n- [ ] z", reportedSection: 1),
            Event(At(10, 0), "## 1. Setup\n- [x] a\n## 4. Notes\n## 5. Later\n- [ ] z", reportedSection: 4),
        ]);

        Assert.Equal([1], times.Select(t => t.Number));
    }

    [Fact]
    public void Unnumbered_sections_are_not_measured()
    {
        var times = SectionTimes.Compute([
            Event(At(9, 0), "## 1. Setup\n- [x] a\n## Workflow follow-up\n- [x] review\n- [ ] archive", reportedSection: null),
        ]);

        Assert.Equal([1], times.Select(t => t.Number));
    }

    [Fact]
    public void Sections_come_back_in_number_order_with_their_latest_title()
    {
        var times = SectionTimes.Compute([
            Event(At(9, 0), "## 2. Core\n- [x] b\n## 1. Setup\n- [x] a", reportedSection: null),
            Event(At(10, 0), "## 2. Core server\n- [x] b\n## 1. Setup\n- [x] a", reportedSection: null),
        ]);

        Assert.Equal([(1, "Setup"), (2, "Core server")], times.Select(t => (t.Number, t.Title)));
    }

    [Fact]
    public void Reports_with_unknown_progress_are_skipped()
    {
        var unreadable = Event(At(12, 0), "## 1. Setup\n- [x] a\n- [ ] b", reportedSection: 1);
        ReportProgress.MarkUnreadable(unreadable);
        var pending = new ReportEvent { TasksSha256 = "", ReceivedAt = At(13, 0) };

        var times = SectionTimes.Compute([
            Event(At(10, 0), "## 1. Setup\n- [ ] a\n- [ ] b", reportedSection: 1),
            Event(At(11, 0), "## 1. Setup\n- [x] a\n- [x] b", reportedSection: 1),
            unreadable,
            pending,
        ]);

        var setup = Assert.Single(times);
        Assert.Equal((At(10, 0), At(11, 0)), (setup.StartedAt, setup.FinishedAt));
    }

    [Fact]
    public void No_readable_reports_means_no_sections()
    {
        Assert.Empty(SectionTimes.Compute([new ReportEvent { TasksSha256 = "", ReceivedAt = _day }]));
    }

    [Fact]
    public void Reports_are_walked_in_the_order_received()
    {
        var times = SectionTimes.Compute([
            Event(At(14, 0), "## 1. Setup\n- [x] a\n- [x] b", reportedSection: 1),
            Event(At(10, 0), "## 1. Setup\n- [x] a\n- [ ] b", reportedSection: 1),
        ]);

        var setup = Assert.Single(times);
        Assert.Equal((At(10, 0), At(14, 0)), (setup.StartedAt, setup.FinishedAt));
    }

    private static DateTimeOffset At(int hour, int minute) => _day.AddHours(hour).AddMinutes(minute);

    private static ReportEvent Event(DateTimeOffset at, string tasks, int? reportedSection)
    {
        var parsed = TasksParser.Parse(tasks);
        var reportEvent = new ReportEvent { TasksSha256 = "", ReportedSection = reportedSection, ReceivedAt = at };
        ReportProgress.Fill(reportEvent, parsed, CurrentSection.Select(parsed, reportedSection));
        return reportEvent;
    }
}
