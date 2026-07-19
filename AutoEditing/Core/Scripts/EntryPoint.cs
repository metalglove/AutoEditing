using System;
using System.Windows.Forms;
using Core.Domain;
using Core.Scripts.Ui;
using ScriptPortal.Vegas;

namespace Core.Scripts
{
    public class EntryPoint
    {
        public void FromVegas(Vegas vegas)
        {
            InitializeVegas(vegas);
            ShowDockView(vegas);
        }

        private void InitializeVegas(Vegas vegas)
        {
            // Ensure that the configuration is loaded.
            ConfigurationManager.ReloadConfiguration();

            // The docked window must outlive this script invocation, so the script
            // domain has to stay loaded after FromVegas returns.
            vegas.UnloadScriptDomainOnScriptExit = false;

            // NOTE: since we are testing mostly, we clear the project timeline upon loading the script everytime.
            vegas.Project.Tracks.Clear();
            vegas.UpdateUI();
        }

        private void ShowDockView(Vegas vegas)
        {
            // Re-activate the pane if it is already loaded; otherwise dock a new one.
            if (vegas.ActivateDockView(MontageDockView.InternalName))
            {
                return;
            }

            MontageDockView dockView = new MontageDockView(vegas);
            try
            {
                vegas.LoadDockView(dockView);
            }
            catch (Exception)
            {
                // Docking is unavailable (e.g. older host build) — fall back to a
                // floating window hosting the same themed control.
                ShowFloatingFallback(dockView);
            }
        }

        private void ShowFloatingFallback(MontageDockView dockView)
        {
            Form fallbackForm = new Form
            {
                Text = dockView.DisplayName,
                Size = dockView.DefaultFloatingSize,
                StartPosition = FormStartPosition.CenterScreen,
                BackColor = VegasTheme.WindowBackground
            };
            dockView.Dock = DockStyle.Fill;
            fallbackForm.Controls.Add(dockView);
            fallbackForm.ShowDialog();
        }
    }
}
