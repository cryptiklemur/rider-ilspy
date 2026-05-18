using System;
using System.Threading;
using JetBrains.Lifetimes;
using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class IlSpyAssemblyWatcherTests
{
    [Fact]
    public void Debounces_Rapid_Events()
    {
        int fires = 0;
        using LifetimeDefinition lt = new LifetimeDefinition();
        using IlSpyAssemblyWatcher watcher = new IlSpyAssemblyWatcher(
            lt.Lifetime,
            _ => Interlocked.Increment(ref fires),
            TimeSpan.FromMilliseconds(100));

        for (int i = 0; i < 10; i++) watcher.OnFileChanged("/x/a.dll");

        Thread.Sleep(300);

        Assert.Equal(1, fires);
    }
}
