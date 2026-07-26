using System;
using System.Collections;
using System.IO;
using System.Reflection;
using ScriptPortal.Vegas;

namespace AutoEditing.ExtensionBootstrap;

public sealed class AutoEditingExtensionModule : ICustomCommandModule
{
	private const string ImplementationAssemblyName = "Core.dll";
	private const string ImplementationTypeName = "Core.Scripts.AutoEditingCommandModule";

	private readonly string extensionDirectory;
	private ICustomCommandModule implementation;

	public AutoEditingExtensionModule()
	{
		extensionDirectory = Path.GetDirectoryName(typeof(AutoEditingExtensionModule).Assembly.Location);
		AppDomain.CurrentDomain.AssemblyResolve += ResolveSiblingAssembly;
		WriteDiagnostic("Bootstrap constructed from " + extensionDirectory);
	}

	public void InitializeModule(Vegas vegas)
	{
		try
		{
			string implementationPath = Path.Combine(extensionDirectory, ImplementationAssemblyName);
			Assembly assembly = Assembly.LoadFrom(implementationPath);
			Type type = assembly.GetType(ImplementationTypeName, true);
			implementation = (ICustomCommandModule)Activator.CreateInstance(type, true);
			implementation.InitializeModule(vegas);
			WriteDiagnostic("Implementation initialized.");
		}
		catch (Exception exception)
		{
			WriteDiagnostic("Bootstrap initialization failed: " + exception);
			throw;
		}
	}

	public ICollection GetCustomCommands()
	{
		if (implementation == null)
			throw new InvalidOperationException("The AutoEditing implementation was not initialized.");
		return implementation.GetCustomCommands();
	}

	private Assembly ResolveSiblingAssembly(object sender, ResolveEventArgs args)
	{
		string fileName = new AssemblyName(args.Name).Name + ".dll";
		string path = Path.Combine(extensionDirectory, fileName);
		return File.Exists(path) ? Assembly.LoadFrom(path) : null;
	}

	private static void WriteDiagnostic(string message)
	{
		try
		{
			string directory = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"AutoEditing");
			Directory.CreateDirectory(directory);
			File.AppendAllText(
				Path.Combine(directory, "extension-loader.log"),
				DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
		}
		catch
		{
		}
	}
}
