namespace PortableSecureNotepad.Core;

public static class VaultDocumentBatch
{
    public static VaultDocument CreateWithAppendedNotes(
        VaultDocument source,
        IEnumerable<NoteEntry> notesToAppend)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(notesToAppend);

        var nextDocument = new VaultDocument
        {
            Version = source.Version,
            Notes = source.Notes.Select(CloneNote).ToList()
        };

        nextDocument.Notes.AddRange(notesToAppend.Select(CloneNote));
        return nextDocument;
    }

    private static NoteEntry CloneNote(NoteEntry note)
    {
        return new NoteEntry
        {
            Id = note.Id,
            CreatedAt = note.CreatedAt,
            Text = note.Text
        };
    }
}
