using System;
using System.Drawing;
using System.Windows.Forms;
using Core.Domain;
using Core.Domain.Clip;
using Core.Domain.Editing;
using Core.Domain.Logging;
using ScriptPortal.Vegas;

namespace Core.Scripts
{
    public class EntryPoint
    {
        private Vegas _vegas;
        private Form _mainForm;
        private TextBox _clipsFolderBox;
        private TextBox _songPathBox;
        private CheckBox _quickModeCheckBox;
        private RichTextBox _logBox;

        public void FromVegas(Vegas vegas)
        {
            InitializeVegas(vegas);
            CreateMainForm();
            SetupEventHandlers();
            ShowForm();
        }

        private void InitializeVegas(Vegas vegas)
        {
            _vegas = vegas;

            // Ensure that the configuration is loaded.
            ConfigurationManager.ReloadConfiguration();

            // Ensures that Vegas releases the DLL on exit of the script.
            vegas.UnloadScriptDomainOnScriptExit = true;

            // NOTE: since we are testing mostly, we clear the project timeline upon loading the script everytime.
            vegas.Project.Tracks.Clear();
            vegas.UpdateUI();
        }

        private void CreateMainForm()
        {
            _mainForm = new Form
            {
                Text = "Sniper Montage Creator",
                Size = new Size(500, 400),
                StartPosition = FormStartPosition.CenterScreen
            };

            BuildClipsFolderSection();
            BuildSongPathSection();
            BuildOptionsSection();
            BuildButtonsSection();
            BuildLogSection();

            Logger.SetLogger(_logBox);
        }

        private void BuildClipsFolderSection()
        {
            Label clipsLabel = new Label
            {
                Text = "Clips Folder:",
                Location = new Point(10, 10),
                Size = new Size(100, 23)
            };
            _mainForm.Controls.Add(clipsLabel);

            _clipsFolderBox = new TextBox
            {
                Text = ConfigurationManager.GetQuickTestingClipsFolder(),
                Location = new Point(10, 35),
                Width = 350
            };
            _mainForm.Controls.Add(_clipsFolderBox);

            Button browseFolderButton = new Button
            {
                Text = "Browse...",
                Location = new Point(370, 35),
                Size = new Size(80, 23)
            };
            _mainForm.Controls.Add(browseFolderButton);

            // Store event handler reference for later setup
            browseFolderButton.Tag = "BrowseFolder";
        }

        private void BuildSongPathSection()
        {
            Label songLabel = new Label
            {
                Text = "Song Path:",
                Location = new Point(10, 70),
                Size = new Size(100, 23)
            };
            _mainForm.Controls.Add(songLabel);

            _songPathBox = new TextBox
            {
                Text = ConfigurationManager.GetQuickTestingSongPath(),
                Location = new Point(10, 95),
                Width = 350
            };
            _mainForm.Controls.Add(_songPathBox);

            Button browseSongButton = new Button
            {
                Text = "Browse...",
                Location = new Point(370, 95),
                Size = new Size(80, 23)
            };
            _mainForm.Controls.Add(browseSongButton);

            // Store event handler reference for later setup
            browseSongButton.Tag = "BrowseSong";
        }

        private void BuildOptionsSection()
        {
            _quickModeCheckBox = new CheckBox
            {
                Text = "Quick Mode (Skip Validation)",
                Location = new Point(10, 130),
                Size = new Size(200, 23)
            };
            _mainForm.Controls.Add(_quickModeCheckBox);
        }

        private void BuildButtonsSection()
        {
            Button startButton = new Button
            {
                Text = "Create Full Montage",
                Location = new Point(10, 160),
                Size = new Size(150, 30),
                Tag = "StartMontage"
            };
            _mainForm.Controls.Add(startButton);

            Button quickButton = new Button
            {
                Text = "Quick Montage",
                Location = new Point(170, 160),
                Size = new Size(120, 30),
                Tag = "QuickMontage"
            };
            _mainForm.Controls.Add(quickButton);

            Button previewButton = new Button
            {
                Text = "Preview Clips",
                Location = new Point(300, 160),
                Size = new Size(100, 30),
                Tag = "PreviewClips"
            };
            _mainForm.Controls.Add(previewButton);
        }

        private void BuildLogSection()
        {
            _logBox = new RichTextBox
            {
                Multiline = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Location = new Point(10, 200),
                Size = new Size(470, 150),
                ReadOnly = true,
                BackColor = Color.Black,
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 9)
            };
            _mainForm.Controls.Add(_logBox);
        }

        private void SetupEventHandlers()
        {
            // Wire up button events based on their Tag property
            foreach (Control control in _mainForm.Controls)
            {
                if (control is Button button && button.Tag != null)
                {
                    switch (button.Tag.ToString())
                    {
                        case "BrowseFolder":
                            button.Click += OnBrowseFolderClick;
                            break;
                        case "BrowseSong":
                            button.Click += OnBrowseSongClick;
                            break;
                        case "StartMontage":
                            button.Click += OnStartMontageClick;
                            break;
                        case "QuickMontage":
                            button.Click += OnQuickMontageClick;
                            break;
                        case "PreviewClips":
                            button.Click += OnPreviewClipsClick;
                            break;
                    }
                }
            }

            _mainForm.Shown += OnFormShown;
        }

        private void ShowForm()
        {
            _mainForm.ShowDialog();
        }

        #region Event Handlers

        private void OnBrowseFolderClick(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
            {
                if (folderDialog.ShowDialog() == DialogResult.OK)
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
                if (openDialog.ShowDialog() == DialogResult.OK)
                {
                    _songPathBox.Text = openDialog.FileName;
                }
            }
        }

        private void OnStartMontageClick(object sender, EventArgs e)
        {
            CreateMontage(_vegas, _clipsFolderBox.Text, _songPathBox.Text, false, _quickModeCheckBox.Checked);
        }

        private void OnQuickMontageClick(object sender, EventArgs e)
        {
            CreateMontage(_vegas, _clipsFolderBox.Text, _songPathBox.Text, true, _quickModeCheckBox.Checked);
        }

        private void OnPreviewClipsClick(object sender, EventArgs e)
        {
            PreviewClips(_clipsFolderBox.Text);
        }

        private void OnFormShown(object sender, EventArgs e)
        {
            Logger.Log("Sniper Montage Automation Ready");
            Logger.Log("================================");

            LogConfigurationDebugInfo();
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

        private void CreateMontage(Vegas vegas, string clipsFolder, string songPath, bool quickMode, bool skipValidation)
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
                    orchestrator.CreateQuickMontage(vegas, clipsFolder, songPath, skipValidation);
                    Logger.Log("Quick montage creation completed!");
                }
                else
                {
                    orchestrator.CreateMontage(vegas, clipsFolder, songPath);
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

                    Logger.Log($"{prefix}{clip.PlayerName} - {clip.Game} - {clip.Map} - {clip.Gun} - {clip.ClipType} #{clip.SequenceNumber}");
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
