namespace Core.Scripts;

public sealed class DisplayChoice<T>
{
	public T Value { get; }
	public string Label { get; }

	public DisplayChoice(T value, string label)
	{
		Value = value;
		Label = label;
	}

	public override string ToString() { return Label; }
}
