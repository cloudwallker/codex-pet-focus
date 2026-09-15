using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CodexPetFocus.App;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle activationEvent;
    private readonly ManualResetEvent stopEvent = new(false);
    private readonly Thread? listenerThread;
    private bool ownsMutex;
    private bool disposed;

    public SingleInstanceCoordinator(string dataDirectory)
    {
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(dataDirectory))));
        var baseName = $@"Local\CodexPetFocus.{identity}";
        mutex = new Mutex(false, baseName + ".mutex");
        activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, baseName + ".activate");

        try
        {
            ownsMutex = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }

        if (ownsMutex)
        {
            listenerThread = new Thread(Listen)
            {
                IsBackground = true,
                Name = "Codex Pet Focus activation listener"
            };
            listenerThread.Start();
        }
    }

    public bool IsFirstInstance => ownsMutex;

    public event EventHandler? ActivationRequested;

    public void SignalFirstInstance()
    {
        if (!ownsMutex)
            activationEvent.Set();
    }

    private void Listen()
    {
        var handles = new WaitHandle[] { activationEvent, stopEvent };
        while (WaitHandle.WaitAny(handles) == 0)
            ActivationRequested?.Invoke(this, EventArgs.Empty);
    }

    private static string Normalize(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (!string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.ToUpperInvariant();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        stopEvent.Set();
        listenerThread?.Join(TimeSpan.FromSeconds(1));
        if (ownsMutex)
        {
            mutex.ReleaseMutex();
            ownsMutex = false;
        }
        stopEvent.Dispose();
        activationEvent.Dispose();
        mutex.Dispose();
    }
}
