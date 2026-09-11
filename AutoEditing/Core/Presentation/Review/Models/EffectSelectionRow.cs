using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Core.Scripts;

public sealed class EffectSelectionRow : INotifyPropertyChanged
{
	private bool _isEnabled;

	public string Name { get; set; }
	public string Description { get; set; }
	public string Incorporation { get; set; }
	public string Availability { get; set; }
	public bool CanEnable { get; set; }
	public bool IsEnabled
	{
		get { return _isEnabled; }
		set
		{
			if (_isEnabled == value) return;
			_isEnabled = value;
			OnPropertyChanged();
		}
	}

	public event PropertyChangedEventHandler PropertyChanged;

	private void OnPropertyChanged([CallerMemberName] string propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
