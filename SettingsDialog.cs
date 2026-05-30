using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

internal sealed class SettingsDialog : Form
{
    private readonly TextBox outputDirectoryBox = new TextBox();
    private string selectedOutputDirectory;

    public SettingsDialog(string outputDirectory)
    {
        Text = "设置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(620, 180);
        BackColor = Color.White;
        Font = new Font(AppStyles.UiFontName, 9f);

        var label = new Label();
        label.Left = 24;
        label.Top = 22;
        label.Width = 420;
        label.Height = 22;
        label.Text = "保存目录";
        label.ForeColor = AppStyles.OptionText;
        Controls.Add(label);

        outputDirectoryBox.Left = 24;
        outputDirectoryBox.Top = 50;
        outputDirectoryBox.Width = 450;
        outputDirectoryBox.Height = 26;
        outputDirectoryBox.Text = outputDirectory ?? string.Empty;
        Controls.Add(outputDirectoryBox);

        var browseButton = CreateDialogButton("浏览...", 486, 48);
        browseButton.Click += delegate { BrowseOutputDirectory(); };
        Controls.Add(browseButton);

        var clearButton = CreateDialogButton("清空", 486, 82);
        clearButton.Click += delegate { outputDirectoryBox.Text = string.Empty; };
        Controls.Add(clearButton);

        var hint = new Label();
        hint.Left = 24;
        hint.Top = 86;
        hint.Width = 450;
        hint.Height = 42;
        hint.Text = "留空：本地图片存原目录，剪贴板图片存 " + GetClipboardTempDirectory();
        hint.ForeColor = Color.FromArgb(95, 101, 110);
        Controls.Add(hint);

        var okButton = CreateDialogButton("保存", 404, 140);
        okButton.Click += delegate { SaveAndClose(); };
        Controls.Add(okButton);

        var cancelButton = CreateDialogButton("取消", 494, 140);
        cancelButton.DialogResult = DialogResult.Cancel;
        Controls.Add(cancelButton);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    public string OutputDirectory
    {
        get { return selectedOutputDirectory; }
    }

    private static Button CreateDialogButton(string text, int left, int top)
    {
        var button = new Button();
        button.Left = left;
        button.Top = top;
        button.Width = 82;
        button.Height = 28;
        button.Text = text;
        button.UseVisualStyleBackColor = true;
        return button;
    }

    private static string GetClipboardTempDirectory()
    {
        return Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private void BrowseOutputDirectory()
    {
        using (var dialog = new FolderBrowserDialog())
        {
            dialog.Description = "选择标注保存目录";
            dialog.ShowNewFolderButton = true;

            string current = outputDirectoryBox.Text.Trim();
            if (Directory.Exists(current))
            {
                dialog.SelectedPath = current;
            }

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                outputDirectoryBox.Text = dialog.SelectedPath;
            }
        }
    }

    private void SaveAndClose()
    {
        try
        {
            selectedOutputDirectory = AppSettingsStore.NormalizeDirectory(outputDirectoryBox.Text);
            if (!string.IsNullOrEmpty(selectedOutputDirectory))
            {
                Directory.CreateDirectory(selectedOutputDirectory);
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "图片标注", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
