using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using ScriptPortal.Vegas;
using Core.Domain.Editing;
using Core.Domain.Clip;

namespace Core.Scripts
{
    public class EntryPoint
    {
        public void FromVegas(Vegas vegas)
        {
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
            TextBox logBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(10, 200),
                Size = new Size(470, 150),
                ReadOnly = true,
                BackColor = Color.Black,
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 9)
            };
            form.Controls.Add(logBox);

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

            // Initialize log
            logBox.AppendText("Sniper Montage Automation Ready\r\n");
            logBox.AppendText("================================\r\n");

            form.ShowDialog();
        }

        private void CreateMontage(Vegas vegas, string clipsFolder, string songPath, TextBox logBox, bool quickMode, bool skipValidation)
        {
            try
            {
                logBox.AppendText($"Starting {(quickMode ? "quick " : "")}montage creation...\r\n");

                // Validate inputs
                if (string.IsNullOrWhiteSpace(clipsFolder))
                {
                    logBox.AppendText("Error: Please select a clips folder.\r\n");
                    return;
                }

                if (string.IsNullOrWhiteSpace(songPath))
                {
                    logBox.AppendText("Error: Please select a song file.\r\n");
                    return;
                }

                if (!System.IO.Directory.Exists(clipsFolder))
                {
                    logBox.AppendText("Error: Clips folder does not exist.\r\n");
                    return;
                }

                if (!System.IO.File.Exists(songPath))
                {
                    logBox.AppendText("Error: Song file does not exist.\r\n");
                    return;
                }

                logBox.AppendText("Inputs validated successfully.\r\n");

                // Create montage using MontageOrchestrator
                var orchestrator = new MontageOrchestrator();
                
                if (quickMode)
                {
                    orchestrator.CreateQuickMontage(vegas, clipsFolder, songPath, skipValidation);
                    logBox.AppendText("Quick montage creation completed!\r\n");
                }
                else
                {
                    orchestrator.CreateMontage(vegas, clipsFolder, songPath);
                    logBox.AppendText("Full montage creation completed!\r\n");
                }

                logBox.AppendText("Timeline built successfully. Check your VEGAS project.\r\n");
            }
            catch (Exception ex)
            {
                logBox.AppendText($"Error: {ex.Message}\r\n");
                System.Diagnostics.Debug.WriteLine($"Montage creation error: {ex}");
            }
        }

        private void PreviewClips(string clipsFolder, TextBox logBox)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(clipsFolder) || !System.IO.Directory.Exists(clipsFolder))
                {
                    logBox.AppendText("Error: Please select a valid clips folder.\r\n");
                    return;
                }

                var parser = new ClipParser();
                var clips = parser.ParseAllClips(clipsFolder);

                logBox.AppendText($"\r\nFound {clips.Count} clips:\r\n");
                logBox.AppendText("========================\r\n");

                foreach (var clip in clips)
                {
                    string prefix = "";
                    if (clip.IsOpener) prefix = "[OPENER] ";
                    if (clip.IsCloser) prefix = "[CLOSER] ";

                    logBox.AppendText($"{prefix}{clip.PlayerName} - {clip.Game} - {clip.Map} - {clip.Gun} - {clip.ClipType} #{clip.SequenceNumber}\r\n");
                }

                logBox.AppendText("========================\r\n");
            }
            catch (Exception ex)
            {
                logBox.AppendText($"Error previewing clips: {ex.Message}\r\n");
            }
        }
    }
}
