
namespace MagicRemoteService {
	public enum LogLevel {
		Error = 0,
		Warning = 1,
		Information = 2,
		Debug = 3
	}
	public static class Logger {
		private const long lMaxFileSize = 1024 * 1024;
		private const int iMaxFileCount = 5;
		private const int iMaxRecent = 500;
		private const int iLevelRefreshInterval = 5000;
		private const int iFileRetryInterval = 60000;
		private const LogLevel llDefault = LogLevel.Information;

		private static readonly object oLock = new object();
		private static readonly System.Diagnostics.EventLog elEventLog;
		private static readonly System.Collections.Generic.Queue<string> qRecent = new System.Collections.Generic.Queue<string>();
		private static System.IO.StreamWriter swFile;
		private static int iFileRetryTick;
		private static bool bFileFailed;
		private static volatile int iLevel = (int)Logger.llDefault;
		private static int iLevelReadTick;
		private static bool bLevelRead;

		public static readonly string strDirectory;
		public static readonly string strFilePath;

		static Logger() {
			// Elevated processes (the service and its client process) share settings in HKLM, so they log to ProgramData; a non-elevated
			// instance keeps settings in HKCU and cannot write files created by the service, so it logs to the user profile
			Logger.strDirectory = System.IO.Path.Combine(System.Environment.GetFolderPath(MagicRemoteService.Program.bElevated ? System.Environment.SpecialFolder.CommonApplicationData : System.Environment.SpecialFolder.LocalApplicationData), "MagicRemoteService", "Logs");
			// Only one service process and one interactive process can run at once, so each file has a single writer
			Logger.strFilePath = System.IO.Path.Combine(Logger.strDirectory, "MagicRemoteService-" + (System.Environment.UserInteractive ? "app" : "service") + ".log");
			try {
				if(!System.Diagnostics.EventLog.SourceExists("MagicRemoteService")) {
					System.Diagnostics.EventLog.CreateEventSource("MagicRemoteService", "Application");
				}
			} catch(System.Exception) {
				// Creating the source needs administrator rights; writing still works once it exists
			}
			Logger.elEventLog = new System.Diagnostics.EventLog("Application", ".", "MagicRemoteService");
		}

		private static Microsoft.Win32.RegistryKey RootKey {
			get {
				return MagicRemoteService.Program.bElevated ? Microsoft.Win32.Registry.LocalMachine : Microsoft.Win32.Registry.CurrentUser;
			}
		}

		// Read from the registry at most every few seconds so a change made in the settings applies to every running process
		public static LogLevel Level {
			get {
				if(!Logger.bLevelRead || unchecked(System.Environment.TickCount - Logger.iLevelReadTick) > Logger.iLevelRefreshInterval) {
					Logger.iLevelReadTick = System.Environment.TickCount;
					Logger.bLevelRead = true;
					try {
						using(Microsoft.Win32.RegistryKey rkMagicRemoteService = Logger.RootKey.OpenSubKey(@"Software\MagicRemoteService")) {
							int iValue = rkMagicRemoteService == null ? (int)Logger.llDefault : (int)rkMagicRemoteService.GetValue("LogLevel", (int)Logger.llDefault);
							Logger.iLevel = System.Math.Max((int)LogLevel.Error, System.Math.Min((int)LogLevel.Debug, iValue));
						}
					} catch(System.Exception) {
					}
				}
#if DEBUG
				return LogLevel.Debug;
#else
				return (LogLevel)Logger.iLevel;
#endif
			}
			set {
				using(Microsoft.Win32.RegistryKey rkMagicRemoteService = Logger.RootKey.CreateSubKey(@"Software\MagicRemoteService")) {
					rkMagicRemoteService.SetValue("LogLevel", (int)value, Microsoft.Win32.RegistryValueKind.DWord);
				}
				Logger.iLevel = (int)value;
				Logger.iLevelReadTick = System.Environment.TickCount;
				Logger.bLevelRead = true;
			}
		}
		public static bool IsEnabled(LogLevel ll) {
			return ll <= Logger.Level;
		}
		public static string[] GetRecent() {
			lock(Logger.oLock) {
				return Logger.qRecent.ToArray();
			}
		}
		public static void Write(LogLevel ll, string strMessage, bool bEventLog = true) {
			if(!Logger.IsEnabled(ll)) {
				return;
			}
			string strLine = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + Logger.Tag(ll) + " [" + System.Threading.Thread.CurrentThread.ManagedThreadId.ToString("00") + "] " + strMessage;
			lock(Logger.oLock) {
				Logger.qRecent.Enqueue(strLine);
				while(Logger.qRecent.Count > Logger.iMaxRecent) {
					Logger.qRecent.Dequeue();
				}
				Logger.WriteFile(strLine);
			}
			if(bEventLog && ll != LogLevel.Debug) {
				try {
					Logger.elEventLog.WriteEntry(strMessage.Length > 30000 ? strMessage.Substring(0, 30000) : strMessage, ll == LogLevel.Error ? System.Diagnostics.EventLogEntryType.Error : ll == LogLevel.Warning ? System.Diagnostics.EventLogEntryType.Warning : System.Diagnostics.EventLogEntryType.Information);
				} catch(System.Exception) {
				}
			}
		}
		private static string Tag(LogLevel ll) {
			switch(ll) {
				case LogLevel.Error:
					return "ERROR";
				case LogLevel.Warning:
					return "WARN ";
				case LogLevel.Information:
					return "INFO ";
				default:
					return "DEBUG";
			}
		}
		private static void WriteFile(string strLine) {
			if(Logger.bFileFailed && unchecked(System.Environment.TickCount - Logger.iFileRetryTick) < Logger.iFileRetryInterval) {
				return;
			}
			try {
				if(Logger.swFile == null) {
					System.IO.Directory.CreateDirectory(Logger.strDirectory);
					Logger.swFile = new System.IO.StreamWriter(new System.IO.FileStream(Logger.strFilePath, System.IO.FileMode.Append, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete), new System.Text.UTF8Encoding(false)) {
						AutoFlush = true
					};
				}
				Logger.swFile.WriteLine(strLine);
				Logger.bFileFailed = false;
				if(Logger.swFile.BaseStream.Length > Logger.lMaxFileSize) {
					Logger.Rotate();
				}
			} catch(System.Exception) {
				Logger.CloseFile();
				Logger.bFileFailed = true;
				Logger.iFileRetryTick = System.Environment.TickCount;
			}
		}
		private static void Rotate() {
			Logger.CloseFile();
			for(int i = Logger.iMaxFileCount - 1; i > 0; i--) {
				string strFrom = i == 1 ? Logger.strFilePath : Logger.strFilePath + "." + (i - 1);
				string strTo = Logger.strFilePath + "." + i;
				if(System.IO.File.Exists(strFrom)) {
					if(System.IO.File.Exists(strTo)) {
						System.IO.File.Delete(strTo);
					}
					System.IO.File.Move(strFrom, strTo);
				}
			}
		}
		private static void CloseFile() {
			if(Logger.swFile != null) {
				try {
					Logger.swFile.Dispose();
				} catch(System.Exception) {
				}
				Logger.swFile = null;
			}
		}
	}
}
