using PortableSecureNotepad.Core;

namespace PortableSecureNotepad.Ui;

internal sealed class MainForm : Form
{
    private readonly ExecutableVaultStore _store;
    private readonly VaultDocument _document;
    private readonly string _password;
    private readonly TextBox _searchBox = new();
    private readonly ListBox _noteList = new();
    private readonly TextBox _previewBox = new();
    private readonly TextBox _composeBox = new();
    private readonly Label _statusLabel = new();
    private readonly Button _stageButton = new();
    private readonly Button _commitButton = new();
    private readonly List<NoteEntry> _pendingNotes = [];

    private List<NoteListItem> _filteredItems = [];
    private bool _isCommitting;

    public MainForm(ExecutableVaultStore store, VaultDocument document, string password)
    {
        _store = store;
        _document = document;
        _password = password;

        Text = "Portable Secure Notepad";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 560);
        ClientSize = new Size(920, 650);
        FormClosing += ConfirmCloseWithPendingNotes;

        Controls.Add(BuildLayout());
        RefreshNotes();
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        root.Controls.Add(BuildSearchRow(), 0, 0);
        root.Controls.Add(BuildNotesPanel(), 0, 1);

        _composeBox.Dock = DockStyle.Fill;
        _composeBox.Multiline = true;
        _composeBox.ScrollBars = ScrollBars.Vertical;
        _composeBox.PlaceholderText = "Type a note entry...";
        _composeBox.TextChanged += (_, _) => UpdateButtonStates();
        root.Controls.Add(_composeBox, 0, 2);

        root.Controls.Add(BuildBottomRow(), 0, 3);
        return root;
    }

    private Control BuildSearchRow()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        panel.Controls.Add(new Label
        {
            Text = "Search",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _searchBox.Dock = DockStyle.Fill;
        _searchBox.PlaceholderText = "Filter notes as you type";
        _searchBox.TextChanged += (_, _) => RefreshNotes();
        panel.Controls.Add(_searchBox, 1, 0);

        return panel;
    }

    private Control BuildNotesPanel()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 340
        };

        _noteList.Dock = DockStyle.Fill;
        _noteList.IntegralHeight = false;
        _noteList.SelectedIndexChanged += (_, _) => UpdatePreview();
        split.Panel1.Controls.Add(_noteList);

        _previewBox.Dock = DockStyle.Fill;
        _previewBox.Multiline = true;
        _previewBox.ReadOnly = true;
        _previewBox.ScrollBars = ScrollBars.Vertical;
        _previewBox.BackColor = SystemColors.Window;
        split.Panel2.Controls.Add(_previewBox);

        return split;
    }

    private Control BuildBottomRow()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_statusLabel, 0, 0);

        _stageButton.Text = "Save Entry";
        _stageButton.Dock = DockStyle.Fill;
        _stageButton.Click += SavePendingEntry;
        panel.Controls.Add(_stageButton, 1, 0);

        _commitButton.Text = "Commit All";
        _commitButton.Dock = DockStyle.Fill;
        _commitButton.Click += CommitPendingEntries;
        panel.Controls.Add(_commitButton, 2, 0);

        return panel;
    }

    private void RefreshNotes()
    {
        string query = _searchBox.Text.Trim();
        IEnumerable<NoteListItem> items = _document.Notes
            .Select(note => new NoteListItem(note, isPending: false))
            .Concat(_pendingNotes.Select(note => new NoteListItem(note, isPending: true)))
            .OrderByDescending(item => item.Note.CreatedAt);

        if (!string.IsNullOrWhiteSpace(query))
        {
            items = items.Where(item => Matches(item.Note, query));
        }

        _filteredItems = items.ToList();

        _noteList.BeginUpdate();
        try
        {
            _noteList.Items.Clear();
            foreach (NoteListItem item in _filteredItems)
            {
                _noteList.Items.Add(item);
            }
        }
        finally
        {
            _noteList.EndUpdate();
        }

        if (_noteList.Items.Count > 0)
        {
            _noteList.SelectedIndex = 0;
        }
        else
        {
            _previewBox.Clear();
        }

        int totalCount = _document.Notes.Count + _pendingNotes.Count;
        string pendingText = _pendingNotes.Count == 1 ? "1 pending" : $"{_pendingNotes.Count} pending";
        _statusLabel.Text = $"{_filteredItems.Count} of {totalCount} entries visible; {pendingText}";
        UpdateButtonStates();
    }

    private static bool Matches(NoteEntry note, string query)
    {
        return note.Text.Contains(query, StringComparison.OrdinalIgnoreCase)
            || note.CreatedAt.LocalDateTime.ToString("g").Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdatePreview()
    {
        if (_noteList.SelectedItem is NoteListItem item)
        {
            _previewBox.Text = item.IsPending
                ? $"Pending entry - not embedded in the EXE yet.{Environment.NewLine}{Environment.NewLine}{item.Note.Text}"
                : item.Note.Text;
        }
        else
        {
            _previewBox.Clear();
        }
    }

    private void SavePendingEntry(object? sender, EventArgs e)
    {
        string text = GetComposedText();
        if (string.IsNullOrWhiteSpace(text))
        {
            _statusLabel.Text = "Type text before saving an entry.";
            return;
        }

        _pendingNotes.Add(CreateNote(text));
        _composeBox.Clear();
        RefreshNotes();
    }

    private void CommitPendingEntries(object? sender, EventArgs e)
    {
        List<NoteEntry> notesToCommit = GetCommitBatch();
        if (notesToCommit.Count == 0)
        {
            _statusLabel.Text = "Save or type an entry before committing.";
            return;
        }

        _isCommitting = true;
        _stageButton.Enabled = false;
        _commitButton.Enabled = false;
        UseWaitCursor = true;

        try
        {
            VaultDocument updatedDocument = VaultDocumentBatch.CreateWithAppendedNotes(_document, notesToCommit);
            byte[] encryptedPayload = VaultCrypto.Encrypt(updatedDocument, _password);
            ReplacementRequest request = _store.PrepareCommit(encryptedPayload);
            _store.StartReplacementHelper(request);

            _statusLabel.Text = "Commit prepared. Restarting to save and verify the encrypted vault...";
            Application.Exit();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"{ex.Message}{Environment.NewLine}{Environment.NewLine}Your entries remain in this window. Any prepared pending EXE is preserved in:{Environment.NewLine}{Path.GetDirectoryName(_store.ExecutablePath)}",
                "Commit failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            _isCommitting = false;
            UpdateButtonStates();
            UseWaitCursor = false;
        }
    }

    private List<NoteEntry> GetCommitBatch()
    {
        var notesToCommit = _pendingNotes
            .Select(note => new NoteEntry
            {
                Id = note.Id,
                CreatedAt = note.CreatedAt,
                Text = note.Text
            })
            .ToList();

        string text = GetComposedText();
        if (!string.IsNullOrWhiteSpace(text))
        {
            notesToCommit.Add(CreateNote(text));
        }

        return notesToCommit;
    }

    private string GetComposedText()
    {
        return _composeBox.Text.Trim();
    }

    private static NoteEntry CreateNote(string text)
    {
        return new NoteEntry
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            Text = text
        };
    }

    private void UpdateButtonStates()
    {
        if (_isCommitting)
        {
            _stageButton.Enabled = false;
            _commitButton.Enabled = false;
            return;
        }

        bool hasComposedText = !string.IsNullOrWhiteSpace(GetComposedText());
        _stageButton.Enabled = hasComposedText;
        _commitButton.Enabled = hasComposedText || _pendingNotes.Count > 0;
    }

    private void ConfirmCloseWithPendingNotes(object? sender, FormClosingEventArgs e)
    {
        if (_isCommitting)
        {
            return;
        }

        bool hasUnsavedWork = _pendingNotes.Count > 0 || !string.IsNullOrWhiteSpace(GetComposedText());
        if (!hasUnsavedWork)
        {
            return;
        }

        DialogResult result = MessageBox.Show(
            "Close without committing pending entries?",
            "Pending entries",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        e.Cancel = result != DialogResult.Yes;
    }

    private sealed class NoteListItem
    {
        public NoteListItem(NoteEntry note, bool isPending)
        {
            Note = note;
            IsPending = isPending;
        }

        public NoteEntry Note { get; }

        public bool IsPending { get; }

        public override string ToString()
        {
            string firstLine = Note.Text
                .Split(["\r\n", "\n"], StringSplitOptions.None)
                .FirstOrDefault()?
                .Trim() ?? string.Empty;

            if (firstLine.Length > 70)
            {
                firstLine = firstLine[..67] + "...";
            }

            string state = IsPending ? "[pending] " : string.Empty;
            return $"{state}{Note.CreatedAt.LocalDateTime:g}  {firstLine}";
        }
    }
}
