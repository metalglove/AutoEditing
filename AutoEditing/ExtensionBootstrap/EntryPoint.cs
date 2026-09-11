using System;
using System.IO;
using System.Reflection;
using ScriptPortal.Vegas;

public sealed class EntryPoint
{
	public void FromVegas(Vegas vegas)
	{
		string directory = Path.GetDirectoryName(typeof(EntryPoint).Assembly.Location);
		ResolveEventHandler resolver = delegate(object sender, ResolveEventArgs args)
		{
			string path = Path.Combine(directory, new AssemblyName(args.Name).Name + ".dll");
			return File.Exists(path) ? Assembly.LoadFrom(path) : null;
		};
		AppDomain.CurrentDomain.AssemblyResolve += resolver;
		try
		{
			Assembly core = Assembly.LoadFrom(Path.Combine(directory, "Core.dll"));
			Type entryPointType = core.GetType("Core.Scripts.EntryPoint", true);
			object entryPoint = Activator.CreateInstance(entryPointType);
			entryPointType.GetMethod("FromVegas").Invoke(entryPoint, new object[] { vegas });
		}
		finally
		{
			AppDomain.CurrentDomain.AssemblyResolve -= resolver;
		}
	}
}
