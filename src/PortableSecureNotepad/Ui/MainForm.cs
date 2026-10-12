using PortableSecureNotepad.Core;
using System.Security.Cryptography;

namespace PortableSecureNotepad.Ui;

internal sealed class MainForm : Form
{
    private readonly ExecutableVaultStore _store;
    private readonly VaultSession _session;
    private readonly Func<NoteEntry, bool, bool> _confirmDeletion;
    private readonly TextBox _searchBox = new() { Name = "SearchBox", AccessibleName = "Search notes" };
    private readonly ListBox _noteList = new() { Name = "NoteList", AccessibleName = "Note entries" };
    private readonly TextBox _previewBox = new() { Name = "PreviewBox", AccessibleName = "Selected note", MaxLength = 0 };
    private readonly TextBox _composeBox = new() { Name = "ComposeBox", AccessibleName = "Note editor", MaxLength = 0 };
    private readonly Label _editorLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Button _stageButton = new();
    private readonly Button _commitButton = new() { Name = "CommitButton", AccessibleName = "Commit all changes" };
    private readonly Button _editButton = new();
    private readonly Button _deleteButton = new() { Name = "DeleteSelectedButton", AccessibleName = "Delete selected entry" };
    private readonly Button _cancelEditButton = new();
    private readonly MenuStrip _menu = new();
    private readonly ToolStripMenuItem _fileMenu = new("&File");
    private readonly ToolStripMenuItem _noteMenu = new("&Note");
    private readonly ToolStripMenuItem _deleteItem = new("&Delete selected...")
    {
        Name = "DeleteSelected", ShortcutKeys = Keys.Control | Keys.Shift | Keys.D
    };
    private readonly ToolStripMenuItem _changePasswordItem = new("Change &password/PIN...");
    private readonly ToolStripMenuItem _lockItem = new("&Lock now") { ShortcutKeys = Keys.Control | Keys.L };
    private readonly TableLayoutPanel _layout = new();
    private bool _isCommitting;
    private bool _updatingView;

    public MainForm(ExecutableVaultStore store, VaultSession session, Func<NoteEntry, bool, bool>? confirmDeletion = null)
    {
        _store = store;
        _session = session;
        _confirmDeletion = confirmDeletion ?? ConfirmDeletion;
        Text = "Portable Secure Notepad";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 620);
        ClientSize = new Size(960, 720);
        FormClosing += ConfirmCloseWithPendingNotes;
        BuildLayout();
        BuildMenu();
        Controls.Add(_layout);
        Controls.Add(_menu);
        MainMenuStrip = _menu;
        RefreshWorkspace();
    }

    private void BuildMenu()
    {
        _fileMenu.DropDownItems.Add(Command("&Encrypted backup...", Keys.Control | Keys.Shift | Keys.E, () => Export(VaultExportFormat.Encrypted)));
        _fileMenu.DropDownItems.Add(Command("Export unencrypted &JSON...", Keys.None, () => Export(VaultExportFormat.Json)));
        _fileMenu.DropDownItems.Add(Command("Export unencrypted &text...", Keys.None, () => Export(VaultExportFormat.Text)));
        _fileMenu.DropDownItems.Add(new ToolStripSeparator());
        _fileMenu.DropDownItems.Add(Command("&Import backup...", Keys.Control | Keys.Shift | Keys.I, ImportBackup));
        _noteMenu.DropDownItems.Add(Command("&Edit selected", Keys.Control | Keys.E, EditSelected));
        _noteMenu.DropDownItems.Add(Command("&Save entry / update", Keys.Control | Keys.S, SaveDraft));
        _noteMenu.DropDownItems.Add(Command("&Commit all", Keys.Control | Keys.Shift | Keys.S, () => CommitChanges()));
        _noteMenu.DropDownItems.Add(new ToolStripSeparator());
        _deleteItem.Click += (_, _) => Execute(DeleteSelected);
        _noteMenu.DropDownItems.Add(_deleteItem);
        _changePasswordItem.Click += (_, _) => Execute(ChangePassword);
        _lockItem.Click += (_, _) => Execute(ToggleLock);
        var vaultMenu = new ToolStripMenuItem("&Vault");
        vaultMenu.DropDownItems.AddRange([_changePasswordItem, _lockItem]);
        _menu.Items.AddRange([_fileMenu, _noteMenu, vaultMenu]);
    }

    private ToolStripMenuItem Command(string text, Keys shortcut, Action action)
    {
        var command = new ToolStripMenuItem(text) { ShortcutKeys = shortcut };
        command.Click += (_, _) => Execute(action);
        return command;
    }

    private void BuildLayout()
    {
        _layout.Dock = DockStyle.Fill;
        _layout.Padding = new Padding(14);
        _layout.ColumnCount = 1;
        _layout.RowCount = 5;
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var search = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        search.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        search.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        search.Controls.Add(new Label { Text = "Search", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _searchBox.Dock = DockStyle.Fill;
        _searchBox.PlaceholderText = "Filter notes as you type";
        _searchBox.TextChanged += (_, _) => { if (!_updatingView && !_session.IsLocked) RefreshNotes(); };
        search.Controls.Add(_searchBox, 1, 0);
        _layout.Controls.Add(search, 0, 0);
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(920, 300), SplitterDistance = 360 };
        _noteList.Dock = DockStyle.Fill;
        _noteList.IntegralHeight = false;
        _noteList.SelectedIndexChanged += (_, _) => UpdatePreview();
        _noteList.DoubleClick += (_, _) => Execute(EditSelected);
        split.Panel1.Controls.Add(_noteList);
        _previewBox.Dock = DockStyle.Fill;
        _previewBox.Multiline = true;
        _previewBox.ReadOnly = true;
        _previewBox.ScrollBars = ScrollBars.Vertical;
        _previewBox.BackColor = SystemColors.Window;
        split.Panel2.Controls.Add(_previewBox);
        _layout.Controls.Add(split, 0, 1);
        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        _editorLabel.Dock = DockStyle.Fill;
        _editorLabel.TextAlign = ContentAlignment.MiddleLeft;
        editor.Controls.Add(_editorLabel, 0, 0);
        _editButton.Text = "Edit selected";
        _editButton.Dock = DockStyle.Fill;
        _editButton.Click += (_, _) => Execute(EditSelected);
        editor.Controls.Add(_editButton, 1, 0);
        _deleteButton.Text = "Delete selected";
        _deleteButton.Dock = DockStyle.Fill;
        _deleteButton.Click += (_, _) => Execute(DeleteSelected);
        editor.Controls.Add(_deleteButton, 2, 0);
        _cancelEditButton.Text = "Cancel edit";
        _cancelEditButton.Dock = DockStyle.Fill;
        _cancelEditButton.Click += (_, _) => Execute(CancelEdit);
        editor.Controls.Add(_cancelEditButton, 3, 0);
        _layout.Controls.Add(editor, 0, 2);
        _composeBox.Dock = DockStyle.Fill;
        _composeBox.Multiline = true;
        _composeBox.ScrollBars = ScrollBars.Vertical;
        _composeBox.PlaceholderText = "Type a note entry...";
        _composeBox.TextChanged += (_, _) =>
        {
            if (_updatingView || _session.IsLocked) return;
            _session.UpdateDraft(_composeBox.Text);
            UpdateButtonStates();
        };
        _layout.Controls.Add(_composeBox, 0, 3);
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        bottom.Controls.Add(_statusLabel, 0, 0);
        _stageButton.Dock = DockStyle.Fill;
        _stageButton.Click += (_, _) => Execute(SaveDraft);
        bottom.Controls.Add(_stageButton, 1, 0);
        _commitButton.Text = "Commit All";
        _commitButton.Dock = DockStyle.Fill;
        _commitButton.Click += (_, _) => Execute(() => CommitChanges());
        bottom.Controls.Add(_commitButton, 2, 0);
        _layout.Controls.Add(bottom, 0, 4);
    }

    private void RefreshWorkspace(Guid? selectedId = null)
    {
        _updatingView = true;
        try { _composeBox.Text = _session.DraftText; }
        finally { _updatingView = false; }
        _editorLabel.Text = _session.EditingNoteId is null ? "New entry" : "Editing entry — update, then Commit All to save";
        RefreshNotes(selectedId);
    }

    private void RefreshNotes(Guid? selectedId = null)
    {
        selectedId ??= (_noteList.SelectedItem as NoteListItem)?.Note.Id;
        string query = _searchBox.Text.Trim();
        IReadOnlyList<NoteEntry> all = _session.Notes;
        var items = all.Where(note => string.IsNullOrEmpty(query) || note.Text.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            note.CreatedAt.LocalDateTime.ToString("g").Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(note => note.CreatedAt)
            .Select(note => new NoteListItem(note, _session.IsAdded(note.Id), _session.IsChanged(note.Id))).ToArray();
        _noteList.BeginUpdate();
        try
        {
            _noteList.Items.Clear();
            _noteList.Items.AddRange(items);
            int selected = Array.FindIndex(items, item => item.Note.Id == selectedId);
            if (items.Length > 0) _noteList.SelectedIndex = Math.Max(0, selected);
        }
        finally { _noteList.EndUpdate(); }
        UpdatePreview();
        _statusLabel.Text = $"{items.Length} of {all.Count} entries visible; {_session.ChangedCount} uncommitted changes";
        UpdateButtonStates();
    }

    private void UpdatePreview()
    {
        if (_noteList.SelectedItem is NoteListItem item)
        {
            string state = item.IsChanged ? "Uncommitted change — use Commit All to save.\r\n\r\n" : string.Empty;
            string edited = item.Note.UpdatedAt is DateTimeOffset at ? $"Edited {at.LocalDateTime:g}\r\n\r\n" : string.Empty;
            _previewBox.Text = state + edited + item.Note.Text;
        }
        else _previewBox.Clear();
        _editButton.Enabled = !_isCommitting && !_session.IsLocked && _noteList.SelectedItem is not null;
        _deleteButton.Enabled = _editButton.Enabled;
        _deleteItem.Enabled = _editButton.Enabled;
    }

    internal void SaveDraft()
    {
        if (_session.IsLocked || _isCommitting) return;
        Guid saved = _session.SaveDraft();
        RefreshWorkspace(saved);
    }

    private void EditSelected()
    {
        if (_session.IsLocked || _noteList.SelectedItem is not NoteListItem item) return;
        if (_session.HasDraftChanges)
        {
            DialogResult answer = MessageBox.Show(this, "Save the current draft before editing this entry?", "Current draft",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3);
            if (answer == DialogResult.Cancel) return;
            if (answer == DialogResult.Yes) _session.SaveDraft();
            else _session.CancelDraft();
        }
        BeginEdit(item.Note.Id);
    }

    internal void BeginEdit(Guid id)
    {
        _session.BeginEdit(id);
        RefreshWorkspace(id);
        _composeBox.Focus();
    }

    private void CancelEdit()
    {
        if (_session.HasDraftChanges && MessageBox.Show(this, "Discard the changes in the editor?", "Cancel edit",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        _session.CancelDraft();
        RefreshWorkspace();
    }

    private bool ConfirmDeletion(NoteEntry note, bool discardingEditor)
    {
        string editorWarning = discardingEditor ? "\r\nThis also discards the editor text for this entry." : string.Empty;
        return MessageBox.Show(this,
            $"Delete the selected entry created {note.CreatedAt.LocalDateTime:g}?{editorWarning}\r\nUse Commit All to save the deletion to the EXE.",
            "Delete selected entry", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private void DeleteSelected()
    {
        if (_session.IsLocked || _isCommitting || _noteList.SelectedItem is not NoteListItem item) return;
        if (!_confirmDeletion(item.Note, _session.EditingNoteId == item.Note.Id)) return;
        _session.DeleteNote(item.Note.Id);
        RefreshWorkspace();
    }

    private void CommitChanges(byte[]? preparedPayload = null)
    {
        if (_session.IsLocked || _isCommitting) return;
        if (preparedPayload is null && !_session.HasChanges) return;
        byte[] payload = preparedPayload ?? _session.CreateCommitPayload();
        _isCommitting = true;
        _layout.Enabled = false;
        _menu.Enabled = false;
        UseWaitCursor = true;
        try
        {
            ReplacementRequest request = _store.PrepareCommit(payload);
            _store.StartReplacementHelper(request);
            _statusLabel.Text = "Commit prepared. Restarting to save and verify the encrypted vault...";
            Application.Exit();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
        {
            _isCommitting = false;
            _layout.Enabled = true;
            _menu.Enabled = true;
            UseWaitCursor = false;
            UpdateButtonStates();
            MessageBox.Show(this, $"{ex.Message}\r\n\r\nYour entries remain in this window. Prepared pending EXEs are preserved in:\r\n{Path.GetDirectoryName(_store.ExecutablePath)}",
                "Commit failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { CryptographicOperations.ZeroMemory(payload); }
    }

    private void ChangePassword()
    {
        if (_session.IsLocked) return;
        using var dialog = new PasswordSetupForm(changePassword: true);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        CommitChanges(_session.CreatePasswordChangePayload(dialog.CurrentPassword, dialog.Password));
    }

    private void Export(VaultExportFormat format)
    {
        if (_session.IsLocked) return;
        bool plain = format != VaultExportFormat.Encrypted;
        if (plain && MessageBox.Show(this, "This export contains readable, unencrypted notes, including uncommitted changes and the draft. Continue?",
            "Unencrypted export", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        string extension = format switch { VaultExportFormat.Encrypted => "psnvault", VaultExportFormat.Json => "json", _ => "txt" };
        using var dialog = new SaveFileDialog
        {
            Title = plain ? "Export unencrypted notes" : "Save encrypted backup",
            Filter = $"Notepad {extension} file|*.{extension}", DefaultExt = extension, AddExtension = true,
            FileName = $"Notepad-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}", OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        byte[] bytes = _session.CreateExport(format);
        try { VaultTransfer.WriteExport(dialog.FileName, bytes, format); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        _statusLabel.Text = $"Export saved to {dialog.FileName}. EXE changes still require Commit All.";
    }

    private void ImportBackup()
    {
        if (_session.IsLocked) return;
        using var dialog = new OpenFileDialog
        {
            Title = "Import backup as uncommitted notes",
            Filter = "Notepad backups|*.psnvault;*.json;*.exe|Encrypted vault|*.psnvault|JSON backup|*.json|Portable EXE backup|*.exe"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        VaultDocument document;
        if (Path.GetExtension(dialog.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase)) document = VaultTransfer.ReadPlainJson(dialog.FileName);
        else
        {
            byte[] payload = VaultTransfer.ReadEncryptedBackup(dialog.FileName);
            try
            {
                while (true)
                {
                    using var unlock = new UnlockForm("Unlock Backup", "Enter the password/PIN used for this backup.");
                    if (unlock.ShowDialog(this) != DialogResult.OK) return;
                    try { document = VaultCrypto.Decrypt(payload, unlock.Password); break; }
                    catch (VaultUnlockException ex) { MessageBox.Show(this, ex.Message, "Unlock failed", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                    catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "Password required", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
            }
            finally { CryptographicOperations.ZeroMemory(payload); }
        }
        int added = _session.Import(document);
        RefreshNotes();
        _statusLabel.Text = $"{added} entries imported. Existing entries were preserved; use Commit All to save.";
    }

    private void ToggleLock()
    {
        if (!_session.IsLocked)
        {
            LockWorkspace();
            return;
        }
        while (_session.IsLocked)
        {
            using var unlock = new UnlockForm("Unlock Workspace", "Enter this notepad's password/PIN to resume your notes and draft.");
            if (unlock.ShowDialog(this) != DialogResult.OK) return;
            try { UnlockWorkspace(unlock.Password); }
            catch (VaultUnlockException ex) { MessageBox.Show(this, ex.Message, "Unlock failed", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "Password required", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }

    internal void LockWorkspace()
    {
        _session.Lock();
        _updatingView = true;
        try
        {
            _composeBox.Clear();
            _searchBox.Clear();
            _noteList.Items.Clear();
            _previewBox.Clear();
            _composeBox.ClearUndo();
            _searchBox.ClearUndo();
            _previewBox.ClearUndo();
        }
        finally { _updatingView = false; }
        _editorLabel.Text = "Locked";
        _statusLabel.Text = "Locked. Use Vault → Unlock or Ctrl+L to resume.";
        _layout.Enabled = false;
        Text = "Portable Secure Notepad — Locked";
        UpdateButtonStates();
    }

    internal void UnlockWorkspace(string password)
    {
        _session.Unlock(password);
        _layout.Enabled = true;
        Text = "Portable Secure Notepad";
        RefreshWorkspace(_session.EditingNoteId);
    }

    private void UpdateButtonStates()
    {
        bool unlocked = !_session.IsLocked && !_isCommitting;
        _fileMenu.Enabled = unlocked;
        _noteMenu.Enabled = unlocked;
        _changePasswordItem.Enabled = unlocked;
        _lockItem.Enabled = !_isCommitting;
        _lockItem.Text = _session.IsLocked ? "&Unlock..." : "&Lock now";
        _stageButton.Text = _session.EditingNoteId is null ? "Save Entry" : "Update Entry";
        _stageButton.Enabled = unlocked && !string.IsNullOrWhiteSpace(_session.DraftText);
        _commitButton.Enabled = unlocked && _session.HasChanges;
        _cancelEditButton.Enabled = unlocked && _session.EditingNoteId is not null;
        _editButton.Enabled = unlocked && _noteList.SelectedItem is not null;
        _deleteButton.Enabled = _editButton.Enabled;
        _deleteItem.Enabled = _editButton.Enabled;
        if (unlocked)
        {
            string draft = _session.HasDraftChanges ? "; unsaved draft" : string.Empty;
            _statusLabel.Text = $"{_noteList.Items.Count} of {_session.NoteCount} entries; {_session.ChangedCount} staged changes{draft}";
        }
    }

    private void ConfirmCloseWithPendingNotes(object? sender, FormClosingEventArgs e)
    {
        if (_isCommitting || !_session.HasChanges) return;
        e.Cancel = MessageBox.Show(this, "Close without committing changes and the draft?", "Uncommitted work",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes;
    }

    private void Execute(Action action)
    {
        if (_isCommitting) return;
        try { action(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or VaultUnlockException)
        {
            MessageBox.Show(this, ex.Message, "Notepad operation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private sealed record NoteListItem(NoteEntry Note, bool IsAdded, bool IsChanged)
    {
        public override string ToString()
        {
            string firstLine = Note.Text.Split(["\r\n", "\n"], StringSplitOptions.None)[0].Trim();
            if (firstLine.Length > 70) firstLine = firstLine[..67] + "...";
            string state = IsAdded ? "[pending] " : IsChanged ? "[edited] " : string.Empty;
            return $"{state}{Note.CreatedAt.LocalDateTime:g}  {firstLine}";
        }
    }
}
