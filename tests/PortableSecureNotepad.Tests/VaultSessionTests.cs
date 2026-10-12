using PortableSecureNotepad.Core;
using System.Text.Json;

namespace PortableSecureNotepad.Tests;

[TestClass]
public sealed class VaultSessionTests
{
    private const string Password = "original-password";
    private static VaultDocument Source() => new()
    {
        Notes = [new NoteEntry { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            CreatedAt = DateTimeOffset.Parse("2026-10-01T10:30:00-10:00"), Text = "Original ʻōlelo note" }]
    };

    [TestMethod]
    public void Edit_PreservesIdentityCreationTimeWhitespaceAndSource()
    {
        var original = Source();
        using var session = new VaultSession(original, Password);
        Guid id = original.Notes[0].Id;
        session.BeginEdit(id);
        session.UpdateDraft("  edited ʻōlelo\r\nwith trailing spaces  ");
        Assert.IsTrue(session.HasChanges);
        session.SaveDraft();
        Assert.HasCount(1, session.Notes);
        Assert.AreEqual(id, session.Notes[0].Id);
        Assert.AreEqual(original.Notes[0].CreatedAt, session.Notes[0].CreatedAt);
        Assert.IsNotNull(session.Notes[0].UpdatedAt);
        Assert.AreEqual("  edited ʻōlelo\r\nwith trailing spaces  ", session.Notes[0].Text);
        Assert.AreEqual("Original ʻōlelo note", original.Notes[0].Text);
        Assert.IsTrue(session.IsChanged(id));
        Assert.IsFalse(session.IsAdded(id));
    }

    [TestMethod]
    public void EditingPendingEntry_DoesNotCreateDuplicate()
    {
        using var session = new VaultSession(Source(), Password);
        session.UpdateDraft("pending");
        Guid id = session.SaveDraft();
        session.BeginEdit(id);
        session.UpdateDraft("updated pending");
        session.SaveDraft();
        Assert.HasCount(2, session.Notes);
        Assert.AreEqual("updated pending", session.Notes.Single(note => note.Id == id).Text);
        Assert.IsTrue(session.IsAdded(id));
    }

    [TestMethod]
    public void Snapshot_IncludesActiveEditWithoutMutatingWorkingNotes()
    {
        using var session = new VaultSession(Source(), Password);
        session.BeginEdit(session.Notes[0].Id);
        session.UpdateDraft("un-staged edit");
        var snapshot = session.Snapshot();
        Assert.HasCount(1, snapshot.Notes);
        Assert.AreEqual("un-staged edit", snapshot.Notes[0].Text);
        Assert.AreEqual("Original ʻōlelo note", session.Notes[0].Text);
        snapshot.Notes[0].Text = "external mutation";
        Assert.AreEqual("un-staged edit", session.DraftText);
    }

    [TestMethod]
    public void CancelEdit_RestoresExistingNoteAndCleanState()
    {
        using var session = new VaultSession(Source(), Password);
        session.BeginEdit(session.Notes[0].Id);
        Assert.IsFalse(session.HasChanges);
        session.UpdateDraft("discarded edit");
        session.CancelDraft();
        Assert.IsFalse(session.HasChanges);
        Assert.AreEqual("Original ʻōlelo note", session.Notes[0].Text);
    }

    [TestMethod]
    public void Lock_ProtectsNotesPendingChangesAndDraftUntilCorrectPassword()
    {
        using var session = new VaultSession(Source(), Password);
        session.UpdateDraft("pending note");
        Guid pending = session.SaveDraft();
        session.BeginEdit(session.Notes[0].Id);
        session.UpdateDraft("unfinished edit");
        Guid? editing = session.EditingNoteId;
        session.Lock();
        Assert.IsTrue(session.IsLocked);
        Assert.IsTrue(session.HasChanges);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = session.Notes);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = session.DraftText);
        Assert.ThrowsExactly<InvalidOperationException>(() => session.CreateExport(VaultExportFormat.Json));
        Assert.ThrowsExactly<VaultUnlockException>(() => session.Unlock("incorrect"));
        Assert.IsTrue(session.IsLocked);
        session.Unlock(Password);
        Assert.AreEqual("unfinished edit", session.DraftText);
        Assert.AreEqual(editing, session.EditingNoteId);
        Assert.HasCount(2, session.Notes);
        Assert.IsTrue(session.IsAdded(pending));
        Assert.AreEqual("unfinished edit", session.Snapshot().Notes[0].Text);
        Assert.HasCount(2, session.Snapshot().Notes);
    }

    [TestMethod]
    public void Lock_NewDraftAndRepeatedLockCyclesPreserveExactTextAndStableId()
    {
        using var session = new VaultSession(new VaultDocument(), Password);
        session.UpdateDraft("draft\r\n  unchanged  ");
        Guid draftId = session.Snapshot().Notes[0].Id;
        for (int i = 0; i < 2; i++)
        {
            session.Lock();
            session.Unlock(Password);
            Assert.IsEmpty(session.Notes);
            Assert.AreEqual("draft\r\n  unchanged  ", session.DraftText);
            Assert.AreEqual(draftId, session.Snapshot().Notes[0].Id);
        }
    }

    [TestMethod]
    public void EmptyEdit_CannotBeCommittedOrExportedButSurvivesLockAndCancel()
    {
        using var session = new VaultSession(Source(), Password);
        session.BeginEdit(session.Notes[0].Id);
        session.UpdateDraft(string.Empty);
        Assert.ThrowsExactly<ArgumentException>(() => session.CreateCommitPayload());
        Assert.ThrowsExactly<ArgumentException>(() => session.CreateExport(VaultExportFormat.Json));
        session.Lock();
        session.Unlock(Password);
        Assert.AreEqual(string.Empty, session.DraftText);
        session.CancelDraft();
        Assert.IsFalse(session.HasChanges);
        Assert.HasCount(1, session.Notes);
    }

    [TestMethod]
    public void PasswordChange_NewPayloadUsesNewPasswordWhileLiveSessionKeepsOriginal()
    {
        using var session = new VaultSession(Source(), Password);
        session.UpdateDraft("unsaved draft");
        byte[] rekeyed = session.CreatePasswordChangePayload(Password, "new-password");
        Assert.ThrowsExactly<VaultUnlockException>(() => VaultCrypto.Decrypt(rekeyed, Password));
        Assert.HasCount(2, VaultCrypto.Decrypt(rekeyed, "new-password").Notes);
        Assert.HasCount(2, VaultCrypto.Decrypt(session.CreateExport(VaultExportFormat.Encrypted), Password).Notes);
        session.Lock();
        session.Unlock(Password);
        Assert.AreEqual("unsaved draft", session.DraftText);
    }

    [TestMethod]
    public void PasswordChange_RejectsIncorrectCurrentPasswordAndBlankNewPassword()
    {
        using var session = new VaultSession(Source(), Password);
        Assert.ThrowsExactly<VaultUnlockException>(() => session.CreatePasswordChangePayload("wrong", "new-password"));
        Assert.ThrowsExactly<ArgumentException>(() => session.CreatePasswordChangePayload(Password, "    "));
        Assert.ThrowsExactly<ArgumentException>(() => session.CreatePasswordChangePayload(Password, "123"));
        Assert.IsFalse(session.HasChanges);
    }

    [TestMethod]
    public void Import_PreservesConflictingVersionsAndDeduplicatesRepeatedImport()
    {
        using var session = new VaultSession(Source(), Password);
        var imported = Source();
        imported.Notes[0].Text = "backup edit";
        Assert.AreEqual(1, session.Import(imported));
        Assert.HasCount(2, session.Notes);
        Assert.AreEqual("Original ʻōlelo note", session.Notes[0].Text);
        Assert.AreNotEqual(session.Notes[0].Id, session.Notes[1].Id);
        Assert.AreEqual(0, session.Import(imported));
        imported.Notes[0].Text = "external change";
        Assert.AreEqual("backup edit", session.Notes[1].Text);
    }

    [TestMethod]
    public void Exports_IncludePendingNotesAndActiveDraftWithStableMetadata()
    {
        using var session = new VaultSession(Source(), Password);
        session.UpdateDraft("pending");
        session.SaveDraft();
        session.UpdateDraft("new draft");
        byte[] encrypted = session.CreateExport(VaultExportFormat.Encrypted);
        var json = JsonSerializer.Deserialize<VaultDocument>(session.CreateExport(VaultExportFormat.Json),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var restored = VaultCrypto.Decrypt(encrypted, Password);
        Assert.HasCount(3, restored.Notes);
        CollectionAssert.AreEqual(restored.Notes.Select(note => note.Id).ToArray(), json.Notes.Select(note => note.Id).ToArray());
        Assert.AreEqual("new draft", session.DraftText);
        Assert.HasCount(2, session.Notes);
    }

    [TestMethod]
    public void DisposedSession_CannotUnlockOrExport()
    {
        var session = new VaultSession(Source(), Password);
        session.Lock();
        session.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => session.Unlock(Password));
        Assert.ThrowsExactly<ObjectDisposedException>(() => session.CreateCommitPayload());
    }

    [TestMethod]
    public void InvalidImport_LeavesCurrentVaultAndDraftUntouched()
    {
        using var session = new VaultSession(Source(), Password);
        session.UpdateDraft("keep my draft");
        var imported = Source();
        imported.Notes.Add(new NoteEntry { Id = imported.Notes[0].Id, Text = "duplicate identity" });
        Assert.ThrowsExactly<InvalidDataException>(() => session.Import(imported));
        Assert.HasCount(1, session.Notes);
        Assert.AreEqual("keep my draft", session.DraftText);
        Assert.AreEqual(0, session.ChangedCount);
    }

    [TestMethod]
    public void Import_PreservesDistinctIdentitiesEvenWhenTextAndTimestampMatch()
    {
        using var session = new VaultSession(new VaultDocument(), Password);
        var imported = Source();
        var note = imported.Notes[0];
        imported.Notes.Add(new NoteEntry { Id = Guid.NewGuid(), Text = note.Text, CreatedAt = note.CreatedAt });
        Assert.AreEqual(2, session.Import(imported));
        Assert.HasCount(2, session.Notes);
        Assert.AreEqual(0, session.Import(imported));
    }

    [TestMethod]
    public void DeleteLastSavedEntry_StagesEmptyVaultAndSurvivesLock()
    {
        var original = Source();
        using var session = new VaultSession(original, Password);
        session.DeleteNote(original.Notes[0].Id);
        Assert.IsEmpty(session.Notes);
        Assert.IsTrue(session.HasChanges);
        Assert.AreEqual(1, session.ChangedCount);
        Assert.HasCount(1, original.Notes);
        session.Lock();
        Assert.IsTrue(session.HasChanges);
        session.Unlock(Password);
        Assert.IsEmpty(VaultCrypto.Decrypt(session.CreateCommitPayload(), Password).Notes);
        Assert.IsEmpty(VaultCrypto.Decrypt(session.CreateExport(VaultExportFormat.Encrypted), Password).Notes);
    }

    [TestMethod]
    public void DeletePendingEntry_CancelsAdditionWithoutLeavingPhantomChanges()
    {
        using var session = new VaultSession(Source(), Password);
        session.UpdateDraft("pending entry");
        Guid pending = session.SaveDraft();
        session.DeleteNote(pending);
        Assert.HasCount(1, session.Notes);
        Assert.IsFalse(session.HasChanges);
        Assert.AreEqual(0, session.ChangedCount);
        Assert.IsFalse(session.IsAdded(pending));
    }

    [TestMethod]
    public void DeleteEditedEntry_ClearsItsDraftAndDoesNotResurrectItOnCommit()
    {
        using var session = new VaultSession(Source(), Password);
        Guid id = session.Notes[0].Id;
        session.BeginEdit(id);
        session.UpdateDraft("unfinished changes to deleted entry");
        session.DeleteNote(id);
        Assert.IsNull(session.EditingNoteId);
        Assert.AreEqual(string.Empty, session.DraftText);
        Assert.IsEmpty(session.Snapshot().Notes);
        Assert.IsEmpty(VaultCrypto.Decrypt(session.CreateCommitPayload(), Password).Notes);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DeleteDifferentEntry_PreservesNewDraftOrOtherActiveEdit(bool editingAnother)
    {
        var source = Source();
        var other = new NoteEntry { Text = "keep this entry" };
        source.Notes.Add(other);
        using var session = new VaultSession(source, Password);
        if (editingAnother) session.BeginEdit(other.Id);
        session.UpdateDraft("keep this unfinished draft");
        session.DeleteNote(source.Notes[0].Id);
        Assert.AreEqual("keep this unfinished draft", session.DraftText);
        Assert.AreEqual(editingAnother ? other.Id : (Guid?)null, session.EditingNoteId);
        Assert.HasCount(editingAnother ? 1 : 2, session.Snapshot().Notes);
        Assert.IsFalse(session.Snapshot().Notes.Any(note => note.Id == source.Notes[0].Id));
    }

    [TestMethod]
    public void Delete_RejectsMissingEntryAndLockedSessionWithoutChangingNotes()
    {
        using var session = new VaultSession(Source(), Password);
        Guid id = session.Notes[0].Id;
        Assert.ThrowsExactly<ArgumentException>(() => session.DeleteNote(Guid.NewGuid()));
        Assert.IsFalse(session.HasChanges);
        session.Lock();
        Assert.ThrowsExactly<InvalidOperationException>(() => session.DeleteNote(id));
        session.Unlock(Password);
        Assert.HasCount(1, session.Notes);
        Assert.IsFalse(session.HasChanges);
    }

    [TestMethod]
    public void DeleteReimportedSavedEntry_RemainsDirtySoOriginalDeletionCanBeCommitted()
    {
        var source = Source();
        using var session = new VaultSession(source, Password);
        Guid id = source.Notes[0].Id;
        session.DeleteNote(id);
        Assert.AreEqual(1, session.Import(source));
        session.DeleteNote(id);
        Assert.IsTrue(session.HasChanges);
        Assert.AreEqual(1, session.ChangedCount);
        Assert.IsEmpty(VaultCrypto.Decrypt(session.CreateCommitPayload(), Password).Notes);
    }
}
