// Integration with JetBrains.ProjectModel.IFileSystemTracker happens in
// IlSpySearchService — that component subscribes to file-change events
// for each indexed assembly path and calls OnFileChanged here.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Lifetimes;

namespace RiderIlSpy.Search;

public sealed class IlSpyAssemblyWatcher : IDisposable
{
    private readonly Dictionary<string, CancellationTokenSource> myPendingByPath = new Dictionary<string, CancellationTokenSource>();
    private readonly Action<string> myOnChanged;
    private readonly TimeSpan myDebounce;
    private readonly Lifetime myLifetime;

    public IlSpyAssemblyWatcher(Lifetime lifetime, Action<string> onChanged, TimeSpan? debounce = null)
    {
        myLifetime = lifetime;
        myOnChanged = onChanged;
        myDebounce = debounce ?? TimeSpan.FromMilliseconds(500);
    }

    public void OnFileChanged(string path)
    {
        lock (myPendingByPath)
        {
            if (myPendingByPath.TryGetValue(path, out CancellationTokenSource? prev))
            {
                prev.Cancel();
                prev.Dispose();
            }
            CancellationTokenSource cts = new CancellationTokenSource();
            myPendingByPath[path] = cts;
            Task.Delay(myDebounce, cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                lock (myPendingByPath) { myPendingByPath.Remove(path); }
                myOnChanged(path);
            }, TaskScheduler.Default);
        }
    }

    public void Dispose()
    {
        lock (myPendingByPath)
        {
            foreach (CancellationTokenSource cts in myPendingByPath.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            myPendingByPath.Clear();
        }
    }
}
