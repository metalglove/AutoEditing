namespace Core.Scripts;

/// <summary>
/// Narrow public entry point used by the repository's net48 Core test
/// executable. Production callers should use the workbench services directly.
/// </summary>
public static class WorkbenchProjectionTestRunner
{
	public static void RunAll()
	{
		WorkbenchSessionProjectionSelfTests.Run();
		SynchronizationVelocityRenderingSelfTests.Run();
	}
}
