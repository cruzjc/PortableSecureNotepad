using PortableSecureNotepad.Core;

namespace PortableSecureNotepad.Tests;

[TestClass]
public sealed class VaultDocumentBatchTests
{
    [TestMethod]
    public void CreateWithAppendedNotes_AppendsMultipleNotesWithoutMutatingSource()
    {
        var source = new VaultDocument
        {
            Notes =
            [
                new NoteEntry
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    CreatedAt = DateTimeOffset.Parse("2026-05-04T10:00:00-10:00"),
                    Text = "existing"
                }
            ]
        };
        NoteEntry firstPending = new()
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            CreatedAt = DateTimeOffset.Parse("2026-05-04T10:01:00-10:00"),
            Text = "first pending"
        };
        NoteEntry secondPending = new()
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            CreatedAt = DateTimeOffset.Parse("2026-05-04T10:02:00-10:00"),
            Text = "second pending"
        };

        VaultDocument result = VaultDocumentBatch.CreateWithAppendedNotes(
            source,
            [firstPending, secondPending]);

        Assert.AreEqual(1, source.Notes.Count);
        Assert.AreEqual(3, result.Notes.Count);
        Assert.AreEqual("existing", result.Notes[0].Text);
        Assert.AreEqual("first pending", result.Notes[1].Text);
        Assert.AreEqual("second pending", result.Notes[2].Text);

        firstPending.Text = "mutated after append";
        Assert.AreEqual("first pending", result.Notes[1].Text);
    }
}
