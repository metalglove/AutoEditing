using System;
using System.Drawing;
using System.Windows.Forms;
using Core.Domain;
using Core.Domain.Clip;
using Core.Domain.Editing;
using Core.Domain.Logging;
using ScriptPortal.Vegas;

namespace Core.Scripts.Ui
{
    /// <summary>
    /// The Sniper Montage Creator as a VEGAS Pro docked window.
    /// </summary>
    /// <remarks>
    /// Inherits <see cref="DockableControl"/> so VEGAS hosts it in the window docking
    /// area next to the Explorer/Trimmer panes. The layout uses table layouts and
    /// docking (no absolute positions) so the pane resizes and re-docks cleanly.
    /// </remarks>
    public class MontageDockView : DockableControl
    {
        /// <summary>
        /// Internal dock window name used by VEGAS to identify and reactivate the pane.
        /// </summary>
        public const string InternalName = "AutoEditing.MontageCreator";

        private readonly Vegas _vegas;

        private TextBox _clipsFolderBox;
        private TextBox _songPathBox;
        private CheckBox _quickModeCheckBox;
        private RichTextBox _logBox;
        private ToolTip _toolTip;

        public MontageDockView(Vegas vegas)
            : base(InternalName)
        {
            _vegas = vegas;
            DisplayName = "Sniper Montage Creator";

            BuildLayout();
            Logger.SetLogger(_logBox);
            Load += OnViewLoad;
        }

        public override DockWindowStyle DefaultDockWindowStyle
        {
            get { return DockWindowStyle.Attached; }
        }

        public override Size DefaultFloatingSize
        {
            get { return new Size(520, 620); }
        }

        private void BuildLayout()
        {
            BackColor = VegasTheme.WindowBackground;
            ForeColor = VegasTheme.TextColor;
            Font = VegasTheme.BaseFont;
            MinimumSize = new Size(320, 360);

            _toolTip = new ToolTip();

            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                BackColor = VegasTheme.WindowBackground,
                Padding = new Padding(12, 6, 12, 12)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            AddAutoRow(root, VegasTheme.CreateSectionHeader("Media Sources"));
            AddAutoRow(root, VegasTheme.CreateFieldLabel("Clips folder"));
            AddAutoRow(root, BuildPathRow(out _clipsFolderBox, ConfigurationManager.GetQuickTestingClipsFolder(), OnBrowseFolderClick, "Browse for the folder containing your gameplay clips"));
            AddAutoRow(root, VegasTheme.CreateFieldLabel("Music track"));
            AddAutoRow(root, BuildPathRow(out _songPathBox, ConfigurationManager.GetQuickTestingSongPath(), OnBrowseSongClick, "Browse for the song to sync the montage to"));

            AddAutoRow(root, VegasTheme.CreateSectionHeader("Options"));
            _quickModeCheckBox = new CheckBox
            {
                Text = "Quick mode (skip validation)",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            VegasTheme.StyleCheckBox(_quickModeCheckBox);
            _toolTip.SetToolTip(_quickModeCheckBox, "Skips clip filename validation before building the timeline");
            AddAutoRow(root, _quickModeCheckBox);

            AddAutoRow(root, BuildButtonRow());

            AddAutoRow(root, VegasTheme.CreateSectionHeader("Output Log"));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowCount = root.RowStyles.Count;
            root.Controls.Add(BuildLogPanel(), 0, root.RowCount - 1);

            Controls.Add(root);
        }

        private static void AddAutoRow(TableLayoutPanel root, Control control)
        {
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowCount = root.RowStyles.Count;
            if (!(control is Label))
            {
                control.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            }
            root.Controls.Add(control, 0, root.RowCount - 1);
        }

        private Control BuildPathRow(out TextBox pathBox, string initialText, EventHandler onBrowse, string browseHint)
        {
            TableLayoutPanel row = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 4)
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            pathBox = new TextBox
            {
                Text = initialText,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 2, 4, 2)
            };
            VegasTheme.StyleTextBox(pathBox);
            row.Controls.Add(pathBox, 0, 0);

            Button browseButton = new Button
            {
                Text = "...",
                Size = new Size(30, pathBox.PreferredHeight),
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0, 2, 0, 2)
            };
            VegasTheme.StyleButton(browseButton);
            browseButton.Click += onBrowse;
            _toolTip.SetToolTip(browseButton, browseHint);
            row.Controls.Add(browseButton, 1, 0);

            return row;
        }

        private Control BuildButtonRow()
        {
            TableLayoutPanel row = new TableLayoutPanel
            {
                ColumnCount = 3,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 8, 0, 0)
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));

            Button startButton = CreateActionButton("Create Full Montage", OnStartMontageClick, "Analyzes the song and builds the complete beat-synced montage");
            VegasTheme.StylePrimaryButton(startButton);
            row.Controls.Add(startButton, 0, 0);

            Button quickButton = CreateActionButton("Quick Montage", OnQuickMontageClick, "Builds a montage using the faster, simplified pipeline");
            row.Controls.Add(quickButton, 1, 0);

            Button previewButton = CreateActionButton("Preview Clips", OnPreviewClipsClick, "Lists the parsed clips from the clips folder in the log");
            row.Controls.Add(previewButton, 2, 0);

            return row;
        }

        private Button CreateActionButton(string text, EventHandler onClick, string hint)
        {
            Button button = new Button
            {
                Text = text,
                Height = 30,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 0, 4, 0)
            };
            VegasTheme.StyleButton(button);
            button.Click += onClick;
            _toolTip.SetToolTip(button, hint);
            return button;
        }

        private Control BuildLogPanel()
        {
            // 1px border panel around a borderless RichTextBox gives the flat, inset
            // look VEGAS uses for its own list/console areas.
            Panel border = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = VegasTheme.BorderColor,
                Padding = new Padding(1),
                Margin = new Padding(0)
            };

            _logBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = VegasTheme.LogBackground,
                ForeColor = VegasTheme.LogTextColor,
                Font = VegasTheme.MonospaceFont
            };
            border.Controls.Add(_logBox);

            return border;
        }

        #region Event Handlers

        private void OnViewLoad(object sender, EventArgs e)
        {
            Logger.Log("Sniper Montage Automation Ready");
            Logger.Log("================================");

            LogConfigurationDebugInfo();
        }

        private void OnBrowseFolderClick(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "Select the clips folder";
                if (folderDialog.ShowDialog(this) == DialogResult.OK)
                {
                    _clipsFolderBox.Text = folderDialog.SelectedPath;
                }
            }
        }

        private void OnBrowseSongClick(object sender, EventArgs e)
        {
            using (OpenFileDialog openDialog = new OpenFileDialog())
            {
                openDialog.Filter = "Audio Files|*.mp3;*.wav;*.m4a;*.aac|All Files|*.*";
                if (openDialog.ShowDialog(this) == DialogResult.OK)
                {
                    _songPathBox.Text = openDialog.FileName;
                }
            }
        }

        private void OnStartMontageClick(object sender, EventArgs e)
        {
            CreateMontage(_clipsFolderBox.Text, _songPathBox.Text, false, _quickModeCheckBox.Checked);
        }

        private void OnQuickMontageClick(object sender, EventArgs e)
        {
            CreateMontage(_clipsFolderBox.Text, _songPathBox.Text, true, _quickModeCheckBox.Checked);
        }

        private void OnPreviewClipsClick(object sender, EventArgs e)
        {
            PreviewClips(_clipsFolderBox.Text);
        }

        #endregion

        private void LogConfigurationDebugInfo()
        {
            // Debug configuration values
            Logger.Log("=== Configuration Debug ===");
            Logger.Log($"Clips Folder: {ConfigurationManager.GetQuickTestingClipsFolder()}");
            Logger.Log($"Song Path: {ConfigurationManager.GetQuickTestingSongPath()}");
            Logger.Log($"Output Folder: {ConfigurationManager.GetOutputFolder()}");
            Logger.Log($"Log File Path: {ConfigurationManager.GetLogFilePath()}");

            // Show all configuration keys for debugging
            System.Collections.Generic.Dictionary<string, string> allConfig = ConfigurationManager.GetAllConfigurationValues();
            Logger.Log($"Total config keys loaded: {allConfig.Count}");
            foreach (System.Collections.Generic.KeyValuePair<string, string> kvp in allConfig)
            {
                Logger.Log($"  {kvp.Key} = {kvp.Value}");
            }
            Logger.Log("=== End Configuration Debug ===");
        }

        private void CreateMontage(string clipsFolder, string songPath, bool quickMode, bool skipValidation)
        {
            try
            {
                Logger.Log($"Starting {(quickMode ? "quick " : "")}montage creation...");

                // Validate inputs
                if (string.IsNullOrWhiteSpace(clipsFolder))
                {
                    Logger.LogError("Error: Please select a clips folder.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(songPath))
                {
                    Logger.LogError("Error: Please select a song file.");
                    return;
                }

                if (!System.IO.Directory.Exists(clipsFolder))
                {
                    Logger.LogError("Error: Clips folder does not exist.");
                    return;
                }

                if (!System.IO.File.Exists(songPath))
                {
                    Logger.LogError("Error: Song file does not exist.");
                    return;
                }

                Logger.Log("Inputs validated successfully.");

                // Create montage using MontageOrchestrator
                MontageOrchestrator orchestrator = new MontageOrchestrator();

                if (quickMode)
                {
                    orchestrator.CreateQuickMontage(_vegas, clipsFolder, songPath, skipValidation);
                    Logger.Log("Quick montage creation completed!");
                }
                else
                {
                    orchestrator.CreateMontage(_vegas, clipsFolder, songPath);
                    Logger.Log("Full montage creation completed!");
                }

                Logger.Log("Timeline built successfully. Check your VEGAS project.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error: {ex.Message}", ex);
                System.Diagnostics.Debug.WriteLine($"Montage creation error: {ex}");
            }
        }

        private void PreviewClips(string clipsFolder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(clipsFolder) || !System.IO.Directory.Exists(clipsFolder))
                {
                    Logger.LogError("Error: Please select a valid clips folder.");
                    return;
                }

                ClipParser parser = new ClipParser();
                System.Collections.Generic.List<Clip> clips = parser.ParseAllClips(clipsFolder);

                Logger.Log($"\r\nFound {clips.Count} clips:");
                Logger.Log("========================");

                foreach (Clip clip in clips)
                {
                    string prefix = "";
                    if (clip.IsOpener)
                    {
                        prefix = "[OPENER] ";
                    }

                    if (clip.IsCloser)
                    {
                        prefix = "[CLOSER] ";
                    }

                    string notes = string.IsNullOrEmpty(clip.Notes) ? "" : $" ({clip.Notes})";
                    Logger.Log($"{prefix}{clip.PlayerName} - {clip.Game} - {clip.Map} - {clip.Gun} - {clip.ClipType} #{clip.SequenceNumber}{notes}");
                }

                Logger.Log("========================");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error previewing clips: {ex.Message}", ex);
            }
        }
    }
}
