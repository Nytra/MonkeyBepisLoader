using HarmonyLib;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

internal class MonkeyLoaderAssemblyLoadContext(
	string monkeyLoaderPath,
	MonkeyLoaderAssemblyLoadContext.AssemblyResolveEventHandler handler)
	: AssemblyLoadContext("MonkeyLoader")
{
	private readonly AssemblyResolveEventHandler? _assemblyResolveEventHandler = handler;

	protected override Assembly? Load(AssemblyName assemblyName)
	{
		try
		{
			Debug.WriteLine($"MonkeyLoaderAssemblyLoadContext: Resolving {assemblyName.FullName}");

			if (_assemblyResolveEventHandler != null)
			{
				var resolvedAssembly = _assemblyResolveEventHandler(assemblyName);
				if (resolvedAssembly != null)
				{
					Debug.WriteLine($"=> Resolved assembly: {resolvedAssembly.FullName}");
					return resolvedAssembly;
				}
			}

			var name = assemblyName.Name;

			var mlPath = Path.Combine(monkeyLoaderPath, $"{name}.dll");
			if (File.Exists(mlPath))
				return LoadFromAssemblyPath(mlPath);

			return null;
		}
		catch (Exception e)
		{
			File.WriteAllLines("0MonkeyBepisCrash.log", [DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - MonkeyLoaderAssemblyLoadContext crashed", e.ToString()]);
			throw;
		}
	}

	public delegate Assembly? AssemblyResolveEventHandler(AssemblyName assemblyName);
}

internal class MonkeyLoaderLoader
{
	private static readonly FileInfo _monkeyLoaderPath = new(Path.Combine("MonkeyLoader", "MonkeyLoader.dll"));
	private static object? _monkeyLoaderInstance = null;
	private static MethodInfo? _monkeyLoaderResolveAssemblyMethod = null;

	internal static void Load()
	{
		var loadContext = new MonkeyLoaderAssemblyLoadContext(_monkeyLoaderPath.DirectoryName!, (assemblyName) =>
		{
			if (_monkeyLoaderInstance == null || _monkeyLoaderResolveAssemblyMethod == null)
				return null;

			// Attempt to resolve the assembly using MonkeyLoader's method
			var resolvedAssembly = _monkeyLoaderResolveAssemblyMethod.Invoke(_monkeyLoaderInstance, [assemblyName]);
			if (resolvedAssembly is Assembly assembly)
			{
				Debug.WriteLine("=> Resolved assembly: " + assembly.FullName);
				return assembly;
			}

			return null;
		});

		// This might cause problems
		//loadContext.Resolving += (context, assembly)
		//=> throw new Exception("This should never happen, we need to know about all assemblies ahead of time through ML");

		// https://github.com/dotnet/runtime/blob/main/docs/design/features/AssemblyLoadContext.ContextualReflection.md
		using var contextualReflection = loadContext.EnterContextualReflection();

		var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();

		// MonkeyLoader was already preloaded
		var monkeyLoaderAssembly = loadedAssemblies.FirstOrDefault(a => a.GetName().Name == "MonkeyLoader");

		if (loadedAssemblies.FirstOrDefault(a => a.GetName().Name == "System.Management") is null)
		{
			var systemManagementPath = RuntimeInformation.RuntimeIdentifier.StartsWith("win")
					? new FileInfo(Path.Combine("runtimes", "win", "lib", "net9.0", "System.Management.dll"))
					: new FileInfo("System.Management.dll");

			if (systemManagementPath.Exists)
				loadContext.LoadFromAssemblyPath(systemManagementPath.FullName);
		}

		var monkeyLoaderType = monkeyLoaderAssembly!.GetType("MonkeyLoader.MonkeyLoader");
		var loggingLevelType = monkeyLoaderAssembly.GetType("MonkeyLoader.Logging.LoggingLevel");
		var traceLogLevel = Enum.Parse(loggingLevelType!, "Trace");

		_monkeyLoaderInstance = Activator.CreateInstance(monkeyLoaderType!, traceLogLevel, "MonkeyLoader/MonkeyLoader.json");
		_monkeyLoaderResolveAssemblyMethod = monkeyLoaderType!.GetMethod("ResolveAssemblyFromPoolsAndMods", BindingFlags.Public | BindingFlags.Instance);
		var loadMethod = monkeyLoaderType!.GetMethod("FullLoad", BindingFlags.Public | BindingFlags.Instance);

		loadMethod!.Invoke(_monkeyLoaderInstance!, null);

		// TODO: Should not be necessary anymore with the hookfxr changes. Either way, should be done by the load context
		foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
			NativeLibrary.SetDllImportResolver(assembly, ResolveNativeLibrary);
	}

	private static IEnumerable<string> LibraryExtensions
	{
		get
		{
			yield return ".dll";

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				yield break;

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				yield return ".so";

			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				yield return ".dylib";
		}
	}

	private static IEnumerable<string> LibraryPrefixes
	{
		get
		{
			yield return string.Empty;

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				yield break;

			yield return "lib";
		}
	}

	private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
	{
		if (libraryName == "rnnoise")
			return IntPtr.Zero;

		var nativeDir = new DirectoryInfo(Path.Combine("runtimes", RuntimeInformation.RuntimeIdentifier, "native"));
		var nativeDirFullPath = nativeDir.FullName;

		foreach (var libraryPrefix in LibraryPrefixes)
		{
			foreach (var libraryExtension in LibraryExtensions)
			{
				var libraryPath = Path.Combine(nativeDirFullPath, $"{libraryPrefix}{libraryName}{libraryExtension}");

				if (File.Exists(libraryPath))
					return NativeLibrary.Load(libraryPath);
			}
		}

		return IntPtr.Zero;
	}
}

[HarmonyPatch(typeof(BepisLoader.BepisLoader), "Main")]
class BepisLoaderPatch
{
	// Makes BepisLoader not start Resonite after initializing BepInEx

	private static readonly MethodInfo _invokeMethod = AccessTools.Method(typeof(MethodBase), nameof(MethodBase.Invoke), [typeof(object), typeof(object[])]);

	[HarmonyTranspiler]
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
	{
		var instArr = instructions.ToArray();

		for (var i = 0; i < instArr.Length; i++)
		{
			var instruction = instArr[i];
			if (instruction.Calls(_invokeMethod))
			{
				yield return instruction;
				yield return new CodeInstruction(OpCodes.Pop);
				yield return new CodeInstruction(OpCodes.Ret);
				break;
			}
			else
			{
				yield return instruction;
			}
		}
	}
}

[HarmonyPatch(typeof(MonkeyLoader.AssemblyLoadContextLoadStrategy), "LoadFile")]
class MonkeyLoaderPatch
{
	// Makes MonkeyLoader check the AppDomain for already loaded assemblies

	private static bool Prefix(string assemblyPath, ref Assembly __result)
	{
		var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.Location == assemblyPath || a.GetName().Name == Path.GetFileNameWithoutExtension(assemblyPath));
		if (asm != null)
		{
			__result = asm;
			return false;
		}
		return true;
	}
}

internal class Patcher
{
	// Harmony isn't accessible from the Program class so we need a separate class to do patching
	public static void Patch()
	{
		var harm = new Harmony("MonkeyBepisLoader");
		harm.PatchAll();
	}
}

class Program
{
	private static readonly FileInfo _bepisPath = new("BepisLoader.dll");
	private static Assembly? _bepisAsm;

	private static void PreloadAssemblies()
	{
		_bepisAsm = Assembly.LoadFrom(_bepisPath.FullName);
		foreach (var file in Directory.GetFiles("MonkeyLoader").Where(f => f.EndsWith(".dll")))
		{
			Assembly.LoadFrom(file);
		}
	}

	private static async Task Main(string[] args)
	{
		try
		{
			PreloadAssemblies();
		}
		catch (Exception e)
		{
			File.WriteAllLines("0MonkeyBepisCrash.log", [DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - Preload assemblies crashed", e.ToString()]);
			throw;
		}

		try
		{
			Patcher.Patch();
		}
		catch (Exception e)
		{
			File.WriteAllLines("0MonkeyBepisCrash.log", [DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - Patcher crashed", e.ToString()]);
			throw;
		}

		// The mod loader that starts first will be the only one that can have working pre-patchers
		// afaik there are no ML mods that pre-patch
		// whereas there are actually some that do for BepInEx like FourLeafClover and DeleagateRefEditing
		// Therefore BepInEx comes first

		try
		{
			_bepisAsm!.EntryPoint!.Invoke(null, [args]);
		}
		catch (Exception e)
		{
			File.WriteAllLines("0MonkeyBepisCrash.log", [DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - BepisLoader crashed", e.ToString()]);
			throw;
		}

		try
		{
			MonkeyLoaderLoader.Load();
		}
		catch (Exception e)
		{
			File.WriteAllLines("0MonkeyBepisCrash.log", [DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - MonkeyLoaderLoader crashed", e.ToString()]);
			throw;
		}

		var gamePath = Environment.GetEnvironmentVariable("HOOKFXR_ORIGINAL_APP_PATH");
		var gameAsm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => x.GetName().Name == Path.GetFileNameWithoutExtension(gamePath)) ?? Assembly.LoadFrom(gamePath!);
		try
		{
			var result = gameAsm.EntryPoint!.Invoke(null, [args]);
			if (result is Task task) task.Wait();
		}
		catch (Exception e)
		{
			File.WriteAllLines("0MonkeyBepisCrash.log", [DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - Game crashed", e.ToString()]);
			throw;
		}
	}
}