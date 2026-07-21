using System;
using System.Windows.Forms;
using Core.Domain;
using ScriptPortal.Vegas;

namespace Core.Scripts;

public class EntryPoint
{
	public void FromVegas(Vegas vegas)
	{
		ConfigurationManager.ReloadConfiguration();
		if (VegasScriptBridge.TryExecutePending(vegas))
		{
			return;
		}
		try
		{
			vegas.InvokeCommand("View", "AutoEditingShotReview");
		}
		catch (Exception)
		{
			MessageBox.Show("The AutoEditing application extension is not loaded. Restart VEGAS after deployment, then use View > Extensions > AutoEditing Shot Review.", "AutoEditing", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
	}
}
