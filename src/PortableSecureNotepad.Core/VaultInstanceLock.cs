using System.Security.Cryptography;
using System.Text;

namespace PortableSecureNotepad.Core;

/// <summary>Serializes editing and replacement of the same portable executable.</summary>
public sealed class VaultInstanceLock : IDisposable
{
    private readonly Mutex _mutex;
    private bool _disposed;

    private VaultInstanceLock(Mutex mutex) => _mutex = mutex;

    public static VaultInstanceLock? TryAcquire(string executablePath, TimeSpan timeout)
    {
        string path = Path.GetFullPath(executablePath).ToUpperInvariant();
        string name = "Global\\PortableSecureNotepad." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
        var mutex = new Mutex(false, name);
        try
        {
            bool acquired;
            try
            {
                acquired = mutex.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (acquired)
            {
                return new VaultInstanceLock(mutex);
            }

            mutex.Dispose();
            return null;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _mutex.ReleaseMutex();
        _mutex.Dispose();
        _disposed = true;
    }
}
