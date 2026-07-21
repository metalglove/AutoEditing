using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Core.Scripts;

public sealed class ClipDrawerRow : INotifyPropertyChanged
{
	private bool _isSelected;
	private ImageSource _thumbnail;
	public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); } }
	public ImageSource Thumbnail { get => _thumbnail; set { if (_thumbnail == value) return; _thumbnail = value; OnPropertyChanged(); } }
	public bool FileExists { get; set; }
	public string FilePath { get; set; }
	public string Player { get; set; }
	public string Game { get; set; }
	public string Map { get; set; }
	public string Guns { get; set; }
	public bool IsSwap { get; set; }
	public int SyncPointCount { get; set; }
	public string LeadTimes { get; set; }
	public bool IsReady { get; set; }
	public string FileName => System.IO.Path.GetFileName(FilePath);
	public string DirectoryName => System.IO.Path.GetDirectoryName(FilePath);
	public string StatusText => !FileExists ? "Missing" : IsReady ? "Ready" : "Needs analysis";

	public event PropertyChangedEventHandler PropertyChanged;
	private void OnPropertyChanged([CallerMemberName] string name = null) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
}
