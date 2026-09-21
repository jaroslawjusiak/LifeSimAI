using FluentAssertions;
using LifeSim.Core.Entities;
using Xunit;

namespace LifeSim.Core.Tests;

public class RelationshipTests
{
    [Fact]
    public void Value_ClampsAtBounds()
    {
        var relationship = new Relationship(0);

        relationship.ApplyDelta(100_000);
        relationship.Value.Should().Be(100);

        relationship.ApplyDelta(-100_000);
        relationship.Value.Should().Be(-100);
    }

    [Fact]
    public void ApplyDelta_FiresMilestone_OncePerThresholdCrossed()
    {
        var relationship = new Relationship(0);
        var milestones = new List<int>();
        relationship.MilestoneReached += e => milestones.Add(e.Milestone);

        relationship.ApplyDelta(80); // 0 -> 80, crosses 25, 50, 75

        milestones.Should().Equal(25, 50, 75);
    }

    [Fact]
    public void ApplyDelta_WithinTier_DoesNotFire()
    {
        var relationship = new Relationship(30);
        var fired = 0;
        relationship.MilestoneReached += _ => fired++;

        relationship.ApplyDelta(10); // 30 -> 40
        relationship.ApplyDelta(-5); // 40 -> 35

        fired.Should().Be(0);
    }

    [Fact]
    public void ApplyDelta_FiresNegativeMilestones()
    {
        var relationship = new Relationship(0);
        var milestones = new List<int>();
        relationship.MilestoneReached += e => milestones.Add(e.Milestone);

        relationship.ApplyDelta(-60); // 0 -> -60, crosses -25, -50

        milestones.Should().Equal(-25, -50);
    }

    [Fact]
    public void ApplyDelta_CrossingDown_FiresAgain()
    {
        var relationship = new Relationship(80);
        var milestones = new List<int>();
        relationship.MilestoneReached += e => milestones.Add(e.Milestone);

        relationship.ApplyDelta(-60); // 80 -> 20, falls back below 75, 50, 25

        milestones.Should().Equal(25, 50, 75);
    }

    [Fact]
    public void ApplyDelta_ReportsValue_WithEvent()
    {
        var relationship = new Relationship(20);
        RelationshipMilestoneEventArgs? captured = null;
        relationship.MilestoneReached += e => captured = e;

        relationship.ApplyDelta(10); // 20 -> 30, crosses 25

        captured!.Milestone.Should().Be(25);
        captured.Value.Should().Be(30);
    }
}
