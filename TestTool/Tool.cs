using System;
using System.Threading;
using Tool;
using Tool.Interface;

namespace TestTool {
	internal sealed class Tool : ITool<ToolOptions> {
		public string Title => "Test";

		public void Execute(ToolOptions settings) {
			Logger.Level = LogLevel.Verbose1;
			Logger.Info($"LogLevel: {Logger.Level}");
			Logger.Info("Info");
			Logger.Warning("Warning");
			Logger.Error("Error");
			Logger.Verbose1("Verbose1");
			Logger.Verbose2("Verbose2");
			Logger.Verbose2("Verbose3");

			string separator = new string('*', settings.RequiredOption.Length);
			Logger.Info(separator);
			Logger.Info($"DefaultOption: {settings.DefaultOption}");
			Logger.Info($"RequiredOption: {settings.RequiredOption}");
			Logger.Info($"OptionalOption: {settings.OptionalOption}");
			Logger.Info($"OptionalOption2: {string.Join(", ", settings.OptionalOption2)}");
			Logger.Info(separator);

			var lockedLogger = Logger.EnterLock();
			lockedLogger.Info($"Lock mode | IsLocked: {lockedLogger.IsLocked}");
			new Thread(() => Logger.Warning($"I'm blocked | IsLocked: {lockedLogger.IsLocked}")).Start();
			Thread.Sleep(2000);
			lockedLogger.ExitLock();
			Logger.Info($"No lock mode | IsLocked: {lockedLogger.IsLocked}");

			Logger.Info("Exception test");
			try {
				throw new ApplicationException("test");
			}
			catch (Exception ex) {
				Logger.Exception(ex);
			}

			Logger.Flush();
		}
	}
}
