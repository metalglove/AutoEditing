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
        public void FromVegas(Vegas vegas)
        {
            vegas.UnloadScriptDomainOnScriptExit = true;
            vegas.Project.Tracks.Clear();
            vegas.UpdateUI();
            Form form = new Form
            {
                Text = "Sniper Montage Creator",
                Size = new Size(500, 400),
                StartPosition = FormStartPosition.CenterScreen
            };

            // Clips Folder Section
            Label clipsLabel = new Label
            {
                Text = "Clips Folder:",
                Location = new Point(10, 10),
                Size = new Size(100, 23)
            };
            form.Controls.Add(clipsLabel);

            TextBox clipsFolderBox = new TextBox
            {
                Text = ConfigurationManager.GetQuickTestingClipsFolder(),
                Location = new Point(10, 35),
                Width = 350
            };
            form.Controls.Add(clipsFolderBox);

            Button browseFolderButton = new Button
            {
                Text = "Browse...",
                Location = new Point(370, 35),
                Size = new Size(80, 23)
            };
            browseFolderButton.Click += (sender, e) =>
            {
                using (var folderDialog = new FolderBrowserDialog())
                {
                    if (folderDialog.ShowDialog() == DialogResult.OK)
                    {
                        clipsFolderBox.Text = folderDialog.SelectedPath;
                    }
                }
            };
            form.Controls.Add(browseFolderButton);

            // Song Path Section
            Label songLabel = new Label
            {
                Text = "Song Path:",
                Location = new Point(10, 70),
                Size = new Size(100, 23)
            };
            form.Controls.Add(songLabel);

            TextBox songPathBox = new TextBox
            {
                Text = ConfigurationManager.GetQuickTestingSongPath(),
                Location = new Point(10, 95),
                Width = 350
            };
            form.Controls.Add(songPathBox);

            Button browseSongButton = new Button
            {
                Text = "Browse...",
                Location = new Point(370, 95),
                Size = new Size(80, 23)
            };
            browseSongButton.Click += (sender, e) =>
            {
                using (var openDialog = new OpenFileDialog())
                {
                    openDialog.Filter = "Audio Files|*.mp3;*.wav;*.m4a;*.aac|All Files|*.*";
                    if (openDialog.ShowDialog() == DialogResult.OK)
                    {
                        songPathBox.Text = openDialog.FileName;
                    }
                }
            };
            form.Controls.Add(browseSongButton);

            // Options Section
            CheckBox quickModeCheckBox = new CheckBox
            {
                Text = "Quick Mode (Skip Validation)",
                Location = new Point(10, 130),
                Size = new Size(200, 23)
            };
            form.Controls.Add(quickModeCheckBox);

            // Buttons Section
            Button startButton = new Button
            {
                Text = "Create Full Montage",
                Location = new Point(10, 160),
                Size = new Size(150, 30)
            };

            Button quickButton = new Button
            {
                Text = "Quick Montage",
                Location = new Point(170, 160),
                Size = new Size(120, 30)
            };

            Button previewButton = new Button
            {
                Text = "Preview Clips",
                Location = new Point(300, 160),
                Size = new Size(100, 30)
            };

            // Log Section
            RichTextBox logBox = new RichTextBox
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
            form.Controls.Add(logBox);

            Logger.SetLogger(logBox);

            // Event Handlers
            startButton.Click += (sender, e) =>
            {
                CreateMontage(vegas, clipsFolderBox.Text, songPathBox.Text, logBox, false, quickModeCheckBox.Checked);
            };

            quickButton.Click += (sender, e) =>
            {
                CreateMontage(vegas, clipsFolderBox.Text, songPathBox.Text, logBox, true, quickModeCheckBox.Checked);
            };

            previewButton.Click += (sender, e) =>
            {
                PreviewClips(clipsFolderBox.Text, logBox);
            };

            form.Controls.Add(startButton);
            form.Controls.Add(quickButton);
            form.Controls.Add(previewButton);

            form.Shown += (sender, e) =>
            {
                Logger.Log("Sniper Montage Automation Ready");
                Logger.Log("================================");
                
                // Debug configuration values
                Logger.Log("=== Configuration Debug ===");
                Logger.Log($"Clips Folder: {ConfigurationManager.GetQuickTestingClipsFolder()}");
                Logger.Log($"Song Path: {ConfigurationManager.GetQuickTestingSongPath()}");
                Logger.Log($"Output Folder: {ConfigurationManager.GetOutputFolder()}");
                Logger.Log($"Log File Path: {ConfigurationManager.GetLogFilePath()}");
                
                // Show all configuration keys for debugging
                var allConfig = ConfigurationManager.GetAllConfigurationValues();
                Logger.Log($"Total config keys loaded: {allConfig.Count}");
                foreach (var kvp in allConfig)
                {
                    Logger.Log($"  {kvp.Key} = {kvp.Value}");
                }
                Logger.Log("=== End Configuration Debug ===");
            };

            form.ShowDialog();
        }

        private void CreateMontage(Vegas vegas, string clipsFolder, string songPath, RichTextBox logBox, bool quickMode, bool skipValidation)
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
                var orchestrator = new MontageOrchestrator();
                
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

        private void PreviewClips(string clipsFolder, RichTextBox logBox)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(clipsFolder) || !System.IO.Directory.Exists(clipsFolder))
                {
                    Logger.LogError("Error: Please select a valid clips folder.");
                    return;
                }

                var parser = new ClipParser();
                var clips = parser.ParseAllClips(clipsFolder);

                Logger.Log($"\r\nFound {clips.Count} clips:");
                Logger.Log("========================");

                foreach (var clip in clips)
                {
                    string prefix = "";
                    if (clip.IsOpener) prefix = "[OPENER] ";
                    if (clip.IsCloser) prefix = "[CLOSER] ";

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
