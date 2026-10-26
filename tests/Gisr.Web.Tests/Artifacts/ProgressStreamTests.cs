using Gisr.Web.Artifacts;

namespace Gisr.Web.Tests.Artifacts;

public class ProgressStreamTests
{
    [Fact]
    public async Task ReportsTheCumulativeBytesRead_AndAlwaysTheFinalCount()
    {
        var reports = new List<long>();
        await using var stream = new ProgressStream(new MemoryStream(new byte[1_000_000]), reports.Add, reportEveryBytes: 300_000);
        var buffer = new byte[100_000];

        while (await stream.ReadAsync(buffer) > 0)
        {
        }

        Assert.NotEmpty(reports);
        Assert.Equal(reports.OrderBy(r => r), reports);
        Assert.Equal(1_000_000, reports[^1]);
        Assert.True(reports.Count < 10, "Reports should be throttled.");
    }

    [Fact]
    public void SynchronousReadsAreCounted_Too()
    {
        var reports = new List<long>();
        using var stream = new ProgressStream(new MemoryStream(new byte[10]), reports.Add, reportEveryBytes: 1);

        _ = stream.Read(new byte[4], 0, 4);
        _ = stream.Read(new byte[10], 0, 10);
        _ = stream.Read(new byte[4], 0, 4);

        Assert.Equal(10, reports[^1]);
    }

    [Fact]
    public async Task DisposingTheWrapper_DisposesTheInnerStream()
    {
        var inner = new MemoryStream([1, 2, 3]);
        var stream = new ProgressStream(inner, _ => { });

        await stream.DisposeAsync();

        Assert.False(inner.CanRead);
    }

    [Fact]
    public void IsAForwardOnlyReadableStream()
    {
        using var stream = new ProgressStream(new MemoryStream([1]), _ => { });

        Assert.True(stream.CanRead);
        Assert.False(stream.CanSeek);
        Assert.False(stream.CanWrite);
        Assert.Throws<NotSupportedException>(() => stream.Write([1], 0, 1));
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
    }
}
