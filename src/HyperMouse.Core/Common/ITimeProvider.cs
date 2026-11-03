namespace HyperMouse.Core.Common;

/// <summary>
/// Abstraction for time queries to enable deterministic unit testing without real delays.
/// </summary>
public interface ITimeProvider
{
    long NowMs { get; }
}

public sealed class SystemTimeProvider : ITimeProvider
{
    public static readonly SystemTimeProvider Instance = new();

    public long NowMs => Environment.TickCount64;
}

