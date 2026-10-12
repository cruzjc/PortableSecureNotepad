using System.Security.Cryptography;

namespace PortableSecureNotepad.Core;

/// <summary>Owns the editable vault and preserves an unfinished editor through locking.</summary>
public sealed class VaultSession : IDisposable
{
    private VaultDocument? _document;
    private string? _password;
    private string _draft = string.Empty;
    private byte[]? _lockedCheckpoint;
    private readonly HashSet<Guid> _changedIds = [];
    private readonly HashSet<Guid> _addedIds = [];
    private Guid _draftId = Guid.NewGuid();
    private DateTimeOffset _draftCreatedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset _draftUpdatedAt = DateTimeOffset.UtcNow;
    private bool _lockedHasChanges;
    private bool _disposed;

    public VaultSession(VaultDocument document, string password)
    {
        ArgumentNullException.ThrowIfNull(document);
        VaultCrypto.ValidateDocument(document);
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password/PIN cannot be empty.", nameof(password));
        _document = Clone(document);
        _password = password;
    }

    public bool IsLocked => _document is null;
    public Guid? EditingNoteId { get; private set; }
    public int ChangedCount => _changedIds.Count;
    public int NoteCount => Document.Notes.Count;
    public bool HasChanges => IsLocked ? _lockedHasChanges : _changedIds.Count > 0 || HasDraftChanges;
    public bool HasDraftChanges => EditingNoteId is Guid id
        ? _draft != Document.Notes.Single(note => note.Id == id).Text
        : !string.IsNullOrWhiteSpace(_draft);
    public string DraftText { get { EnsureUnlocked(); return _draft; } }
    public IReadOnlyList<NoteEntry> Notes => Snapshot(includeDraft: false).Notes;
    public bool IsChanged(Guid id) => _changedIds.Contains(id);
    public bool IsAdded(Guid id) => _addedIds.Contains(id);

    private VaultDocument Document { get { EnsureUnlocked(); return _document!; } }

    public void UpdateDraft(string text)
    {
        EnsureUnlocked();
        ArgumentNullException.ThrowIfNull(text);
        if (_draft == text) return;
        if (EditingNoteId is null && string.IsNullOrEmpty(_draft)) _draftCreatedAt = DateTimeOffset.UtcNow;
        _draft = text;
        _draftUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void BeginEdit(Guid id)
    {
        EnsureUnlocked();
        if (HasDraftChanges) throw new InvalidOperationException("Save or cancel the current draft before editing another note.");
        NoteEntry note = Document.Notes.Single(note => note.Id == id);
        EditingNoteId = id;
        _draft = note.Text;
        _draftUpdatedAt = note.UpdatedAt ?? note.CreatedAt;
    }

    public void CancelDraft()
    {
        EnsureUnlocked();
        _draft = string.Empty;
        EditingNoteId = null;
        _draftId = Guid.NewGuid();
    }

    public Guid SaveDraft()
    {
        EnsureUnlocked();
        if (string.IsNullOrWhiteSpace(_draft)) throw new ArgumentException("A note cannot be empty.");
        Guid id = EditingNoteId ?? _draftId;
        bool changed = HasDraftChanges;
        bool added = EditingNoteId is null;
        _document = Snapshot();
        if (changed) _changedIds.Add(id);
        if (added) _addedIds.Add(id);
        CancelDraft();
        return id;
    }

    public VaultDocument Snapshot(bool includeDraft = true)
    {
        var snapshot = Clone(Document);
        if (!includeDraft || !HasDraftChanges) return snapshot;
        if (string.IsNullOrWhiteSpace(_draft)) throw new ArgumentException("The edited note is empty. Enter text or cancel the edit.");
        if (EditingNoteId is Guid id)
        {
            NoteEntry note = snapshot.Notes.Single(note => note.Id == id);
            note.Text = _draft;
            note.UpdatedAt = _draftUpdatedAt;
        }
        else
        {
            snapshot.Notes.Add(new NoteEntry { Id = _draftId, CreatedAt = _draftCreatedAt, Text = _draft });
        }
        return snapshot;
    }

    public byte[] CreateCommitPayload() => VaultCrypto.Encrypt(Snapshot(), Password);

    public byte[] CreatePasswordChangePayload(string currentPassword, string newPassword)
    {
        EnsureUnlocked();
        if (!string.Equals(currentPassword, Password, StringComparison.Ordinal))
            throw new VaultUnlockException("The current password/PIN is incorrect.");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
            throw new ArgumentException("Use at least 4 characters and a non-blank password/PIN.");
        // The active password is unchanged until the replacement succeeds and the app restarts.
        return VaultCrypto.Encrypt(Snapshot(), newPassword);
    }

    public byte[] CreateExport(VaultExportFormat format) => format == VaultExportFormat.Encrypted
        ? VaultCrypto.Encrypt(Snapshot(), Password)
        : VaultTransfer.SerializePlain(Snapshot(), format);

    public int Import(VaultDocument imported)
    {
        EnsureUnlocked();
        ArgumentNullException.ThrowIfNull(imported);
        VaultCrypto.ValidateDocument(imported);
        int added = 0;
        var copy = Clone(imported);
        foreach (NoteEntry note in copy.Notes)
        {
            // An edited version of the same ID must not replace existing work.
            if (Document.Notes.Any(existing => existing.Id == note.Id))
            {
                if (Document.Notes.Any(existing => existing.Text == note.Text &&
                    existing.CreatedAt == note.CreatedAt && existing.UpdatedAt == note.UpdatedAt)) continue;
                note.Id = Guid.NewGuid();
            }
            Document.Notes.Add(note);
            _addedIds.Add(note.Id);
            _changedIds.Add(note.Id);
            added++;
        }
        return added;
    }

    public void Lock()
    {
        EnsureUnlocked();
        bool hasChanges = HasChanges;
        var checkpoint = Clone(Document);
        // The final entry is an encrypted in-memory draft, never an exported or committed note.
        checkpoint.Notes.Add(new NoteEntry { Text = _draft });
        byte[] encrypted = VaultCrypto.Encrypt(checkpoint, Password);
        _lockedCheckpoint = encrypted;
        _lockedHasChanges = hasChanges;
        _document = null;
        _password = null;
        _draft = string.Empty;
    }

    public void Unlock(string password)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsLocked) throw new InvalidOperationException("The vault is already unlocked.");
        VaultDocument checkpoint = VaultCrypto.Decrypt(_lockedCheckpoint!, password);
        string draft = checkpoint.Notes[^1].Text;
        checkpoint.Notes.RemoveAt(checkpoint.Notes.Count - 1);
        _document = checkpoint;
        _password = password;
        _draft = draft;
        CryptographicOperations.ZeroMemory(_lockedCheckpoint!);
        _lockedCheckpoint = null;
    }

    private string Password { get { EnsureUnlocked(); return _password!; } }
    private void EnsureUnlocked()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsLocked) throw new InvalidOperationException("Unlock the vault before using notes.");
    }

    private static VaultDocument Clone(VaultDocument source) => VaultDocumentBatch.CreateWithAppendedNotes(source, []);

    public void Dispose()
    {
        if (_disposed) return;
        if (_lockedCheckpoint is not null) CryptographicOperations.ZeroMemory(_lockedCheckpoint);
        _lockedCheckpoint = null;
        _document = null;
        _password = null;
        _draft = string.Empty;
        _changedIds.Clear();
        _addedIds.Clear();
        _disposed = true;
    }
}
