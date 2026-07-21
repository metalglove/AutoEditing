using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Core.Scripts;

public enum WizardStep
{
	Sources,
	SongAnalysis,
	SfxIndex,
	Analyze,
	Review,
	Drawer
}

public sealed class WizardStepDefinition : INotifyPropertyChanged
{
	private bool _isCurrent;

	public WizardStep Step { get; set; }
	public string Number { get; set; }
	public string Title { get; set; }
	public string Subtitle { get; set; }
	public bool IsCurrent
	{
		get { return _isCurrent; }
		set
		{
			if (_isCurrent == value) return;
			_isCurrent = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsCurrent"));
		}
	}

	public event PropertyChangedEventHandler PropertyChanged;
}
