using System;
using System.IO;
using Wantodo.Core;

namespace Wantodo.Core.Tests;

public sealed class NoteWriteTrackerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Written = new(2026, 11, 14, 11, 59, 58, TimeSpan.Zero);

    private readonly string _root;

    public NoteWriteTrackerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "wantodo-tracker-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 测试清理失败不影响结论。
        }
    }

    [Fact]
    public void IsNotePath_AcceptsNotes_RejectsTempAndHidden()
    {
        Assert.True(NoteWriteTracker.IsNotePath(Path.Combine(_root, "inbox", "20261114-1200-买牛奶.md")));
        Assert.True(NoteWriteTracker.IsNotePath(Path.Combine(_root, "20261114-1200-note.TXT")));
        Assert.False(NoteWriteTracker.IsNotePath(Path.Combine(_root, "20261114-1200-note.md.tmp")));
        Assert.False(NoteWriteTracker.IsNotePath(Path.Combine(_root, ".hidden.md")));
        Assert.False(NoteWriteTracker.IsNotePath(Path.Combine(_root, "readme")));
    }

    [Fact]
    public void Capture_ThenIsSelfWrite_MatchesFileJustWritten()
    {
        var path = Path.Combine(_root, "a.md");
        File.WriteAllText(path, "标题\n正文");

        var stamp = NoteWriteTracker.Capture(path);
        var info = new FileInfo(path);

        Assert.Equal(info.Length, stamp.Length);
        Assert.True(NoteWriteTracker.IsSelfWrite(
            new[] { stamp },
            path,
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc),
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsSelfWrite_LengthDiffers_ReturnsFalse()
    {
        var path = Path.Combine(_root, "b.md");
        var stamp = new NoteWriteStamp(path, 10, Written, Now);

        Assert.False(NoteWriteTracker.IsSelfWrite(new[] { stamp }, path, 11, Written, Now));
    }

    [Fact]
    public void IsSelfWrite_LastWriteDiffersBeyondTolerance_ReturnsFalse()
    {
        var path = Path.Combine(_root, "c.md");
        var stamp = new NoteWriteStamp(path, 10, Written, Now);

        Assert.True(NoteWriteTracker.IsSelfWrite(
            new[] { stamp },
            path,
            10,
            Written.AddMilliseconds(900),
            Now));
        Assert.False(NoteWriteTracker.IsSelfWrite(
            new[] { stamp },
            path,
            10,
            Written.AddMilliseconds(1500),
            Now));
    }

    [Fact]
    public void IsSelfWrite_OtherPath_ReturnsFalse()
    {
        var stamp = new NoteWriteStamp(Path.Combine(_root, "d.md"), 10, Written, Now);

        Assert.False(NoteWriteTracker.IsSelfWrite(
            new[] { stamp },
            Path.Combine(_root, "e.md"),
            10,
            Written,
            Now));
    }

    [Fact]
    public void IsSelfWrite_StampOlderThanWindow_ReturnsFalse()
    {
        var path = Path.Combine(_root, "f.md");
        var stamp = new NoteWriteStamp(path, 10, Written, Now.AddSeconds(-6));

        Assert.False(NoteWriteTracker.IsSelfWrite(new[] { stamp }, path, 10, Written, Now));
    }

    [Fact]
    public void IsSelfWrite_DeletedFile_ReturnsFalse()
    {
        var path = Path.Combine(_root, "g.md");
        var stamp = new NoteWriteStamp(path, 10, Written, Now);

        Assert.False(NoteWriteTracker.IsSelfWrite(new[] { stamp }, path, -1, Written, Now));
    }
}
