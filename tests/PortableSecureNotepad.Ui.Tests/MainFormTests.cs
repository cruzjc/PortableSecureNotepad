using PortableSecureNotepad.Core;
using PortableSecureNotepad.Ui;
using System.Runtime.ExceptionServices;

namespace PortableSecureNotepad.Ui.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MainFormTests
{
    [TestMethod]
    public void EditingAndStaging_UpdateTheSelectedNoteWithoutAppendingDuplicate() => OnSta(() =>
    {
        using var fixture = new FormFixture();
        using var form = new MainForm(fixture.Store, fixture.Session);
        ((ToolStripMenuItem)form.MainMenuStrip!.Items[1]).DropDownItems[0].PerformClick();
        var editor = (TextBox)form.Controls.Find("ComposeBox", true).Single();
        Assert.AreEqual("original note", editor.Text);
        editor.Text = "edited in the form";
        Capture(form, "editing.png");
        ((ToolStripMenuItem)form.MainMenuStrip!.Items[1]).DropDownItems[1].PerformClick();
        Assert.HasCount(1, fixture.Session.Notes);
        Assert.AreEqual(fixture.Id, fixture.Session.Notes[0].Id);
        Assert.AreEqual("edited in the form", fixture.Session.Notes[0].Text);
        Assert.AreEqual(string.Empty, editor.Text);
        StringAssert.Contains(((TextBox)form.Controls.Find("PreviewBox", true).Single()).Text, "edited in the form");
    });

    [TestMethod]
    public void Lock_ClearsAllNoteControlsAndDisablesExportsThenUnlockRestoresEditor() => OnSta(() =>
    {
        using var fixture = new FormFixture();
        using var form = new MainForm(fixture.Store, fixture.Session);
        form.BeginEdit(fixture.Id);
        var editor = (TextBox)form.Controls.Find("ComposeBox", true).Single();
        editor.Text = "unsaved editing draft";
        var search = (TextBox)form.Controls.Find("SearchBox", true).Single();
        search.Text = "original";
        ((ToolStripMenuItem)form.MainMenuStrip!.Items[2]).DropDownItems[1].PerformClick();
        Assert.AreEqual(string.Empty, editor.Text);
        Assert.AreEqual(string.Empty, search.Text);
        Assert.AreEqual(string.Empty, ((TextBox)form.Controls.Find("PreviewBox", true).Single()).Text);
        Assert.IsEmpty(((ListBox)form.Controls.Find("NoteList", true).Single()).Items);
        Assert.IsFalse(editor.CanUndo);
        Assert.IsFalse(form.MainMenuStrip!.Items[0].Enabled);
        Capture(form, "locked.png");
        Assert.ThrowsExactly<VaultUnlockException>(() => form.UnlockWorkspace("incorrect"));
        Assert.AreEqual(string.Empty, editor.Text);
        form.UnlockWorkspace("password");
        Assert.AreEqual("unsaved editing draft", editor.Text);
        Assert.AreEqual(fixture.Id, fixture.Session.EditingNoteId);
        Assert.IsTrue(form.MainMenuStrip.Items[0].Enabled);
        Capture(form, "unlocked.png");
    });

    [TestMethod]
    public void NoteEditor_DoesNotTruncateLongNotes() => OnSta(() =>
    {
        using var fixture = new FormFixture();
        using var form = new MainForm(fixture.Store, fixture.Session);
        string longNote = new('x', 40_000);
        var editor = (TextBox)form.Controls.Find("ComposeBox", true).Single();
        editor.Text = longNote;
        form.SaveDraft();
        Assert.AreEqual(longNote, fixture.Session.Notes.Single(note => note.Id != fixture.Id).Text);
    });

    [TestMethod]
    public void PasswordChangeDialog_HasThreeMaskedFieldsAndExplainsBackupPassword() => OnSta(() =>
    {
        using var dialog = new PasswordSetupForm(changePassword: true);
        var all = Descendants(dialog).ToArray();
        Assert.HasCount(3, all.OfType<TextBox>());
        Assert.IsTrue(all.OfType<TextBox>().All(field => field.UseSystemPasswordChar));
        Assert.IsTrue(all.OfType<Label>().Any(label => label.Text.Contains("backups keep their original password")));
        Capture(dialog, "password-change.png");
    });

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            yield return control;
            foreach (Control nested in Descendants(control)) yield return nested;
        }
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Capture(Form form, string filename)
    {
        string? directory = Environment.GetEnvironmentVariable("PSN_UI_ARTIFACTS_DIR");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        form.ShowInTaskbar = false;
        form.Opacity = 0;
        form.Show();
        Application.DoEvents();
        form.PerformLayout();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(directory, filename));
        form.Hide();
    }

    private sealed class FormFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"psn-form-{Guid.NewGuid():N}");
        public Guid Id { get; } = Guid.NewGuid();
        public VaultSession Session { get; }
        public ExecutableVaultStore Store { get; }
        public FormFixture()
        {
            Directory.CreateDirectory(_root);
            string exe = Path.Combine(_root, "fixture.exe");
            File.WriteAllText(exe, "inert executable");
            Store = new ExecutableVaultStore(exe);
            Session = new VaultSession(new VaultDocument { Notes = [new NoteEntry { Id = Id, Text = "original note" }] }, "password");
        }
        public void Dispose() { Session.Dispose(); Directory.Delete(_root, recursive: true); }
    }
}
