using System.Text;
using PirateChest.ReplayReader;

namespace PirateChest.Core.Tests;

/// <summary>
/// The first bytes of a replay tell whether Fortnite has finished it. While a match is recorded the header is
/// marked live and the 32-byte decryption key is all zeros; both change when recording stops.
/// </summary>
public class ReplayHeaderProbeTests
{
    internal static readonly byte[] RealKey = Enumerable.Range(1, 32).Select(i => (byte)(i * 7)).ToArray();
    internal static readonly byte[] ZeroKey = new byte[32];

    /// <summary>The replay info block as Unreal writes it (LocalFileNetworkReplayStreaming), followed by filler.</summary>
    internal static byte[] Header(bool live, bool encrypted, byte[] key, uint fileVersion = 7, int customVersions = 3, uint lengthMs = 1_234_000u, bool filler = true)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(0x1CA2E27Fu);                 // file magic
        w.Write(fileVersion);
        if (fileVersion >= 7)
        {
            w.Write(customVersions);
            w.Write(new byte[customVersions * 20]); // guid + version each
        }
        w.Write(lengthMs);                    // length, ms
        w.Write(2u);                          // network version
        w.Write(59_594_394u);                 // changelist
        var name = "Unsaved Replay".PadRight(255) + "\0"; // fixed-size friendly name, UTF-16
        w.Write(-name.Length);
        w.Write(Encoding.Unicode.GetBytes(name));
        w.Write(live ? 1u : 0u);
        w.Write(DateTime.UtcNow.Ticks);       // recorded timestamp
        w.Write(1u);                          // compressed
        w.Write(encrypted ? 1u : 0u);
        w.Write((uint)key.Length);
        w.Write(key);
        if (filler)
        {
            w.Write(new byte[256]);           // start of the first chunk; irrelevant here
        }
        w.Flush();
        return ms.ToArray();
    }

    [Fact]
    public void Recording_LiveHeaderWithPlaceholderKey()
    {
        Assert.Equal(ReplayHeaderState.Recording, ReplayFiles.ProbeHeader(Header(live: true, encrypted: true, ZeroKey)));
    }

    [Fact]
    public void Finished_NotLiveAndKeyWritten()
    {
        Assert.Equal(ReplayHeaderState.Finished, ReplayFiles.ProbeHeader(Header(live: false, encrypted: true, RealKey)));
    }

    [Fact]
    public void Recording_NotLiveButKeyIsStillThePlaceholder()
    {
        // Caught in the middle of the final header rewrite: reading now would decrypt with a key of zeros.
        Assert.Equal(ReplayHeaderState.Recording, ReplayFiles.ProbeHeader(Header(live: false, encrypted: true, ZeroKey)));
    }

    [Fact]
    public void Recording_LiveEvenIfAKeyIsPresent()
    {
        Assert.Equal(ReplayHeaderState.Recording, ReplayFiles.ProbeHeader(Header(live: true, encrypted: true, RealKey)));
    }

    [Fact]
    public void Recording_LiveAndNotEncrypted()
    {
        Assert.Equal(ReplayHeaderState.Recording, ReplayFiles.ProbeHeader(Header(live: true, encrypted: false, Array.Empty<byte>())));
    }

    [Fact]
    public void Finished_NotEncryptedReplayNeedsNoKey()
    {
        Assert.Equal(ReplayHeaderState.Finished, ReplayFiles.ProbeHeader(Header(live: false, encrypted: false, Array.Empty<byte>())));
    }

    [Fact]
    public void Finished_OlderHeaderWithoutCustomVersions()
    {
        Assert.Equal(ReplayHeaderState.Finished, ReplayFiles.ProbeHeader(Header(live: false, encrypted: true, RealKey, fileVersion: 6)));
        Assert.Equal(ReplayHeaderState.Recording, ReplayFiles.ProbeHeader(Header(live: true, encrypted: true, ZeroKey, fileVersion: 6)));
    }

    [Fact]
    public void Unreadable_NotAReplay()
    {
        Assert.Equal(ReplayHeaderState.Unreadable, ReplayFiles.ProbeHeader(new byte[4096]));
        Assert.Equal(ReplayHeaderState.Unreadable, ReplayFiles.ProbeHeader(Array.Empty<byte>()));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(40)]
    [InlineData(300)]
    [InlineData(620)]
    public void Unreadable_FileCutShortInsideTheHeader(int bytes)
    {
        // A file that is only being created must never look finished.
        var cut = Header(live: false, encrypted: true, RealKey).AsSpan(0, bytes).ToArray();
        Assert.Equal(ReplayHeaderState.Unreadable, ReplayFiles.ProbeHeader(cut));
    }

    [Fact]
    public void Unreadable_KeyCutShort()
    {
        var full = Header(live: false, encrypted: true, RealKey);
        var cut = full.AsSpan(0, full.Length - 256 - 10).ToArray(); // 22 of the 32 key bytes
        Assert.Equal(ReplayHeaderState.Unreadable, ReplayFiles.ProbeHeader(cut));
    }

    [Fact]
    public void Probe_ReadsAFileThatAnotherProcessKeepsOpenForWriting()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pcc-probe-{Guid.NewGuid():N}.replay");
        try
        {
            using (var game = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
            {
                game.Write(Header(live: true, encrypted: true, ZeroKey));
                game.Flush();
                Assert.Equal(ReplayHeaderState.Recording, ReplayFiles.Probe(path));

                game.Position = 0;
                game.Write(Header(live: false, encrypted: true, RealKey));
                game.Flush();
                Assert.Equal(ReplayHeaderState.Finished, ReplayFiles.Probe(path));
            }
            Assert.Equal(ReplayHeaderState.Finished, ReplayFiles.Probe(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Probe_MissingFileIsUnreadable()
    {
        Assert.Equal(ReplayHeaderState.Unreadable, ReplayFiles.Probe(Path.Combine(Path.GetTempPath(), $"pcc-none-{Guid.NewGuid():N}.replay")));
    }
}
