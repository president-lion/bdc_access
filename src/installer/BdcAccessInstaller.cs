// ---------------------------------------------------------------------------
// bdc_access - a windowed installer.
//
// This is a front end and nothing more: it finds the game, then runs the same
// Install.bat / Uninstall.bat that a command line would, and shows what they
// print. There is deliberately no second copy of the install logic here - one
// installer that can drift from the other is worse than no window at all.
//
// Built against .NET Framework 4, which is on every Windows 10 and 11 machine,
// so there is no runtime to install and nothing to unpack.
//
// Accessibility notes, since that is the whole point of the mod:
//   * every control carries an AccessibleName and, where it is not obvious,
//     an AccessibleDescription saying what it will do;
//   * every button has a mnemonic (Alt+I, Alt+U, Alt+B, Alt+C);
//   * Browse opens a *file* dialog for data.win rather than the old folder
//     tree, because the file dialog is the one a screen reader handles well
//     and the one you can paste a path into;
//   * progress is written to a read-only text box that can be reviewed at any
//     time, and the outcome is also announced in a message box, which every
//     screen reader reads without being asked;
//   * the path box is focused at startup with the detected folder already in
//     it, so Alt+I is a complete install.
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;

public class InstallerForm : Form
{
    private TextBox pathBox;
    private Button browseButton;
    private Button installButton;
    private Button uninstallButton;
    private Button closeButton;
    private TextBox logBox;
    private Label statusLabel;
    private string modDir;
    private bool busy;

    public InstallerForm()
    {
        modDir = Path.GetDirectoryName(Application.ExecutablePath);

        Text = "bdc_access - accessibility for Bad Dream: Coma";
        AccessibleName = Text;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(640, 460);
        ClientSize = new Size(700, 500);
        Font = SystemFonts.MessageBoxFont;

        Label intro = new Label();
        intro.Text = "This adds screen-reader speech and keyboard control to Bad Dream: Coma. " +
                     "Close the game first. Uninstall puts it back exactly as it was.";
        intro.SetBounds(12, 10, 660, 40);
        intro.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        Label pathLabel = new Label();
        pathLabel.Text = "&Game folder (the folder that holds data.win):";
        pathLabel.SetBounds(12, 60, 660, 20);
        pathLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        pathBox = new TextBox();
        pathBox.SetBounds(12, 82, 560, 24);
        pathBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pathBox.AccessibleName = "Game folder";
        pathBox.AccessibleDescription = "The folder holding data.win. Type or paste it, or use the Browse button.";

        browseButton = new Button();
        browseButton.Text = "&Browse...";
        browseButton.SetBounds(580, 81, 92, 26);
        browseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        browseButton.AccessibleName = "Browse";
        browseButton.AccessibleDescription = "Opens a file dialog. Choose the game's data.win file; " +
                                             "the mod is installed into the folder holding it.";
        browseButton.Click += new EventHandler(OnBrowse);

        installButton = new Button();
        installButton.Text = "&Install";
        installButton.SetBounds(12, 118, 120, 30);
        installButton.AccessibleName = "Install";
        installButton.AccessibleDescription = "Patches the game and copies the speech files. Takes a minute or two.";
        installButton.Click += new EventHandler(OnInstall);

        uninstallButton = new Button();
        uninstallButton.Text = "&Uninstall";
        uninstallButton.SetBounds(140, 118, 120, 30);
        uninstallButton.AccessibleName = "Uninstall";
        uninstallButton.AccessibleDescription = "Restores the original game files and removes the speech files.";
        uninstallButton.Click += new EventHandler(OnUninstall);

        closeButton = new Button();
        closeButton.Text = "&Close";
        closeButton.SetBounds(268, 118, 120, 30);
        closeButton.AccessibleName = "Close";
        closeButton.Click += new EventHandler(OnClose);

        statusLabel = new Label();
        statusLabel.Text = "Ready.";
        statusLabel.SetBounds(12, 158, 660, 20);
        statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        statusLabel.AccessibleName = "Status";

        Label logLabel = new Label();
        logLabel.Text = "&Details:";
        logLabel.SetBounds(12, 182, 660, 20);
        logLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        logBox = new TextBox();
        logBox.Multiline = true;
        logBox.ReadOnly = true;
        logBox.ScrollBars = ScrollBars.Vertical;
        logBox.SetBounds(12, 204, 660, 280);
        logBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        logBox.AccessibleName = "Details";
        logBox.AccessibleDescription = "What the installer is doing, and the key list when it finishes.";

        // Tab order: path, browse, install, uninstall, close, details.
        intro.TabIndex = 0;
        pathLabel.TabIndex = 1;
        pathBox.TabIndex = 2;
        browseButton.TabIndex = 3;
        installButton.TabIndex = 4;
        uninstallButton.TabIndex = 5;
        closeButton.TabIndex = 6;
        statusLabel.TabIndex = 7;
        logLabel.TabIndex = 8;
        logBox.TabIndex = 9;

        Controls.Add(intro);
        Controls.Add(pathLabel);
        Controls.Add(pathBox);
        Controls.Add(browseButton);
        Controls.Add(installButton);
        Controls.Add(uninstallButton);
        Controls.Add(closeButton);
        Controls.Add(statusLabel);
        Controls.Add(logLabel);
        Controls.Add(logBox);

        AcceptButton = installButton;
        CancelButton = closeButton;

        string found = startPath;
        if (found == null)
            found = FindGame();
        if (found != null)
        {
            pathBox.Text = found;
            Say("Found the game at " + found + ".");
        }
        else
        {
            Say("I could not find Bad Dream: Coma on this machine. " +
                "Type the folder that holds data.win, or press Browse.");
        }

        Shown += new EventHandler(OnShown);
    }

    private void OnShown(object sender, EventArgs e)
    {
        pathBox.Focus();
        pathBox.SelectAll();
    }

    // -- finding the game ---------------------------------------------------
    private string FindGame()
    {
        List<string> tries = new List<string>();
        // The installer was unzipped straight into the game folder.
        tries.Add(modDir);
        string pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        string pf = Environment.GetEnvironmentVariable("ProgramFiles");
        if (pf86 != null)
        {
            tries.Add(Path.Combine(pf86, "Steam\\steamapps\\common\\Bad Dream Coma"));
            tries.Add(Path.Combine(pf86, "GOG Galaxy\\Games\\Bad Dream Coma"));
        }
        if (pf != null)
            tries.Add(Path.Combine(pf, "Steam\\steamapps\\common\\Bad Dream Coma"));
        tries.Add("C:\\GOG Games\\Bad Dream Coma");
        foreach (DriveInfo d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                string r = d.RootDirectory.FullName;
                tries.Add(Path.Combine(r, "Steam\\steamapps\\common\\Bad Dream Coma"));
                tries.Add(Path.Combine(r, "SteamLibrary\\steamapps\\common\\Bad Dream Coma"));
                tries.Add(Path.Combine(r, "GOG Games\\Bad Dream Coma"));
                tries.Add(Path.Combine(r, "Games\\Bad Dream Coma"));
                tries.Add(Path.Combine(r, "modgames\\bdc\\Bad Dream Coma"));
            }
            catch { }
        }
        foreach (string t in tries)
        {
            try
            {
                if (t != null && File.Exists(Path.Combine(t, "data.win")))
                    return t.TrimEnd('\\');
            }
            catch { }
        }
        return null;
    }

    // -- the browse button --------------------------------------------------
    private void OnBrowse(object sender, EventArgs e)
    {
        OpenFileDialog dlg = new OpenFileDialog();
        dlg.Title = "Find the game - choose its data.win file";
        dlg.Filter = "The game's data file (data.win)|data.win|Every file|*.*";
        dlg.CheckFileExists = true;
        dlg.RestoreDirectory = true;
        try
        {
            if (pathBox.Text.Length > 0 && Directory.Exists(pathBox.Text))
                dlg.InitialDirectory = pathBox.Text;
        }
        catch { }
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            pathBox.Text = Path.GetDirectoryName(dlg.FileName);
            Say("Game folder set to " + pathBox.Text + ".");
            pathBox.Focus();
        }
    }

    private void OnClose(object sender, EventArgs e)
    {
        if (busy)
        {
            MessageBox.Show(this,
                "The installer is still working. Wait until it says it has finished.",
                "Still working", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Close();
    }

    // -- running the batch files -------------------------------------------
    private void OnInstall(object sender, EventArgs e) { Run("Install.bat", "Installing"); }
    private void OnUninstall(object sender, EventArgs e) { Run("Uninstall.bat", "Uninstalling"); }

    private void Run(string batName, string verb)
    {
        if (busy) return;

        string game = pathBox.Text.Trim().Trim('"').TrimEnd('\\');
        if (game.Length == 0)
        {
            Complain("Tell me where the game is first - the folder that holds data.win.");
            pathBox.Focus();
            return;
        }
        if (!File.Exists(Path.Combine(game, "data.win")))
        {
            Complain("There is no data.win in\r\n\r\n" + game + "\r\n\r\n" +
                     "That is not the game folder. Press Browse and pick the game's data.win.");
            pathBox.Focus();
            return;
        }
        string bat = Path.Combine(modDir, batName);
        if (!File.Exists(bat))
        {
            Complain(batName + " is missing. It should sit next to this installer.\r\n\r\n" +
                     "Unzip the whole package and run it from there.");
            return;
        }
        // A game under Program Files cannot be written to without elevation, and
        // the failure would otherwise arrive as an unexplained "could not replace
        // data.win" halfway through. Find out now, and offer the UAC prompt.
        if (!CanWriteTo(game))
        {
            DialogResult r = MessageBox.Show(this,
                "This folder can only be changed by an administrator - which is normal " +
                "for a game under Program Files.\r\n\r\n" +
                "Press OK to start the installer again as administrator. Windows will ask " +
                "you to allow it, and the folder you chose is carried over.",
                "Administrator permission needed", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
            if (r != DialogResult.OK)
                return;
            try
            {
                ProcessStartInfo up = new ProcessStartInfo();
                up.FileName = Application.ExecutablePath;
                up.Arguments = "\"" + game + "\"";
                up.UseShellExecute = true;
                up.Verb = "runas";
                Process.Start(up);
                Close();
            }
            catch (Exception ex)
            {
                Complain("Could not restart as administrator.\r\n\r\n" + ex.Message +
                         "\r\n\r\nRight-click the installer and choose Run as administrator.");
            }
            return;
        }
        if (IsGameRunning())
        {
            DialogResult r = MessageBox.Show(this,
                "Bad Dream: Coma is running right now. The files it has open cannot be " +
                "replaced.\r\n\r\nClose the game, then press OK to carry on, or Cancel to stop.",
                "The game is running", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (r != DialogResult.OK)
                return;
        }

        busy = true;
        SetControlsEnabled(false);
        statusLabel.Text = verb + "... this takes a minute or two. Please wait.";
        Text = verb + " - bdc_access";
        Say("");
        Say(verb + "...");

        RunState st = new RunState();
        st.bat = bat;
        st.game = game;
        st.verb = verb;
        Thread t = new Thread(new ParameterizedThreadStart(Worker));
        t.IsBackground = true;
        t.Start(st);
    }

    private class RunState
    {
        public string bat;
        public string game;
        public string verb;
    }

    private void Worker(object o)
    {
        RunState st = (RunState)o;
        int code = -1;
        string error = null;
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = Environment.GetEnvironmentVariable("ComSpec");
            if (psi.FileName == null || psi.FileName.Length == 0) psi.FileName = "cmd.exe";
            psi.Arguments = "/c \"\"" + st.bat + "\" \"" + st.game + "\"\"";
            psi.WorkingDirectory = modDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;

            Process p = Process.Start(psi);
            // Close its standard input at once. The patcher underneath waits for
            // ever if its stdin is a pipe somebody might still write to.
            p.StandardInput.Close();
            p.OutputDataReceived += new DataReceivedEventHandler(OnOutput);
            p.ErrorDataReceived += new DataReceivedEventHandler(OnOutput);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            code = p.ExitCode;
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        try { BeginInvoke(new FinishedHandler(Finished), new object[] { st.verb, code, error }); }
        catch { }
    }

    private delegate void FinishedHandler(string verb, int code, string error);
    private delegate void SayHandler(string line);

    private void OnOutput(object sender, DataReceivedEventArgs e)
    {
        if (e.Data == null) return;
        try { BeginInvoke(new SayHandler(Say), new object[] { e.Data }); }
        catch { }
    }

    private void Finished(string verb, int code, string error)
    {
        busy = false;
        SetControlsEnabled(true);
        Text = "bdc_access - accessibility for Bad Dream: Coma";

        bool ok = (error == null && code == 0);
        string msg;
        if (error != null)
        {
            msg = "Could not run the installer script.\r\n\r\n" + error;
        }
        else if (!ok)
        {
            msg = verb + " failed. The Details box says why - press Tab to reach it and " +
                  "read from the end.";
        }
        else if (verb == "Installing")
        {
            msg = "Done. The game is patched and ready.\r\n\r\n" +
                  "Start it normally, with your screen reader running.\r\n\r\n" +
                  "Arrow keys move between things, Enter acts on one, A and D filter the " +
                  "room, I is the inventory, H is your health, S is the status screen, and " +
                  "F4 says where you are. The full key list is in the Details box and in " +
                  "the README.";
        }
        else
        {
            msg = "Done. The game is back exactly as it was.";
        }

        statusLabel.Text = ok ? verb + " finished." : verb + " failed.";
        Say(statusLabel.Text);

        MessageBox.Show(this, msg,
            ok ? verb + " finished" : verb + " failed",
            MessageBoxButtons.OK,
            ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);

        if (ok) closeButton.Focus(); else logBox.Focus();
    }

    // -- odds and ends ------------------------------------------------------
    private bool CanWriteTo(string folder)
    {
        string probe = Path.Combine(folder, "bdc_access_write_test.tmp");
        try
        {
            using (FileStream fs = new FileStream(probe, FileMode.Create, FileAccess.Write))
                fs.WriteByte(0);
            File.Delete(probe);
            return true;
        }
        catch
        {
            try { if (File.Exists(probe)) File.Delete(probe); }
            catch { }
            return false;
        }
    }

    private bool IsGameRunning()
    {
        try
        {
            Process[] ps = Process.GetProcessesByName("Bad Dream Coma");
            return ps.Length > 0;
        }
        catch { return false; }
    }

    private void SetControlsEnabled(bool on)
    {
        pathBox.Enabled = on;
        browseButton.Enabled = on;
        installButton.Enabled = on;
        uninstallButton.Enabled = on;
    }

    private void Complain(string what)
    {
        MessageBox.Show(this, what, "bdc_access", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void Say(string line)
    {
        logBox.AppendText(line + "\r\n");
    }

    // A folder passed on the command line wins over the search - that is how the
    // elevated copy of this installer is told what the first one had chosen.
    private static string startPath;

    [STAThread]
    public static void Main(string[] args)
    {
        if (args != null && args.Length > 0)
        {
            string a = args[0].Trim().Trim('"').TrimEnd('\\');
            try
            {
                if (a.Length > 0 && File.Exists(Path.Combine(a, "data.win")))
                    startPath = a;
            }
            catch { }
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new InstallerForm());
    }
}
