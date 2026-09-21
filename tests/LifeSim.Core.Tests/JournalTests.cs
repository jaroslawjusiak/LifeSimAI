using FluentAssertions;
using LifeSim.Core.Journal;
using LifeSim.Core.Time;
using Xunit;

namespace LifeSim.Core.Tests;

public class JournalTests
{
    private static GameClock T(int day, int hour = 0) => new(day, hour, 0);

    [Fact]
    public void Append_AssignsIncrementingSequenceNumbers()
    {
        var journal = new EventJournal();

        var a = journal.Append("c1", T(0), "A");
        var b = journal.Append("c2", T(0), "B");
        var c = journal.Append("c3", T(1), "A");

        a.Seq.Should().Be(1);
        b.Seq.Should().Be(2);
        c.Seq.Should().Be(3);
    }

    [Fact]
    public void Last_ReturnsMostRecentN_InAscendingOrder()
    {
        var journal = new EventJournal();
        for (var i = 0; i < 10; i++)
        {
            journal.Append("c", T(0), "T");
        }

        var last = journal.Last(3);

        last.Should().HaveCount(3);
        last[0].Seq.Should().Be(8);
        last[2].Seq.Should().Be(10);
    }

    [Fact]
    public void ByType_FiltersEntries()
    {
        var journal = new EventJournal();
        journal.Append("c", T(0), "ActionResolved");
        journal.Append("c", T(0), "StageTransition");
        journal.Append("c", T(0), "ActionResolved");

        journal.ByType("ActionResolved").Should().HaveCount(2);
    }

    [Fact]
    public void SinceDay_FiltersByDayIndex()
    {
        var journal = new EventJournal();
        journal.Append("c", T(0), "A");
        journal.Append("c", T(1), "A");
        journal.Append("c", T(2), "A");

        journal.SinceDay(1).Should().HaveCount(2);
    }

    [Fact]
    public void Pinned_And_Rolling_AreClassified()
    {
        var journal = new EventJournal();
        journal.Append("c", T(0), "PinnedFact", isPinned: true);
        journal.Append("c", T(0), "ActionResolved");
        journal.Append("c", T(0), "PinnedFact", isPinned: true);

        journal.Pinned().Should().HaveCount(2);
        journal.Rolling().Should().HaveCount(1);
    }
}
