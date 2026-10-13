namespace PortableSecureNotepad.Ui;

internal sealed class UnlockForm : Form
{
    private readonly TextBox _passwordBox = new();

    public UnlockForm(string title = "Unlock Notes", string message = "Enter the password or PIN for this portable notepad.")
    {
        Icon = AppIcon.Value;
        Text = title;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(400, 170);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 3
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var intro = new Label
        {
            Text = message,
            Dock = DockStyle.Fill,
            AutoSize = false
        };
        root.Controls.Add(intro, 0, 0);
        root.SetColumnSpan(intro, 2);

        root.Controls.Add(new Label { Text = "Password/PIN", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 1);
        _passwordBox.Dock = DockStyle.Fill;
        _passwordBox.UseSystemPasswordChar = true;
        root.Controls.Add(_passwordBox, 1, 1);

        var buttons = CreateButtons();
        root.Controls.Add(buttons, 0, 2);
        root.SetColumnSpan(buttons, 2);

        Controls.Add(root);
        AcceptButton = buttons.Controls.OfType<Button>().First(button => button.DialogResult == DialogResult.OK);
        CancelButton = buttons.Controls.OfType<Button>().First(button => button.DialogResult == DialogResult.Cancel);
    }

    public string Password => _passwordBox.Text;

    private static FlowLayoutPanel CreateButtons()
    {
        var unlockButton = new Button
        {
            Text = "Unlock",
            DialogResult = DialogResult.OK,
            AutoSize = true
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true
        };

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 14, 0, 0)
        };
        panel.Controls.Add(cancelButton);
        panel.Controls.Add(unlockButton);
        return panel;
    }
}
