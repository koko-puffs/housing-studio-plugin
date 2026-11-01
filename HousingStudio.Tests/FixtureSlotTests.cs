using System.Collections.Generic;
using HousingStudio.Core;
using Xunit;

namespace HousingStudio.Tests;

/// <summary>0.9.1: the fixtures window's slots, worked out from what's set and learnt from you.</summary>
public class FixtureSlotTests
{
    [Fact]
    public void SlotsHoldingWhatsSetAreSeen()
    {
        var current = new[]
        {
            new CurrentFixture("Ground Floor", "Wall", 100),
            new CurrentFixture("Ground Floor", "Floor", 200),
            // The same flooring upstairs: which of its slots is which floor's can't be told.
            new CurrentFixture("Upper Floor", "Floor", 200),
        };
        var container = new Dictionary<int, uint> { [3] = 100, [4] = 200, [7] = 200 };
        var seen = FixtureJobs.SlotsSeen(container, current);
        Assert.Equal(new[] { new FixtureJobs.KnownSlot("Ground Floor", "Wall", 3) }, seen);
    }

    [Fact]
    public void TheOrderThatAgreesGivesTheRest()
    {
        // Part by part, the three parts set here: the ground floor's wall in 0, its light in 6.
        var known = new List<FixtureJobs.KnownSlot> { new("Ground Floor", "Wall", 0), new("Ground Floor", "Light", 6) };
        Assert.Equal(1, FixtureJobs.SlotFor(Side.Interior, "Upper Floor", "Wall", known));
        Assert.Equal(3, FixtureJobs.SlotFor(Side.Interior, "Ground Floor", "Flooring", known));
        Assert.Equal(7, FixtureJobs.SlotFor(Side.Interior, "Upper Floor", "Light", known));
        // Two orders agree with a wall in 0 and a floor in 3 (five parts floor by floor, three part by
        // part), and they differ upstairs: no slot.
        var both = new List<FixtureJobs.KnownSlot> { new("Ground Floor", "Wall", 0), new("Ground Floor", "Floor", 3) };
        Assert.Null(FixtureJobs.SlotFor(Side.Interior, "Upper Floor", "Wall", both));
        // What's known wins over any order.
        Assert.Equal(9, FixtureJobs.SlotFor(Side.Interior, "Upper Floor", "Wall", new List<FixtureJobs.KnownSlot>(known) { new("Upper Floor", "Wall", 9) }));
    }

    [Fact]
    public void NothingKnownOrOrdersThatDisagreeGiveNoSlot()
    {
        Assert.Null(FixtureJobs.SlotFor(Side.Interior, "Ground Floor", "Wall", new List<FixtureJobs.KnownSlot>()));
        // Slot 0 for the ground floor's wall: every order starting with the ground floor agrees, and they
        // differ on the upper floor's light.
        var one = new List<FixtureJobs.KnownSlot> { new("Ground Floor", "Wall", 0) };
        Assert.Null(FixtureJobs.SlotFor(Side.Interior, "Upper Floor", "Light", one));
        // No order puts the wall in slot 13.
        var odd = new List<FixtureJobs.KnownSlot> { new("Ground Floor", "Wall", 13) };
        Assert.Null(FixtureJobs.SlotFor(Side.Interior, "Upper Floor", "Wall", odd));
    }

    [Fact]
    public void KeptSlotsComeBackBySide()
    {
        var kept = new Dictionary<string, int>
        {
            [FixtureJobs.SlotKey(Side.Interior, "Ground Floor", "Wall")] = 2,
            [FixtureJobs.SlotKey(Side.Garden, "Exterior", "Roof")] = 0,
        };
        Assert.Equal(new[] { new FixtureJobs.KnownSlot("Ground Floor", "Wall", 2) }, FixtureJobs.KeptSlots(kept, Side.Interior));
        Assert.Equal(new[] { new FixtureJobs.KnownSlot("Exterior", "Roof", 0) }, FixtureJobs.KeptSlots(kept, Side.Garden));
    }
}
