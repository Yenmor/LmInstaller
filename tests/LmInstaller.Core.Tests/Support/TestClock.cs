using LmInstaller.Core.Diagnostics;

namespace LmInstaller.Core.Tests.Support;

internal sealed class TestClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
}
