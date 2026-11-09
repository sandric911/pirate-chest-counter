using PirateChest.Core.Counting;
using PirateChest.Core.Trace;
using static PirateChest.Core.Tests.TraceBuilder;

namespace PirateChest.Core.Tests;

public class CountingRulesTests
{
    private static MatchCount Count(TraceBuilder b, IChestLocator? locator = null) =>
        new MatchCounter(locator: locator).Count(b.Build());

    // --- Rule 3: who opened it -------------------------------------------------------------

    [Fact]
    public void OpenedByMe_CountsForMeAndSquad()
    {
        var c = Count(new TraceBuilder().Appear(10, "A").Open(60, "A", MyPawn));
        Assert.Equal(1, c.Mine);
        Assert.Equal(1, c.MineAndSquad);
        Assert.Equal(CountKind.Opened, c.Credits.Single().Kind);
        Assert.Equal(Attribution.Replay, c.Credits.Single().Method);
    }

    [Fact]
    public void OpenedByTeammate_CountsOnlyForSquad()
    {
        var c = Count(new TraceBuilder().Appear(10, "A").Open(60, "A", MatePawn));
        Assert.Equal(0, c.Mine);
        Assert.Equal(1, c.MineAndSquad);
        Assert.Equal(Who.Teammate, c.Credits.Single().By);
    }

    [Fact]
    public void OpenedByStranger_CountsNowhere()
    {
        var c = Count(new TraceBuilder().Appear(10, "A").Open(60, "A", OtherPawn));
        Assert.Equal(0, c.MineAndSquad);
        Assert.Equal(1, c.ForeignChests);
    }

    [Fact]
    public void OpenedByUnresolvablePawn_CountsNowhere()
    {
        var c = Count(new TraceBuilder().Appear(10, "A").Open(60, "A", GhostPawn));
        Assert.Equal(0, c.MineAndSquad);
    }

    // --- Rules 4 and 5: broken ---------------------------------------------------------------

    [Fact]
    public void BrokenUnopenedByMe_CountsAsOpened()
    {
        var c = Count(new TraceBuilder().Appear(10, "A").BreakUnopened(60, "A", MyPawn).Close(65, "A", CloseReason.Destroyed));
        Assert.Equal(1, c.Mine);
        Assert.Equal(1, c.MineBroken);
        Assert.Equal(CountKind.BrokenUnopened, c.Credits.Single().Kind);
    }

    [Fact]
    public void BrokenUnopenedByTeammate_CountsForSquadOnly()
    {
        var c = Count(new TraceBuilder().Appear(10, "A").BreakUnopened(60, "A", MatePawn));
        Assert.Equal(0, c.Mine);
        Assert.Equal(1, c.MineAndSquad);
        Assert.Equal(1, c.SquadBroken);
    }

    [Fact]
    public void BrokenUnopenedByStranger_CountsNowhere()
    {
        var c = Count(new TraceBuilder().Appear(10, "A").BreakUnopened(60, "A", OtherPawn));
        Assert.Equal(0, c.MineAndSquad);
    }

    [Fact]
    public void SearchWithoutFlag_ThenChannelDestroyedWithinWindow_IsBrokenUnopened()
    {
        // Seen in replays: the search arrives without bAlreadySearched/bDestroyed, the close 5 s later.
        var b = new TraceBuilder().Appear(10, "A")
            .Update(60, "A", "Tiered_Chest_Athena_C", e => { e.SearchCount = 1; e.SearchingPawn = MyPawn; })
            .Close(65, "A", CloseReason.Destroyed);
        var c = Count(b);
        Assert.Equal(1, c.Mine);
        Assert.Equal(CountKind.BrokenUnopened, c.Credits.Single().Kind);
    }

    [Fact]
    public void SearchWithoutFlag_NotDestroyed_IsOpened()
    {
        var b = new TraceBuilder().Appear(10, "A")
            .Update(60, "A", "Tiered_Chest_Athena_C", e => { e.SearchCount = 1; e.SearchingPawn = MyPawn; })
            .Update(90, "A", "Tiered_Chest_Athena_C", e => e.Health = 300);
        var c = Count(b);
        Assert.Equal(1, c.Mine);
        Assert.Equal(CountKind.Opened, c.Credits.Single().Kind);
    }

    [Fact]
    public void BreakingAChestOpenedByMe_DoesNotCountTwice()
    {
        var c = Count(new TraceBuilder().Appear(10, "A").Open(60, "A", MyPawn).Destroy(80, "A").Close(85, "A", CloseReason.Destroyed));
        Assert.Equal(1, c.Mine);
        Assert.Equal(CountKind.Opened, c.Credits.Single().Kind);
        Assert.Equal(1, c.BrokenAfterOpen);
    }

    [Fact]
    public void BreakingAChestOpenedByStranger_DoesNotCount_EvenIfIAmRightThere()
    {
        // Rule 5 — the main requirement: breaking already opened chests never counts.
        var b = new TraceBuilder().Appear(10, "A").Open(60, "A", OtherPawn)
            .Pawn(MyPawn, 79, 0, 0).Pawn(MyPawn, 80, 0, 0).Pawn(MyPawn, 81, 0, 0)
            .Destroy(80, "A");
        var c = Count(b, new FixedLocator("A", 0, 0));
        Assert.Equal(0, c.MineAndSquad);
        Assert.Equal(1, c.BrokenAfterOpen);
    }

    [Fact]
    public void ChannelClosedAsDestroyed_WithoutBDestroyed_IsNotABreak()
    {
        // Client unloads actors with closeReason Destroyed; that alone is not a break.
        var b = new TraceBuilder().Appear(10, "A").Pawn(MyPawn, 100, 0, 0).Close(100, "A", CloseReason.Destroyed);
        var c = Count(b, new FixedLocator("A", 0, 0));
        Assert.Equal(0, c.MineAndSquad);
        Assert.Equal(0, c.UnattributedDestroyed);
    }

    // --- Rule 2: once per match, re-entering relevancy --------------------------------------

    [Fact]
    public void ReenteringRelevancyAlreadyOpened_IsNotCountedAgain()
    {
        var b = new TraceBuilder().Appear(10, "A").Open(60, "A", MyPawn)
            .Close(120, "A", CloseReason.Relevancy)
            .Appear(300, "A", searched: true, pawn: MyPawn)
            .Close(400, "A", CloseReason.Destroyed)
            .Appear(500, "A", searched: true, pawn: MyPawn);
        var c = Count(b);
        Assert.Equal(1, c.Mine);
    }

    [Fact]
    public void DynamicChestWithNewGuidAtSameSpot_IsTheSameChest()
    {
        var b = new TraceBuilder();
        var first = b.AppearDynamic(10, "B_Athena_BuriedChest_Chest_C", 5000, 5000);
        b.OpenDynamic(30, first, "B_Athena_BuriedChest_Chest_C", MyPawn);
        b.CloseDynamic(40, first, "B_Athena_BuriedChest_Chest_C", CloseReason.Destroyed);
        b.AppearDynamic(200, "B_Athena_BuriedChest_Chest_C", 5050, 4980, searched: true, pawn: MyPawn);
        var c = Count(b);
        Assert.Equal(1, c.Mine);
        Assert.Equal(ChestType.Normal, c.Credits.Single().Type); // buried chests count as normal
    }

    [Fact]
    public void DynamicChestsFarApart_AreDifferentChests()
    {
        var b = new TraceBuilder();
        var a = b.AppearDynamic(10, "B_Athena_BuriedChest_Chest_C", 5000, 5000);
        var d = b.AppearDynamic(10, "B_Athena_BuriedChest_Chest_C", 9000, 5000);
        b.OpenDynamic(30, a, "B_Athena_BuriedChest_Chest_C", MyPawn);
        b.OpenDynamic(40, d, "B_Athena_BuriedChest_Chest_C", MyPawn);
        Assert.Equal(2, Count(b).Mine);
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public void ResetByGameAndReopenedByMe(bool countReopened, int expectedMine)
    {
        var b = new TraceBuilder().Appear(10, "A").Open(60, "A", MyPawn).Reset(300, "A").Open(320, "A", MyPawn);
        var c = new MatchCounter(options: new CountingOptions { CountReopenedAfterReset = countReopened }).Count(b.Build());
        Assert.Equal(expectedMine, c.Mine);
        Assert.Equal(1, c.ReopenedAfterReset);
    }

    [Fact]
    public void ResetWhileOutOfView_ThenIOpen_CountsForMe()
    {
        // A stranger opens it, the chest leaves relevancy, the game resets it unseen; it comes back closed and I open it.
        var b = new TraceBuilder().Appear(10, "A").Open(60, "A", OtherPawn)
            .Close(100, "A", CloseReason.Relevancy)
            .Appear(400, "A")
            .Update(420, "A", "Tiered_Chest_Athena_C", e => { e.AlreadySearched = true; e.SearchCount = 1; });
        // SearchingPawn is unchanged on the wire only if the same pawn searches; here I do, so it is sent.
        b.Build().ContainerEvents[^1].SearchingPawn = MyPawn;
        Assert.Equal(1, Count(b).Mine);
    }

    [Theory]
    [InlineData(true, 1, 2)]
    [InlineData(false, 0, 1)]
    public void TeammateOpened_ResetByGame_ThenIOpen(bool countReopened, int expectedMine, int expectedSquad)
    {
        var b = new TraceBuilder().Appear(10, "A").Open(60, "A", MatePawn).Reset(300, "A").Open(320, "A", MyPawn);
        var c = new MatchCounter(options: new CountingOptions { CountReopenedAfterReset = countReopened }).Count(b.Build());
        Assert.Equal(expectedMine, c.Mine);
        Assert.Equal(expectedSquad, c.MineAndSquad);
    }

    [Fact]
    public void ReenteringOpen_IsNeverASecondSearch_EvenWithReopenCounting()
    {
        var b = new TraceBuilder().Appear(10, "A").Open(60, "A", MyPawn)
            .Close(100, "A", CloseReason.Relevancy).Appear(200, "A", searched: true, pawn: MyPawn)
            .Close(300, "A", CloseReason.Relevancy).Appear(400, "A", searched: true, pawn: MyPawn);
        Assert.Equal(1, new MatchCounter(options: new CountingOptions { CountReopenedAfterReset = true }).Count(b.Build()).Mine);
    }

    [Fact]
    public void ResetByGame_LeftView_ComesBackSearchedWithoutPawn_IsNotMine()
    {
        // Real match (2026-11-08 05:30): I open the chest, the game resets it, it leaves relevancy; I die and spectate,
        // and it comes back (new channel) already searched with no SearchingPawn. Whoever searched it, it was not my
        // dead pawn: the initial bunch says "no searcher known", so the pawn remembered from my first search must not count.
        var b = new TraceBuilder().Appear(158, "A", tier: 2).Open(207, "A", MyPawn).Reset(313, "A")
            .Close(420, "A", CloseReason.Relevancy)
            .Appear(819, "A", searched: true, tier: 2);
        var c = new MatchCounter(options: new CountingOptions { CountReopenedAfterReset = true }).Count(b.Build());
        Assert.Equal(1, c.Mine);
        Assert.Equal(1, c.MineAndSquad);
    }

    [Fact]
    public void ResetByGame_LeftView_ComesBackSearchedByTeammate_CountsForSquad()
    {
        var b = new TraceBuilder().Appear(10, "A").Open(60, "A", MyPawn).Reset(300, "A")
            .Close(400, "A", CloseReason.Relevancy)
            .Appear(800, "A", searched: true, pawn: MatePawn);
        var c = new MatchCounter(options: new CountingOptions { CountReopenedAfterReset = true }).Count(b.Build());
        Assert.Equal(1, c.Mine);
        Assert.Equal(2, c.MineAndSquad);
    }

    [Fact]
    public void ResetByGame_ReopenedBySamePawnInView_StillCounts()
    {
        // Real match (2026-11-09 13:27): reset, then searched again by me while in view; SearchingPawn is not resent
        // because it did not change (same pawn). That second search is mine.
        var b = new TraceBuilder().Appear(248, "A", tier: 2).Open(439, "A", MyPawn).Reset(520, "A")
            .Update(617, "A", "Tiered_Chest_Athena_C", e => { e.AlreadySearched = true; e.SearchCount = 1; });
        var c = new MatchCounter(options: new CountingOptions { CountReopenedAfterReset = true }).Count(b.Build());
        Assert.Equal(2, c.Mine);
    }

    [Fact]
    public void ResetAfterStrangerOpened_ThenIOpen_CountsForMe()
    {
        var b = new TraceBuilder().Appear(10, "A").Open(60, "A", OtherPawn).Reset(300, "A").Open(320, "A", MyPawn);
        Assert.Equal(1, Count(b).Mine);
    }

    // --- Rule 7: already open when first seen ----------------------------------------------

    [Fact]
    public void AlreadyOpenWhenFirstSeen_ByStranger_IsForeign()
    {
        var c = Count(new TraceBuilder().Appear(10, "A", searched: true, pawn: OtherPawn));
        Assert.Equal(0, c.MineAndSquad);
        Assert.Equal(1, c.ForeignChests);
    }

    [Fact]
    public void AlreadyOpenWhenFirstSeen_WithoutPawn_IsForeign()
    {
        var c = Count(new TraceBuilder().Appear(10, "A", searched: true));
        Assert.Equal(0, c.MineAndSquad);
    }

    [Fact]
    public void AlreadyOpenWhenFirstSeen_ByTeammate_CountsForSquad()
    {
        var c = Count(new TraceBuilder().Appear(10, "A", searched: true, pawn: MatePawn));
        Assert.Equal(0, c.Mine);
        Assert.Equal(1, c.MineAndSquad);
    }

    [Fact]
    public void AlreadyOpenWhenFirstSeen_ThenBroken_DoesNotCount()
    {
        var c = Count(new TraceBuilder().Appear(10, "A", searched: true, pawn: OtherPawn).Destroy(50, "A"));
        Assert.Equal(0, c.MineAndSquad);
    }

    // --- Rule 6: distance heuristic ---------------------------------------------------------

    [Fact]
    public void DestroyedUnopenedWithoutInstigator_MeWithinRadius_HeuristicCredit()
    {
        var b = new TraceBuilder().Appear(10, "A")
            .Pawn(MyPawn, 99, 10, 0).Pawn(MyPawn, 100, 10, 0).Pawn(MatePawn, 100, 40, 0)
            .Destroy(100, "A");
        var c = Count(b, new FixedLocator("A", 0, 0));
        Assert.Equal(1, c.Mine);
        Assert.Equal(1, c.MineHeuristic);
        Assert.Equal(Attribution.Heuristic, c.Credits.Single().Method);
        Assert.Equal(CountKind.BrokenUnopened, c.Credits.Single().Kind);
    }

    [Fact]
    public void DestroyedUnopenedWithoutInstigator_OnlyTeammateClose_CreditsTeammate()
    {
        var b = new TraceBuilder().Appear(10, "A").Pawn(MyPawn, 100, 30, 0).Pawn(MatePawn, 100, 5, 0).Destroy(100, "A");
        var c = Count(b, new FixedLocator("A", 0, 0));
        Assert.Equal(0, c.Mine);
        Assert.Equal(1, c.MineAndSquad);
        Assert.Equal(1, c.SquadHeuristic);
    }

    [Fact]
    public void DestroyedUnopenedWithoutInstigator_MeTooFar_NotCounted()
    {
        var b = new TraceBuilder().Appear(10, "A").Pawn(MyPawn, 100, 16, 0).Destroy(100, "A");
        var c = Count(b, new FixedLocator("A", 0, 0));
        Assert.Equal(0, c.MineAndSquad);
        Assert.Equal(1, c.UnattributedDestroyed);
    }

    [Fact]
    public void DestroyedUnopenedWithoutInstigator_MeCloseButOutsideTimeWindow_NotCounted()
    {
        var b = new TraceBuilder().Appear(10, "A").Pawn(MyPawn, 95, 1, 0).Pawn(MyPawn, 105, 1, 0).Destroy(100, "A");
        var c = Count(b, new FixedLocator("A", 0, 0));
        Assert.Equal(0, c.MineAndSquad);
    }

    [Fact]
    public void HeuristicRadius_IsConfigurable()
    {
        var b = new TraceBuilder().Appear(10, "A").Pawn(MyPawn, 100, 20, 0).Destroy(100, "A");
        var counter = new MatchCounter(options: new CountingOptions { HeuristicRadiusMeters = 25 }, locator: new FixedLocator("A", 0, 0));
        Assert.Equal(1, counter.Count(b.Build()).MineHeuristic);
    }

    [Fact]
    public void DestroyedUnopened_UnknownLocation_NotCounted()
    {
        var b = new TraceBuilder().Appear(10, "A").Pawn(MyPawn, 100, 0, 0).Destroy(100, "A");
        var c = Count(b);
        Assert.Equal(0, c.MineAndSquad);
        Assert.Equal(1, c.UnattributedDestroyed);
    }

    // --- Rules 1 and 9: what is a chest, types ----------------------------------------------

    [Fact]
    public void AmmoBoxesAndOtherContainers_AreIgnored()
    {
        var b = new TraceBuilder()
            .Appear(10, "Ammo", "Tiered_Ammo_Athena_C").Open(20, "Ammo", MyPawn, "Tiered_Ammo_Athena_C")
            .Appear(10, "Food", "FoodBox_Produce_Athena_C").Open(20, "Food", MyPawn, "FoodBox_Produce_Athena_C")
            .Appear(10, "Cannon", "BGA_LootCannon_StandardChest_Pirate_C").Open(20, "Cannon", MyPawn, "BGA_LootCannon_StandardChest_Pirate_C");
        Assert.Equal(0, Count(b).MineAndSquad);
    }

    [Theory]
    [InlineData("Tiered_Chest_Athena_C", 0, ChestType.Normal)]
    [InlineData("Tiered_Chest_Athena_C", 2, ChestType.Rare)]
    [InlineData("AlwaysSpawn_NormalChest_C", 0, ChestType.Normal)]
    [InlineData("AlwaysSpawn_RareChest_C", 2, ChestType.Rare)]
    [InlineData("AlwaysSpawn_RareChest_C", 0, ChestType.Rare)]
    [InlineData("B_TieredChest_Relics_C", 0, ChestType.Sprite)]
    [InlineData("B_Delilah_TieredChest_Relics_Variant1_C", 0, ChestType.Sprite)]
    [InlineData("B_SpritePosters_Booty_Chest_C", 0, ChestType.Sprite)]
    [InlineData("B_Athena_BuriedChest_Chest_C", 0, ChestType.Normal)]
    public void ChestTypes(string cls, int tier, ChestType expected)
    {
        var c = Count(new TraceBuilder().Appear(10, "A", cls, tier: tier).Open(60, "A", MyPawn, cls));
        Assert.Equal(expected, c.Credits.Single().Type);
    }

    [Fact]
    public void UnknownChestClass_IsIgnoredButReported()
    {
        var c = Count(new TraceBuilder().Appear(10, "A", "Some_New_Chest_C").Open(60, "A", MyPawn, "Some_New_Chest_C"));
        Assert.Equal(0, c.Mine);
        Assert.Contains("Some_New_Chest_C", c.UnknownChestClasses);
    }

    [Fact]
    public void TreasureHuntMarker_IsNotCountedAndDoesNotChangeTheChest()
    {
        var b = new TraceBuilder().Appear(10, "A").TreasureMarker(59, "A", MyPawn)
            .Update(59.2f, "A", "Tiered_Chest_Athena_C", e => e.LootTier = 2)
            .Open(60, "A", MyPawn);
        var c = Count(b);
        Assert.Equal(1, c.Mine);
        Assert.Equal(ChestType.Rare, c.Credits.Single().Type);
    }

    // --- Mixed scenario ---------------------------------------------------------------------

    [Fact]
    public void ControlMatchScenario_N_Opened_M_BrokenUnopened_K_BrokenOpened()
    {
        // Mirrors the control match: N=3 opened, M=2 broken unopened, K=2 broken after opening.
        var b = new TraceBuilder();
        for (var i = 0; i < 7; i++)
        {
            b.Appear(5, $"C{i}");
        }
        b.Open(60, "C0", MyPawn).Open(70, "C1", MyPawn).Open(80, "C2", MyPawn)
         .BreakUnopened(90, "C3", MyPawn).BreakUnopened(100, "C4", MyPawn)
         .Open(110, "C5", OtherPawn).Destroy(120, "C5")
         .Open(130, "C6", MatePawn).Destroy(140, "C6");
        var c = Count(b);
        Assert.Equal(3 + 2, c.Mine);
        Assert.Equal(2, c.MineBroken);
        Assert.Equal(6, c.MineAndSquad);
        Assert.Equal(2, c.BrokenAfterOpen);
    }

    private sealed class FixedLocator : IChestLocator
    {
        private readonly string _key;
        private readonly (double, double, double) _pos;
        public FixedLocator(string key, double xMeters, double yMeters) { _key = key; _pos = (xMeters * 100, yMeters * 100, 0); }
        public (double X, double Y, double Z)? Locate(string chestKey) => chestKey == _key ? _pos : null;
    }
}
