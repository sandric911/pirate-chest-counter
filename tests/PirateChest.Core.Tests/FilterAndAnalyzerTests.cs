using PirateChest.Core.Counting;
using PirateChest.Core.Trace;
using static PirateChest.Core.Tests.TraceBuilder;

namespace PirateChest.Core.Tests;

public class FilterAndAnalyzerTests
{
    [Theory]
    [InlineData("Playlist_NoBuildBR_Solo")]
    [InlineData("Playlist_NoBuildBR_Duo")]
    [InlineData("Playlist_NoBuildBR_Trio")]
    [InlineData("Playlist_NoBuildBR_Squad")]
    [InlineData("Playlist_DefaultSolo")]
    [InlineData("Playlist_DefaultSquad")]
    public void BattleRoyaleAndZeroBuild_AreCounted(string playlist)
    {
        var s = new MatchAnalyzer().Analyze(new TraceBuilder().Meta(m => m.Playlist = playlist).Build());
        Assert.True(s.Counted, s.SkipReason);
    }

    [Theory]
    [InlineData("Playlist_PlaygroundV2")]
    [InlineData("Playlist_Juno_Survival")]
    [InlineData("Playlist_Respawn_24")]
    public void OtherModes_AreSkippedWithReason(string playlist)
    {
        var s = new MatchAnalyzer().Analyze(new TraceBuilder().Meta(m => m.Playlist = playlist).Build());
        Assert.False(s.Counted);
        Assert.Contains(playlist, s.SkipReason);
    }

    [Fact]
    public void PreviousSeasonBuild_IsSkipped()
    {
        var s = new MatchAnalyzer().Analyze(new TraceBuilder().Meta(m => { m.BuildMajor = 42; m.BuildMinor = 30; m.TimestampUtc = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc); }).Build());
        Assert.False(s.Counted);
        Assert.Contains("42.30", s.SkipReason);
    }

    [Fact]
    public void BeforeNovemberFirst_IsSkipped()
    {
        var s = new MatchAnalyzer().Analyze(new TraceBuilder().Meta(m => m.TimestampUtc = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc)).Build());
        Assert.False(s.Counted);
    }

    [Fact]
    public void LiveReplay_IsSkipped()
    {
        var s = new MatchAnalyzer().Analyze(new TraceBuilder().Meta(m => m.IsLive = true).Build());
        Assert.False(s.Counted);
    }

    [Fact]
    public void NoOwner_IsSkipped()
    {
        var t = new TraceBuilder().Build();
        t.Players.ForEach(p => p.IsReplayOwner = false);
        Assert.False(new MatchAnalyzer().Analyze(t).Counted);
    }

    [Fact]
    public void Landing_ActiveTime_ChestsPerMinute_BeforeDeath()
    {
        var b = new TraceBuilder()
            .Pawn(MyPawn, 30, 0, 0, inAir: true).Pawn(MyPawn, 60, 100, 50, inAir: true)
            .Pawn(MyPawn, 70, 120, 60).Pawn(MyPawn, 370, 500, 500)
            .Appear(5, "A").Appear(5, "B").Appear(5, "C")
            .Open(100, "A", MyPawn).Open(200, "B", MyPawn).Open(360, "C", MyPawn)
            .Meta(m => m.OwnerDeathTime = 300);
        var s = new MatchAnalyzer().Analyze(b.Build());
        Assert.Equal(70, s.LandingTime);
        Assert.Equal(12000, s.LandingX);
        Assert.Equal(370, s.ActiveEndTime);
        Assert.Equal(5.0, s.ActiveMinutes, 3);
        Assert.Equal(3 / 5.0, s.ChestsPerMinute!.Value, 3);
        Assert.Equal(2, s.MineBeforeFirstDeath);
    }

    [Fact]
    public void Landing_IgnoresJumpsOnThePreGameIsland()
    {
        var b = new TraceBuilder();
        // pre-game island: launch pad jump (20 m), then the bus drop from 600 m
        b.Pawn(TraceBuilder.MyPawn, 5, 9000, 15000).Pawn(TraceBuilder.MyPawn, 10, 9000, 15000, inAir: true);
        var t = b.Build();
        t.PawnSamples[^1].Z = 2000;
        b.Pawn(TraceBuilder.MyPawn, 12, 9001, 15001);
        b.Pawn(TraceBuilder.MyPawn, 40, 100, 50, inAir: true);
        t.PawnSamples[^1].Z = 60000;
        b.Pawn(TraceBuilder.MyPawn, 70, 120, 60);
        var landing = MatchAnalyzer.FindLanding(t.PawnSamples);
        Assert.Equal(70, landing.Time);
    }

    [Fact]
    public void ModeLabels_AreRussian()
    {
        Assert.Equal("Zero Build, отряды", MatchFilter.ModeLabel("Playlist_NoBuildBR_Squad"));
        Assert.Equal("Королевская битва, дуо", MatchFilter.ModeLabel("Playlist_DefaultDuo"));
    }

    [Fact]
    public void Atlas_LearnsStaticChestLocationFromOpeners_AndRejectsOutliers()
    {
        var atlas = new ChestAtlas();
        for (var i = 0; i < 4; i++)
        {
            atlas.Add("A", 1000 + i * 10, 2000, 0);
        }
        atlas.Add("A", 90000, 90000, 0); // glider passing by
        var loc = atlas.Locate("A")!.Value;
        Assert.InRange(loc.X, 1000, 1030);
        Assert.Equal(2000, loc.Y, 3);
        Assert.Null(atlas.Locate("B"));
    }
}
