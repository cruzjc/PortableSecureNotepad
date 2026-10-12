namespace PortableSecureNotepad.Ui;

internal sealed class PasswordSetupForm : Form
{
    private readonly TextBox _passwordBox = new();
    private readonly TextBox _confirmBox = new();

    public PasswordSetupForm()
    {
        Text = "Create Password/PIN";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(420, 220);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 4
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var intro = new Label
        {
            Text = "Create the password or PIN used to encrypt notes inside this EXE.",
            Dock = DockStyle.Fill,
            AutoSize = false
        };
        root.Controls.Add(intro, 0, 0);
        root.SetColumnSpan(intro, 2);

        root.Controls.Add(new Label { Text = "Password/PIN", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 1);
        ConfigurePasswordBox(_passwordBox);
        root.Controls.Add(_passwordBox, 1, 1);

        root.Controls.Add(new Label { Text = "Confirm", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 2);
        ConfigurePasswordBox(_confirmBox);
        root.Controls.Add(_confirmBox, 1, 2);

        var buttons = CreateButtons();
        root.Controls.Add(buttons, 0, 3);
        root.SetColumnSpan(buttons, 2);

        Controls.Add(root);
        AcceptButton = buttons.Controls.OfType<Button>().First(button => button.Text == "Create");
        CancelButton = buttons.Controls.OfType<Button>().First(button => button.DialogResult == DialogResult.Cancel);
    }

    public string Password { get; private set; } = string.Empty;

    private static void ConfigurePasswordBox(TextBox textBox)
    {
        textBox.Dock = DockStyle.Fill;
        textBox.UseSystemPasswordChar = true;
    }

    private FlowLayoutPanel CreateButtons()
    {
        var okButton = new Button
        {
            Text = "Create",
            AutoSize = true
        };
        okButton.Click += ValidatePassword;

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
        panel.Controls.Add(okButton);
        return panel;
    }

    private void ValidatePassword(object? sender, EventArgs e)
    {
        string password = _passwordBox.Text;
        if (password.Length < 4 || string.IsNullOrWhiteSpace(password))
        {
            MessageBox.Show(
                "Use at least 4 characters.",
                "Password/PIN required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        if (!string.Equals(password, _confirmBox.Text, StringComparison.Ordinal))
        {
            MessageBox.Show(
                "Password/PIN entries do not match.",
                "Confirmation failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        Password = password;
        DialogResult = DialogResult.OK;
        Close();
    }
}
