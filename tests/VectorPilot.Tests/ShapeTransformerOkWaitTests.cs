using VectorPilot.Geometry;
using VectorPilot.Serial;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>SPK-1101FlipH parity: mirror across the vertical centerline.</summary>
public class ShapeTransformerTests
{
    [Fact]
    public void FlipHorizontal_Mirrors_Across_Centerline()
    {
        // Line (0,0)-(10,0); combined centerline x=5 → mirrored endpoints.
        var line = VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(10, 0));
        var shapes = ShapeTransformer.FlipHorizontal(new[] { line }, new VectorPoint(5, 0));
        var s = shapes[0];
        Assert.Equal(10, s.Points[0].X);
        Assert.Equal(0, s.Points[0].Y);
        Assert.Equal(0, s.Points[1].X);
    }

    [Fact]
    public void FlipHorizontal_Circle_Center_Mirrors_Radius_Stays()
    {
        var circle = VectorShape.Circle(new VectorPoint(2, 5), 2);
        var shapes = ShapeTransformer.FlipHorizontal(new[] { circle }, new VectorPoint(5, 0));
        var s = shapes[0];
        Assert.Equal(8, s.Points[0].X); // 2·5 − 2
        Assert.Equal(5, s.Points[0].Y);
        Assert.Equal(2, s.Radius);
    }

    [Fact]
    public void FlipVertical_Mirrors_Across_Horizontal_Centerline()
    {
        var line = VectorShape.Line(new VectorPoint(1, 2), new VectorPoint(1, 8));
        var shapes = ShapeTransformer.FlipVertical(new[] { line }, new VectorPoint(0, 5));
        var s = shapes[0];
        Assert.Equal(8, s.Points[0].Y);
        Assert.Equal(2, s.Points[1].Y);
    }

    [Fact]
    public void BoundingBoxCenter_Is_Centroid_Of_Combined_Bounds()
    {
        var a = VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(2, 0));
        var b = VectorShape.Line(new VectorPoint(4, 4), new VectorPoint(6, 8));
        var c = ShapeTransformer.BoundingBoxCenter(new[] { a, b });
        Assert.Equal(3, c.X); // (0 + 6) / 2
        Assert.Equal(4, c.Y); // (0 + 8) / 2
    }

    [Fact]
    public void Scale_And_Rotate_About_Center()
    {
        var line = VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(10, 0));
        var scaled = ShapeTransformer.Scale(new[] { line }, 0.5, new VectorPoint(5, 0))[0];
        Assert.Equal(2.5, scaled.Points[0].X, 6); // (0−5)·0.5 + 5
        Assert.Equal(7.5, scaled.Points[1].X, 6);

        var rotated = ShapeTransformer.Rotate(new[] { line }, 90, new VectorPoint(5, 0))[0];
        Assert.Equal(5, rotated.Points[0].X, 6);
        Assert.Equal(-5, rotated.Points[0].Y, 6);
        Assert.Equal(5, rotated.Points[1].X, 6);
        Assert.Equal(5, rotated.Points[1].Y, 6);
    }
}

/// <summary>SPK-0404a parity: the streamer sends ONE line and waits for its ack
/// before sending the next (ok-wait protocol).</summary>
public class OkWaitProtocolTests
{
    /// <summary>Fake transport that only acks when told (via SendNextAck).</summary>
    private sealed class GatedTransport : IMachineTransport
    {
        public event Action<TransportEvent>? EventReceived;
        public List<string> Sent { get; } = new();

        public Task OpenAsync(MachineProfile profile, CancellationToken ct = default)
            => Task.CompletedTask;

        public bool IsOpen => true;
        public string Name => "gated";

        public Task WriteLineAsync(string line, CancellationToken ct = default)
        {
            Sent.Add(line);
            EventReceived?.Invoke(new TransportEvent(TransportEventType.DataReceived, line, DateTime.UtcNow));
            return Task.CompletedTask;
        }

        public void SendNextAck()
        {
            EventReceived?.Invoke(new TransportEvent(TransportEventType.Ok, "ok", DateTime.UtcNow));
        }

        public Task CloseAsync() => Task.CompletedTask;
        public Task SetFeedOverrideAsync(int percent, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetSpindleOverrideAsync(int percent, CancellationToken ct = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task UnlockAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ResetAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task JogAsync(double x, double y, double z, double rate, CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Sends_One_Line_Then_Waits_For_Ack()
    {
        var transport = new GatedTransport();
        var streamer = new VectorPilot.Engine.GCodeStreamer(transport);

        var run = streamer.StartAsync(new[] { "G1 X1", "G1 X2", "G1 X3" });
        await Task.Delay(200);

        // No acks delivered yet — only the FIRST line may have been sent.
        Assert.Single(transport.Sent);
        Assert.Equal("G1 X1", transport.Sent[0]);

        transport.SendNextAck();
        await Task.Delay(100);
        Assert.Equal(2, transport.Sent.Count);

        transport.SendNextAck();
        await Task.Delay(100);
        Assert.Equal(3, transport.Sent.Count);

        transport.SendNextAck();
        await run;
        Assert.Equal(3, transport.Sent.Count);
        Assert.Equal(VectorPilot.Engine.StreamPhase.Completed, streamer.Phase);
    }
}
