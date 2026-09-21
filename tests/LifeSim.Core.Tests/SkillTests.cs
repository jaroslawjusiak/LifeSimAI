using FluentAssertions;
using LifeSim.Core.Entities;
using Xunit;

namespace LifeSim.Core.Tests;

public class SkillTests
{
    private static SkillDef Cooking => new("cooking", "Cooking", [100, 250], []);

    [Fact]
    public void AddXp_LevelsUp_AtThreshold()
    {
        var skill = new Skill(Cooking);

        var gained = skill.AddXp(100);

        skill.Level.Should().Be(2);
        gained.Should().Equal(2);
    }

    [Fact]
    public void AddXp_GainsMultipleLevels_OnLargeAmount()
    {
        var skill = new Skill(Cooking);

        var gained = skill.AddXp(1000);

        skill.Level.Should().Be(3);
        skill.Xp.Should().Be(1000);
        gained.Should().Equal(2, 3);
    }

    [Fact]
    public void AddXp_NeverExceedsMaxLevel()
    {
        var skill = new Skill(Cooking);

        skill.AddXp(1_000_000);

        skill.Level.Should().Be(3);
    }

    [Fact]
    public void AddXp_Negative_Throws()
    {
        var skill = new Skill(Cooking);
        var act = () => skill.AddXp(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SkillSet_LevelOf_ReturnsZeroForUnknown()
    {
        var set = new SkillSet([Cooking]);

        set.LevelOf("cooking").Should().Be(0);
    }

    [Fact]
    public void SkillSet_AddXp_MakesSkillKnown()
    {
        var set = new SkillSet([Cooking]);

        set.IsKnown("cooking").Should().BeFalse();

        set.AddXp("cooking", 50);

        set.IsKnown("cooking").Should().BeTrue();
        set.LevelOf("cooking").Should().Be(1);
    }

    [Fact]
    public void SkillSet_AddXp_UnknownSkill_Throws()
    {
        var set = new SkillSet([Cooking]);
        var act = () => set.AddXp("nope", 10);

        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void SkillSet_MeetsPrerequisites()
    {
        var set = new SkillSet(
        [
            Cooking,
            new SkillDef("gourmet", "Gourmet", [500], [new SkillPrerequisite("cooking", 2)]),
        ]);

        set.MeetsPrerequisites("gourmet").Should().BeFalse();

        set.AddXp("cooking", 100); // reaches level 2

        set.MeetsPrerequisites("gourmet").Should().BeTrue();
    }
}
