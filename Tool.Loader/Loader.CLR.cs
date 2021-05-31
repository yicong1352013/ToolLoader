using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Tool.Interface;

namespace Tool.Loader {
	internal static unsafe class Loader {
		private const uint PAGE_EXECUTE_READWRITE = 0x40;

		[DllImport("shell32.dll", BestFitMapping = false, CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern char** CommandLineToArgv(string lpCmdLine, int* pNumArgs);

		[DllImport("kernel32.dll", BestFitMapping = false, CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern void* LocalFree(void* hMem);

		[DllImport("kernel32.dll", BestFitMapping = false, CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern bool VirtualProtect(void* lpAddress, uint dwSize, uint flNewProtect, out uint lpflOldProtect);

		public static void Execute(string[] args) {
			try {
				Console.Title = GetTitle(Assembly.GetExecutingAssembly());
			}
			catch {
			}
			if (args is null || args.Length == 0) {
				// 直接运行加载器或调试时使用
				var commandLine = new StringBuilder();
				Console.WriteLine("Specify tool path:");
				commandLine.Append(Console.ReadLine());
				Console.WriteLine("Specify arguments:");
				commandLine.Append(Console.ReadLine());
				Console.WriteLine();
				Console.WriteLine();
				string[]? parsedArgs = CommandLineToArgs(commandLine.ToString());
				if (parsedArgs is null)
					throw new InvalidOperationException("Can't parse command line arguments.");
				Execute(parsedArgs);
				return;
			}
			string toolPath = args[0];
			var toolAssembly = GetOrLoadAssembly(toolPath);
			object tool = CreateToolInstance(toolAssembly, out var optionsType);
			try {
				string? title = (string?)tool.GetType().GetProperty("Title").GetValue(tool, null);
				if (string.IsNullOrEmpty(title))
					title = GetTitle(toolAssembly);
				Console.Title = title;
			}
			catch {
			}
			string[] toolArguments = new string[args.Length - 1];
			for (int i = 0; i < toolArguments.Length; i++)
				toolArguments[i] = args[i + 1];
			object?[] invokeParameters = new object?[] { toolArguments, null, null };
			if ((bool)typeof(CommandLine).GetMethod("TryParse").MakeGenericMethod(optionsType).Invoke(null, invokeParameters) && !(bool)invokeParameters[2]!) {
				var executeStub = typeof(Loader).GetMethod("ExecuteStub", BindingFlags.NonPublic | BindingFlags.Static);
				var realExecute = tool.GetType().GetMethod("Execute", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { optionsType }, null);
				RuntimeHelpers.PrepareMethod(executeStub.MethodHandle);
				RuntimeHelpers.PrepareMethod(realExecute.MethodHandle);
				byte* address = (byte*)executeStub.MethodHandle.GetFunctionPointer();
				byte* target = (byte*)realExecute.MethodHandle.GetFunctionPointer();
				if (address != target)
					WriteJmp(address, target);
				ExecuteStub(tool, invokeParameters[1]!);
			}
			else {
				typeof(CommandLine).GetMethod("ShowUsage").MakeGenericMethod(optionsType).Invoke(null, null);
			}
			if (IsN00bUser() || Debugger.IsAttached) {
				Console.WriteLine("Press any key to exit...");
				try {
					Console.ReadKey(true);
				}
				catch {
				}
			}
		}

		private static string GetTitle(Assembly assembly) {
			string productName = GetAssemblyAttribute<AssemblyProductAttribute>(assembly).Product;
			string version = assembly.GetName().Version.ToString();
			string copyright = GetAssemblyAttribute<AssemblyCopyrightAttribute>(assembly).Copyright.Substring(12);
			int firstBlankIndex = copyright.IndexOf(' ');
			string copyrightOwnerName = copyright.Substring(firstBlankIndex + 1);
			string copyrightYear = copyright.Substring(0, firstBlankIndex);
			return $"{productName} v{version} by {copyrightOwnerName} {copyrightYear}";
		}

		private static T GetAssemblyAttribute<T>(Assembly assembly) {
			return (T)assembly.GetCustomAttributes(typeof(T), false)[0];
		}

		private static string[]? CommandLineToArgs(string commandLine) {
			int length;
			char** pArgs = CommandLineToArgv(commandLine, &length);
			if (pArgs == null)
				return null;
			string[] args = new string[length];
			for (int i = 0; i < length; i++)
				args[i] = new string(pArgs[i]);
			LocalFree(pArgs);
			return args;
		}

		private static Assembly GetOrLoadAssembly(string assemblyPath) {
			string assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
			var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(t => string.Equals(t.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
			return assembly ?? Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
		}

		private static object CreateToolInstance(Assembly assembly, out Type optionsType) {
			var toolTypeGenericDefinition = typeof(ITool<>);
			foreach (var type in assembly.ManifestModule.GetTypes()) {
				foreach (var interfaceType in type.GetInterfaces()) {
					if (!interfaceType.IsGenericType || interfaceType.GetGenericTypeDefinition() != toolTypeGenericDefinition)
						continue;
					var genericArguments = interfaceType.GetGenericArguments();
					if (genericArguments.Length != 1)
						continue;
					optionsType = genericArguments[0];
					return Activator.CreateInstance(type);
				}
			}
			throw new InvalidOperationException("Tool type not found");
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
#pragma warning disable IDE0060 // Remove unused parameter
		private static void ExecuteStub(object @this, object options) {
#pragma warning restore IDE0060 // Remove unused parameter
			throw new Exception("ExecuteStub");
		}

		private static void WriteJmp(void* address, void* target) {
			byte[] jmpStub;
			if (sizeof(void*) == 4) {
				jmpStub = new byte[] {
					0xE9, 0x00, 0x00, 0x00, 0x00 // jmp rel
				};
				fixed (byte* p = jmpStub)
					*(int*)(p + 1) = (int)target - (int)address - 5;
			}
			else {
				jmpStub = new byte[] {
					0x48, 0xB8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // mov rax, target
					0xFF, 0xE0                                                  // jmp rax
				};
				fixed (byte* p = jmpStub)
					*(ulong*)(p + 2) = (ulong)target;
			}
			Write(address, jmpStub);
			address = (byte*)address + jmpStub.Length;
		}

		private static void Write(void* address, byte[] value) {
			VirtualProtect(address, (uint)value.Length, PAGE_EXECUTE_READWRITE, out uint oldProtection);
			Marshal.Copy(value, 0, (IntPtr)address, value.Length);
			VirtualProtect(address, (uint)value.Length, oldProtection, out _);
		}

		private static bool IsN00bUser() {
			if (HasEnv("VisualStudioDir"))
				return false;
			if (HasEnv("SHELL"))
				return false;
			return HasEnv("windir") && !HasEnv("PROMPT");
		}

		private static bool HasEnv(string name) {
			foreach (object key in Environment.GetEnvironmentVariables().Keys) {
				if (key is not string env)
					continue;
				if (string.Equals(env, name, StringComparison.OrdinalIgnoreCase))
					return true;
			}
			return false;
		}
	}
}
