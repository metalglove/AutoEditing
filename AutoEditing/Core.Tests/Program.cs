using System;
using Core.Scripts;

namespace AutoEditing.Core.Tests;

internal static class Program
{
	private static int Main()
	{
		try
		{
			WorkbenchProjectionTestRunner.RunAll();
			Console.WriteLine(
				"Core workbench projection and VEGAS rendering-policy tests passed.");
			return 0;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return 1;
		}
	}
}
