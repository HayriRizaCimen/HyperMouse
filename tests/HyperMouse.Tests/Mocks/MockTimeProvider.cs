using HyperMouse.Core.Common;

namespace HyperMouse.Tests.Mocks;

public sealed class MockTimeProvider : ITimeProvider
{
    public long NowMs { get; set; } = 1000;

    public void Advance(long deltaMs)
    {
        NowMs += deltaMs;
    }
}

