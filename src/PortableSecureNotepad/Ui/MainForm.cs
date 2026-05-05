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
    private readonly Button _commitButton = new();

    private List<NoteEntry> _filteredNotes = [];

    public MainForm(ExecutableVaultStore store, VaultDocument document, string password)
    {
        _store = store;
        _document = document;
        _password = password;

        Text = "Portable Secure Notepad";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 560);
        ClientSize = new Size(920, 650);

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
        _composeBox.PlaceholderText = "Type text to commit to the encrypted EXE...";
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
            ColumnCount = 2,
            RowCount = 1
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_statusLabel, 0, 0);

        _commitButton.Text = "Commit";
        _commitButton.Dock = DockStyle.Fill;
        _commitButton.Click += CommitNote;
        panel.Controls.Add(_commitButton, 1, 0);

        return panel;
    }

    private void RefreshNotes()
    {
        string query = _searchBox.Text.Trim();
        IEnumerable<NoteEntry> notes = _document.Notes.OrderByDescending(note => note.CreatedAt);

        if (!string.IsNullOrWhiteSpace(query))
        {
            notes = notes.Where(note => Matches(note, query));
        }

        _filteredNotes = notes.ToList();

        _noteList.BeginUpdate();
        try
        {
            _noteList.Items.Clear();
            foreach (NoteEntry note in _filteredNotes)
            {
                _noteList.Items.Add(new NoteListItem(note));
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

        _statusLabel.Text = $"{_filteredNotes.Count} of {_document.Notes.Count} notes";
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
            _previewBox.Text = item.Note.Text;
        }
        else
        {
            _previewBox.Clear();
        }
    }

    private void CommitNote(object? sender, EventArgs e)
    {
        string text = _composeBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            _statusLabel.Text = "Type text before committing.";
            return;
        }

        _commitButton.Enabled = false;
        UseWaitCursor = true;

        try
        {
            var updatedDocument = new VaultDocument
            {
                Notes = _document.Notes
                    .Select(note => new NoteEntry
                    {
                        Id = note.Id,
                        CreatedAt = note.CreatedAt,
                        Text = note.Text
                    })
                    .ToList()
            };
            updatedDocument.Notes.Add(new NoteEntry
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
                Text = text
            });

            byte[] encryptedPayload = VaultCrypto.Encrypt(updatedDocument, _password);
            ReplacementRequest request = _store.PrepareCommit(encryptedPayload);
            _store.StartReplacementHelper(request);

            MessageBox.Show(
                "Note committed. The app will restart to embed the encrypted vault inside this EXE.",
                "Commit saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            Application.Exit();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Commit failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            _commitButton.Enabled = true;
            UseWaitCursor = false;
        }
    }

    private sealed class NoteListItem
    {
        public NoteListItem(NoteEntry note)
        {
            Note = note;
        }

        public NoteEntry Note { get; }

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

            return $"{Note.CreatedAt.LocalDateTime:g}  {firstLine}";
        }
    }
}
