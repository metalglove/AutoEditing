namespace AutoEditing.LlmEditor.Inference;

internal sealed class InferenceBudgets
{
	public int MinimumContextTokens { get; init; } = 65536;
	public int PlanningMaxOutputTokens { get; init; } = 32768;
	public int ReviewMaxOutputTokens { get; init; } = 8192;

	public static InferenceBudgets FromEnvironment()
	{
		InferenceBudgets budgets = new()
		{
			MinimumContextTokens = ReadPositiveInteger("AUTOEDITING_LLM_MIN_CONTEXT_TOKENS", 65536),
			PlanningMaxOutputTokens = ReadPositiveInteger("AUTOEDITING_LLM_PLAN_MAX_OUTPUT_TOKENS", 32768),
			ReviewMaxOutputTokens = ReadPositiveInteger("AUTOEDITING_LLM_REVIEW_MAX_OUTPUT_TOKENS", 8192)
		};
		if (budgets.PlanningMaxOutputTokens >= budgets.MinimumContextTokens)
			throw new InvalidOperationException(
				"AUTOEDITING_LLM_PLAN_MAX_OUTPUT_TOKENS must be smaller than AUTOEDITING_LLM_MIN_CONTEXT_TOKENS.");
		if (budgets.ReviewMaxOutputTokens >= budgets.MinimumContextTokens)
			throw new InvalidOperationException(
				"AUTOEDITING_LLM_REVIEW_MAX_OUTPUT_TOKENS must be smaller than AUTOEDITING_LLM_MIN_CONTEXT_TOKENS.");
		return budgets;
	}

	public void ValidateServerContext(int contextSize)
	{
		if (contextSize < MinimumContextTokens)
			throw new InvalidOperationException(
				$"llama.cpp exposes a {contextSize:N0}-token context, but AutoEditing requires " +
				$"at least {MinimumContextTokens:N0}. Restart llama.cpp with a larger context " +
				$"(for example --ctx-size {MinimumContextTokens}).");
	}

	private static int ReadPositiveInteger(string name, int fallback)
	{
		string? text = Environment.GetEnvironmentVariable(name);
		if (string.IsNullOrWhiteSpace(text)) return fallback;
		if (!int.TryParse(text, out int value) || value < 1)
			throw new InvalidOperationException(name + " must be a positive integer.");
		return value;
	}
}
