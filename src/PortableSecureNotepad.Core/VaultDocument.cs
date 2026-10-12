namespace PortableSecureNotepad.Core;

public sealed class VaultDocument
{
    public int Version { get; set; } = 1;

    public List<NoteEntry> Notes { get; set; } = [];
}

public sealed class NoteEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    public string Text { get; set; } = string.Empty;
}
