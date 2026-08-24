namespace VectorPilot.Tests;

/// <summary>
/// Suite-wide gate for lazily constructing the WPF Application in STA test threads.
/// "Check-then-create" from two different STA threads races (one thread passes the
/// null check while another is mid-constructor), and WPF forbids a second instance
/// per AppDomain. Every OnSta harness that lazily news up an Application must take
/// this lock first.
/// </summary>
public static class STAApplicationGate
{
    public static readonly object Lock = new();
}
