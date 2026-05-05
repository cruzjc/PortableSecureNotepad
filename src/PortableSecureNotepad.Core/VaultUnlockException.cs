namespace PortableSecureNotepad.Core;

public sealed class VaultUnlockException : Exception
{
    public VaultUnlockException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
