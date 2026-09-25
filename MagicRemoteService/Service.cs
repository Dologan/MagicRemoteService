
namespace MagicRemoteService {
	public enum ServiceType {
		Server,
		Client,
		Both
	}
	public enum WebSocketOpCode : byte {
		Continuation = 0x0,
		Text = 0x1,
		Binary = 0x2,
		ConnectionClose = 0x8,
		Ping = 0x9,
		Pong = 0xA
	}
	public enum MessageType : byte {
		PositionRelative = 0x00,
		PositionAbsolute = 0x01,
		Wheel = 0x02,
		Visible = 0x03,
		Key = 0x04,
		Unicode = 0x05,
		Shutdown = 0x06
	}
	public sealed class ConnectionInfo {
		public string Client;
		public System.DateTime ConnectedAt;
		public System.DateTime LastMessageAt;
		public long MessageCount;
		public bool LogForwarding;
	}
	public partial class Service : System.ServiceProcess.ServiceBase {
		// Connection state of this process, shown in the Diagnostics tab
		private static readonly System.Collections.Generic.List<MagicRemoteService.ConnectionInfo> liConnection = new System.Collections.Generic.List<MagicRemoteService.ConnectionInfo>();
		private static readonly System.Collections.Generic.Queue<string> qConnectionHistory = new System.Collections.Generic.Queue<string>();
		public static MagicRemoteService.ConnectionInfo[] GetConnections() {
			lock(Service.liConnection) {
				return Service.liConnection.ToArray();
			}
		}
		public static string[] GetConnectionHistory() {
			lock(Service.qConnectionHistory) {
				return Service.qConnectionHistory.ToArray();
			}
		}
		public static string FormatDuration(System.TimeSpan ts) {
			return (int)ts.TotalHours + ":" + ts.ToString(@"mm\:ss");
		}
		private static void AddConnectionHistory(string strEvent) {
			lock(Service.qConnectionHistory) {
				Service.qConnectionHistory.Enqueue(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + strEvent);
				while(Service.qConnectionHistory.Count > 30) {
					Service.qConnectionHistory.Dequeue();
				}
			}
		}
		public ServiceType Type {
			get {
				return this.stType;
			}
		}
		private volatile int iPort;
		private volatile bool bInactivity;
		private volatile int iTimeoutInactivity;
		private volatile bool bVideoInput;
		private volatile int iTimeoutVideoInput;
		private readonly System.Collections.Generic.Dictionary<ushort, Bind[]> dBind = new System.Collections.Generic.Dictionary<ushort, Bind[]>() {
			{ 0x0001, null },
			{ 0x0002, null },
			{ 0x0008, null },
			{ 0x000D, null },
			{ 0x0021, null },
			{ 0x0022, null },
			{ 0x0025, null },
			{ 0x0026, null },
			{ 0x0027, null },
			{ 0x0028, null },
			{ 0x0030, null },
			{ 0x0031, null },
			{ 0x0032, null },
			{ 0x0033, null },
			{ 0x0034, null },
			{ 0x0035, null },
			{ 0x0036, null },
			{ 0x0037, null },
			{ 0x0038, null },
			{ 0x0039, null },
			{ 0x0193, null },
			{ 0x0194, null },
			{ 0x0195, null },
			{ 0x0196, null },
			{ 0x01CD, null },
			{ 0x019F, null },
			{ 0x0013, null },
			{ 0x01A1, null },
			{ 0x019C, null },
			{ 0x019D, null }
		};

		private System.Threading.Thread thrServer;
		private ServiceType stType;

		private static readonly System.Threading.ManualResetEvent mreStop = new System.Threading.ManualResetEvent(true);
		private static readonly System.Threading.AutoResetEvent areSessionChanged = new System.Threading.AutoResetEvent(false);
		private static System.Threading.EventWaitHandle ewhServerStarted;
		private static System.Threading.EventWaitHandle ewhClientStarted;
		private static System.Threading.EventWaitHandle ewhSessionChanged;
		private static System.Threading.EventWaitHandle ewhClientConnecting;
		private static System.Threading.EventWaitHandle ewhServerMessage;
		private static System.Threading.EventWaitHandle ewhServerDisconnecting;

		private static readonly byte[] tabClose = { (0b1000 << 4) | (0x8 << 0), 0x00 };
		private static readonly byte[] tabPing = { (0b1000 << 4) | (0x9 << 0), 0x00 };
		private static readonly byte[] tabPingUserInput = { (0b1000 << 4) | (0x9 << 0), 0x01, 0x01 };
		public Service() {
			this.InitializeComponent();
		}
		private readonly object oLifecycle = new object();
		private bool bStarted;
		private int iLifecycleGeneration;
		private System.DateTime dtLastStart;
		private int iConsecutiveFailure;
		public void ServiceStart() {
			lock(this.oLifecycle) {
				this.iLifecycleGeneration++;
				if(!this.bStarted) {
					this.ServiceStartCore(true);
				}
			}
		}
		public void ServiceStop() {
			lock(this.oLifecycle) {
				this.iLifecycleGeneration++;
				if(this.bStarted) {
					this.ServiceStopCore(true);
				}
			}
		}
		private void ServiceRestart() {
			System.Threading.Tasks.Task.Run(delegate () {
				lock(this.oLifecycle) {
					this.iLifecycleGeneration++;
					if(this.bStarted) {
						this.ServiceStopCore(true);
					}
					this.ServiceStartCore(true);
				}
			});
		}
		private void ServiceRestartAfterFailure() {
			System.Threading.Tasks.Task.Run(delegate () {
				int iGeneration;
				int iDelay;
				lock(this.oLifecycle) {
					if(!this.bStarted) {
						return;
					}
					this.ServiceStopCore(false);
					iGeneration = this.iLifecycleGeneration;
					if((System.DateTime.UtcNow - this.dtLastStart) < System.TimeSpan.FromMinutes(1)) {
						this.iConsecutiveFailure++;
					} else {
						this.iConsecutiveFailure = 1;
					}
					iDelay = (int)System.Math.Min(60000, 2000 * System.Math.Pow(2, System.Math.Min(this.iConsecutiveFailure - 1, 5)));
				}
				Service.Warn("Service restarting after failure in " + (iDelay / 1000) + "s (consecutive failure " + this.iConsecutiveFailure + ")");
				System.Threading.Thread.Sleep(iDelay);
				lock(this.oLifecycle) {
					if(!this.bStarted && iGeneration == this.iLifecycleGeneration) {
						try {
							this.ServiceStartCore(false);
						} catch(System.Exception eException) {
							Service.Error("Service restart failed: " + eException.ToString());
						}
					}
				}
			});
		}
		private void ServiceStartCore(bool bReportStatus) {
			this.bStarted = true;
			this.dtLastStart = System.DateTime.UtcNow;
			Service.ewhServerStarted = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.ManualReset, @"Global\{FFB31601-E362-48A5-B9A2-5DF29A3B06C1}", out _, Program.ewhsAll);
			Service.ewhClientStarted = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.ManualReset, @"Global\{9878BC83-46A0-412B-86B6-10F1C43FC0D9}", out _, Program.ewhsAll);
			Service.ewhSessionChanged = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, @"Global\{996C2D37-8FAC-4C89-8A00-CE30CBE66B87}", out _, Program.ewhsAll);
			Service.ewhClientConnecting = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, @"Global\{06031D31-621A-4288-850E-8FEE0ED3F054}", out _, Program.ewhsAll);
			Service.ewhServerMessage = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, @"Global\{78968501-8AE0-424D-B82E-EA0A0BEA3414}", out _, Program.ewhsAll);
			Service.ewhServerDisconnecting = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, @"Global\{C45127E0-B626-46D0-8610-C5DE20E4F790}", out _, Program.ewhsAll);

			if(!System.Environment.UserInteractive) {
				this.stType = ServiceType.Server;
			} else if(!Service.ewhServerStarted.WaitOne(System.TimeSpan.Zero)) {
				this.stType = ServiceType.Both;
			} else {
				this.stType = ServiceType.Client;
			}

			WinApi.ServiceStatus ssServiceStatus = new WinApi.ServiceStatus();
			switch(this.stType) {
				case ServiceType.Server:
					Service.Log("Service server start");
					break;
				case ServiceType.Both:
					Service.Log("Service both start");
					break;
				case ServiceType.Client:
					Service.Log("Service client start");
					break;
			}
			switch(this.stType) {
				case ServiceType.Server when bReportStatus:
					ssServiceStatus.dwCurrentState = WinApi.ServiceCurrentState.SERVICE_START_PENDING;
					ssServiceStatus.dwWaitHint = 100000;
					WinApi.Advapi32.SetServiceStatus(this.ServiceHandle, ref ssServiceStatus);
					break;
				case ServiceType.Server:
				case ServiceType.Both:
				case ServiceType.Client:
					break;
			}
			Microsoft.Win32.RegistryKey rkMagicRemoteService = (MagicRemoteService.Program.bElevated ? Microsoft.Win32.Registry.LocalMachine : Microsoft.Win32.Registry.CurrentUser).OpenSubKey(@"Software\MagicRemoteService");
			switch(this.stType) {
				case ServiceType.Server:
				case ServiceType.Both:
					if(rkMagicRemoteService == null) {
						this.iPort = 41230;
					} else {
						this.iPort = (int)rkMagicRemoteService.GetValue("Port", 41230);
					}
					break;
				case ServiceType.Client:
					break;
			}
			switch(this.stType) {
				case ServiceType.Server:
					break;
				case ServiceType.Both:
				case ServiceType.Client:
					if(rkMagicRemoteService == null) {
						this.bInactivity = true;
						this.iTimeoutInactivity = 7200000;
						this.bVideoInput = true;
						this.iTimeoutVideoInput = 900000;
					} else {
						this.bInactivity = (int)rkMagicRemoteService.GetValue("Inactivity", 1) != 0;
						this.iTimeoutInactivity = (int)rkMagicRemoteService.GetValue("TimeoutInactivity", 7200000);
						this.bVideoInput = (int)rkMagicRemoteService.GetValue("VideoInput", 1) != 0;
						this.iTimeoutVideoInput = (int)rkMagicRemoteService.GetValue("TimeoutVideoInput", 900000);
					}
					Microsoft.Win32.RegistryKey rkMagicRemoteServiceRemoteBind = (MagicRemoteService.Program.bElevated ? Microsoft.Win32.Registry.LocalMachine : Microsoft.Win32.Registry.CurrentUser).OpenSubKey(@"Software\MagicRemoteService\Remote\Bind");
					if(rkMagicRemoteServiceRemoteBind == null) {
						this.dBind[0x0001] = new Bind[] { new BindMouse(BindMouseValue.Left) };
						this.dBind[0x0002] = new Bind[] { new BindMouse(BindMouseValue.Right) };
						this.dBind[0x0008] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.Back, 0x0E, false) };
						this.dBind[0x000D] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.Enter, 0x1C, false) };
						this.dBind[0x0021] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.ControlKey, 0x1D, false), new BindKeyboard((byte)System.Windows.Forms.Keys.C, 0x2E, false) };
						this.dBind[0x0022] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.ControlKey, 0x1D, false), new BindKeyboard((byte)System.Windows.Forms.Keys.V, 0x2F, false) };
						this.dBind[0x0025] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.Left, 0x4B, true) };
						this.dBind[0x0026] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.Up, 0x48, true) };
						this.dBind[0x0027] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.Right, 0x4D, true) };
						this.dBind[0x0028] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.Down, 0x50, true) };
						this.dBind[0x0030] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.NumPad0, 0x52, false) };
						this.dBind[0x0031] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.NumPad1, 0x4F, false) };
						this.dBind[0x0032] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.NumPad2, 0x50, false) };
						this.dBind[0x0033] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.NumPad3, 0x51, false) };
						this.dBind[0x0034] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.NumPad4, 0x4B, false) };
						this.dBind[0x0035] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.NumPad5, 0x4C, false) };
						this.dBind[0x0036] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.NumPad6, 0x4D, false) };
						this.dBind[0x0037] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.NumPad7, 0x47, false) };
						this.dBind[0x0038] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.NumPad8, 0x48, false) };
						this.dBind[0x0039] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.NumPad9, 0x49, false) };
						this.dBind[0x0193] = new Bind[] { new BindAction(BindActionValue.Shutdown) };
						this.dBind[0x0194] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.LWin, 0x5B, true) };
						this.dBind[0x0195] = new Bind[] { new BindMouse(BindMouseValue.Right) };
						this.dBind[0x0196] = new Bind[] { new BindAction(BindActionValue.Keyboard) };
						this.dBind[0x01CD] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.Escape, 0x01, false) };
						this.dBind[0x019F] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.Play, 0x00, false) };
						this.dBind[0x0013] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.Pause, 0x00, false) };
						this.dBind[0x01A1] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.MediaNextTrack, 0x00, false) };
						this.dBind[0x019C] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.MediaPreviousTrack, 0x00, false) };
						this.dBind[0x019D] = new Bind[] { new BindKeyboard((byte)System.Windows.Forms.Keys.MediaStop, 0x00, false) };
					} else {
						foreach(string sKey in rkMagicRemoteServiceRemoteBind.GetSubKeyNames()) {
							System.Collections.Generic.List<Bind> liBind = new System.Collections.Generic.List<Bind>();
							Microsoft.Win32.RegistryKey rkMagicRemoteServiceRemoteBindKey = rkMagicRemoteServiceRemoteBind.OpenSubKey(sKey);
							foreach(string sBind in rkMagicRemoteServiceRemoteBindKey.GetSubKeyNames()) {
								Microsoft.Win32.RegistryKey rkMagicRemoteServiceRemoteBindBind = rkMagicRemoteServiceRemoteBindKey.OpenSubKey(sBind);
								switch((int)rkMagicRemoteServiceRemoteBindBind.GetValue("Kind")) {
									case 0x00:
										liBind.Add(new BindMouse((BindMouseValue)(int)rkMagicRemoteServiceRemoteBindBind.GetValue("Value", 0x0000)));
										break;
									case 0x01:
										liBind.Add(new BindKeyboard((byte)(int)rkMagicRemoteServiceRemoteBindBind.GetValue("VirtualKey", 0x00), (byte)(int)rkMagicRemoteServiceRemoteBindBind.GetValue("ScanCode", 0x00), (int)rkMagicRemoteServiceRemoteBindBind.GetValue("Extended", 0x00) == 0x01));
										break;
									case 0x02:
										liBind.Add(new BindAction((BindActionValue)(int)rkMagicRemoteServiceRemoteBindBind.GetValue("Value", 0x00)));
										break;
									case 0x03:
										liBind.Add(new BindCommand((string)rkMagicRemoteServiceRemoteBindBind.GetValue("Command")));
										break;
								}
							}
							this.dBind[ushort.Parse(sKey.Substring(2), System.Globalization.NumberStyles.HexNumber)] = liBind.ToArray();
						}
					}
					break;
			}
			switch(this.stType) {
				case ServiceType.Server:
					Service.ewhServerStarted.Set();
					break;
				case ServiceType.Both:
					break;
				case ServiceType.Client:
					Service.ewhClientStarted.Set();
					break;
			}
			Service.mreStop.Reset();
			this.thrServer = new System.Threading.Thread(delegate () {
				this.ThreadServer();
			});
			this.thrServer.Start();
			switch(this.stType) {
				case ServiceType.Server:
					Service.Log("Service server started");
					break;
				case ServiceType.Both:
					Service.Log("Service both started");
					break;
				case ServiceType.Client:
					Service.Log("Service client started");
					break;
			}
			switch(this.stType) {
				case ServiceType.Server when bReportStatus:
					ssServiceStatus.dwCurrentState = WinApi.ServiceCurrentState.SERVICE_RUNNING;
					WinApi.Advapi32.SetServiceStatus(this.ServiceHandle, ref ssServiceStatus);
					break;
				case ServiceType.Server:
				case ServiceType.Both:
				case ServiceType.Client:
					break;
			}
		}
		private void ServiceStopCore(bool bReportStatus) {
			WinApi.ServiceStatus ssServiceStatus = new WinApi.ServiceStatus();
			switch(this.stType) {
				case ServiceType.Server:
					Service.Log("Service server stop");
					break;
				case ServiceType.Both:
					Service.Log("Service both stop");
					break;
				case ServiceType.Client:
					Service.Log("Service client stop");
					break;
			}
			switch(this.stType) {
				case ServiceType.Server when bReportStatus:
					ssServiceStatus.dwCurrentState = WinApi.ServiceCurrentState.SERVICE_STOP_PENDING;
					ssServiceStatus.dwWaitHint = 100000;
					WinApi.Advapi32.SetServiceStatus(this.ServiceHandle, ref ssServiceStatus);
					break;
				case ServiceType.Server:
				case ServiceType.Both:
				case ServiceType.Client:
					break;
			}
			switch(this.stType) {
				case ServiceType.Server:
					Service.ewhServerStarted.Reset();
					break;
				case ServiceType.Both:
					break;
				case ServiceType.Client:
					Service.ewhClientStarted.Reset();
					break;
			}
			Service.mreStop.Set();
			this.thrServer?.Join();
			this.thrServer = null;
			switch(this.stType) {
				case ServiceType.Server:
					Service.Log("Service server stoped");
					break;
				case ServiceType.Both:
					Service.Log("Service both stoped");
					break;
				case ServiceType.Client:
					Service.Log("Service client stoped");
					break;
			}
			switch(this.stType) {
				case ServiceType.Server when bReportStatus:
					ssServiceStatus.dwCurrentState = WinApi.ServiceCurrentState.SERVICE_STOPPED;
					WinApi.Advapi32.SetServiceStatus(this.ServiceHandle, ref ssServiceStatus);
					break;
				case ServiceType.Server:
					break;
				case ServiceType.Both:
				case ServiceType.Client:
					break;
			}
			Service.ewhServerStarted.Close();
			Service.ewhServerStarted.Dispose();
			Service.ewhClientStarted.Close();
			Service.ewhClientStarted.Dispose();
			Service.ewhSessionChanged.Close();
			Service.ewhSessionChanged.Dispose();
			Service.ewhClientConnecting.Close();
			Service.ewhClientConnecting.Dispose();
			Service.ewhServerMessage.Close();
			Service.ewhServerMessage.Dispose();
			Service.ewhServerDisconnecting.Close();
			Service.ewhServerDisconnecting.Dispose();
			this.bStarted = false;
		}
		protected override void OnStart(string[] args) {
			this.ServiceStart();
		}
		protected override void OnStop() {
			this.ServiceStop();
		}
		protected override void OnSessionChange(System.ServiceProcess.SessionChangeDescription scd) {
			switch(scd.Reason) {
				case System.ServiceProcess.SessionChangeReason.ConsoleConnect:
					Service.areSessionChanged.Set();
					break;
				default:
					break;
			}
		}
		public static void Log(string sLog) {
			MagicRemoteService.Logger.Write(MagicRemoteService.LogLevel.Information, sLog);
		}
		public static void LogIfDebug(string sLog) {
			MagicRemoteService.Logger.Write(MagicRemoteService.LogLevel.Debug, sLog);
		}
		public static void Warn(string sWarn) {
			MagicRemoteService.Logger.Write(MagicRemoteService.LogLevel.Warning, sWarn);
		}
		public static void Error(string sError) {
			MagicRemoteService.Logger.Write(MagicRemoteService.LogLevel.Error, sError);
		}
		private static bool SetThreadInputDesktop() {
			System.IntPtr hInputDesktop = WinApi.User32.OpenInputDesktop(0, true, 0x10000000);
			if(System.IntPtr.Zero == hInputDesktop) {
				return false;
			} else if(!WinApi.User32.SetThreadDesktop(hInputDesktop)) {
				WinApi.User32.CloseDesktop(hInputDesktop);
				return false;
			} else {
				WinApi.User32.CloseDesktop(hInputDesktop);
				return true;
			}
		}
		private static uint SendInputAdmin(WinApi.Input[] pInputs) {
			uint uiInput = WinApi.User32.SendInput((uint)pInputs.Length, pInputs, System.Runtime.InteropServices.Marshal.SizeOf(typeof(WinApi.Input)));
			if(0 == uiInput && SetThreadInputDesktop()) {
				return WinApi.User32.SendInput((uint)pInputs.Length, pInputs, System.Runtime.InteropServices.Marshal.SizeOf(typeof(WinApi.Input)));
			} else {
				return uiInput;
			}
		}
		private static uint OpenUserInteractiveProcess(string strApplication, string strArgument) {
			uint uiSessionId = WinApi.Kernel32.WTSGetActiveConsoleSessionId();
			if(uiSessionId == 0xFFFFFFFF) {
				return 0;
			} else {
				System.Diagnostics.Process pWinlogon = System.Array.Find<System.Diagnostics.Process>(System.Diagnostics.Process.GetProcessesByName("winlogon"), delegate (System.Diagnostics.Process p) {
					return (uint)p.SessionId == uiSessionId;
				});
				if(pWinlogon == null) {
					throw new System.Exception("Unable to get winlogon process");
				}

				System.IntPtr hProcessToken;
				if(!WinApi.Advapi32.OpenProcessToken(pWinlogon.Handle, 0x0002, out hProcessToken)) {
					throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
				}

				System.IntPtr hProcessTokenDupplicate;
				WinApi.SecurityAttributes sa = new WinApi.SecurityAttributes();
				sa.Length = System.Runtime.InteropServices.Marshal.SizeOf(sa);
				if(!WinApi.Advapi32.DuplicateTokenEx(hProcessToken, WinApi.Advapi32.MAXIMUM_ALLOWED, ref sa, WinApi.SecurityImpersonationLevel.SecurityImpersonation, WinApi.TokenType.TokenPrimary, out hProcessTokenDupplicate)) {
					WinApi.Kernel32.CloseHandle(hProcessToken);
					throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
				}
				WinApi.Kernel32.CloseHandle(hProcessToken);

				System.IntPtr lpEnvironmentBlock;
				System.IntPtr hUserToken;
				if(!WinApi.Wtsapi32.WTSQueryUserToken(uiSessionId, out hUserToken)) {
					lpEnvironmentBlock = System.IntPtr.Zero;
					//throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
				} else {
					if(!WinApi.Userenv.CreateEnvironmentBlock(out lpEnvironmentBlock, hUserToken, true)) {
						WinApi.Kernel32.CloseHandle(hUserToken);
						throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
					}
					WinApi.Kernel32.CloseHandle(hUserToken);
				}

				WinApi.StartupInfo si = new WinApi.StartupInfo();
				WinApi.ProcessInformation piProcess;
				si.cb = System.Runtime.InteropServices.Marshal.SizeOf(si);
				si.lpDesktop = @"winsta0\default";
				if(!WinApi.Advapi32.CreateProcessAsUser(hProcessTokenDupplicate, strApplication, strArgument, ref sa, ref sa, false, 0x00000400, lpEnvironmentBlock, System.IO.Path.GetDirectoryName(strApplication), ref si, out piProcess)) {
					WinApi.Kernel32.CloseHandle(hProcessTokenDupplicate);
					if(lpEnvironmentBlock != System.IntPtr.Zero) {
						WinApi.Userenv.DestroyEnvironmentBlock(lpEnvironmentBlock);
					}
					throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
				}

				WinApi.Kernel32.CloseHandle(hProcessTokenDupplicate);
				if(lpEnvironmentBlock != System.IntPtr.Zero) {
					WinApi.Userenv.DestroyEnvironmentBlock(lpEnvironmentBlock);
				}

				return piProcess.dwProcessId;
			}
		}
		private void ThreadServer() {
			try {
				switch(this.stType) {
					case ServiceType.Server:
						this.ThreadServerServer();
						break;
					case ServiceType.Both:
						this.ThreadServerBoth();
						break;
					case ServiceType.Client:
						this.ThreadServerClient();
						break;
				}
			} catch(System.Exception eException) {
				Service.Error("Server thread failure (" + this.stType + "): " + eException.ToString());
				this.ServiceRestartAfterFailure();
			}
		}
		// The accepted socket is handed to the client process over the pipe as one message: options (int32), length (int32), protocol information
		private static void WriteSocketInformation(System.IO.Stream sPipe, System.Net.Sockets.SocketInformation si) {
			byte[] tabMessage = new byte[8 + si.ProtocolInformation.Length];
			System.Buffer.BlockCopy(System.BitConverter.GetBytes((int)si.Options), 0, tabMessage, 0, 4);
			System.Buffer.BlockCopy(System.BitConverter.GetBytes(si.ProtocolInformation.Length), 0, tabMessage, 4, 4);
			System.Buffer.BlockCopy(si.ProtocolInformation, 0, tabMessage, 8, si.ProtocolInformation.Length);
			sPipe.Write(tabMessage, 0, tabMessage.Length);
			sPipe.Flush();
		}
		private static System.Net.Sockets.SocketInformation ReadSocketInformation(System.IO.Stream sPipe) {
			byte[] tabHeader = Service.ReadExactly(sPipe, 8);
			int iLength = System.BitConverter.ToInt32(tabHeader, 4);
			if(iLength <= 0 || iLength > 4096) {
				throw new System.IO.InvalidDataException("Invalid socket information length " + iLength + " received from the service");
			}
			return new System.Net.Sockets.SocketInformation {
				Options = (System.Net.Sockets.SocketInformationOptions)System.BitConverter.ToInt32(tabHeader, 0),
				ProtocolInformation = Service.ReadExactly(sPipe, iLength)
			};
		}
		private static byte[] ReadExactly(System.IO.Stream sPipe, int iCount) {
			byte[] tabData = new byte[iCount];
			int iRead = 0;
			while(iRead < iCount) {
				int i = sPipe.Read(tabData, iRead, iCount - iRead);
				if(i <= 0) {
					throw new System.IO.EndOfStreamException("Service pipe closed while receiving a socket");
				}
				iRead += i;
			}
			return tabData;
		}
		private static System.Net.Sockets.Socket CreateListenSocket(int iPort) {
			System.Net.Sockets.Socket socListen = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
			try {
				socListen.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, iPort));
				socListen.Listen(10);
			} catch(System.Net.Sockets.SocketException eException) {
				socListen.Close();
				throw new System.Exception("Unable to listen on TCP port " + iPort + " (" + eException.SocketErrorCode + "), is another program using it?", eException);
			}
			Service.Log("Listening on TCP port " + iPort);
			return socListen;
		}
		// Returns the AcceptAsync result after re-arming, or throws when the listening socket itself is unusable
		private static bool AcceptFailed(System.Net.Sockets.Socket socListen, System.Net.Sockets.SocketAsyncEventArgs eaAcceptAsync) {
			System.Net.Sockets.SocketError seError = eaAcceptAsync.SocketError;
			switch(seError) {
				case System.Net.Sockets.SocketError.ConnectionReset:
				case System.Net.Sockets.SocketError.ConnectionAborted:
					Service.Warn("Incoming connection aborted before accept (" + seError + ")");
					eaAcceptAsync.AcceptSocket?.Close();
					eaAcceptAsync.AcceptSocket = null;
					return socListen.AcceptAsync(eaAcceptAsync);
				default:
					throw new System.Net.Sockets.SocketException((int)seError);
			}
		}
		private static void JoinClientThreads(System.Collections.Generic.List<System.Threading.Thread> liClient) {
			foreach(System.Threading.Thread thr in liClient) {
				if(!thr.Join(System.TimeSpan.FromSeconds(10))) {
					Service.Warn("Client thread did not stop within 10s");
				}
			}
			liClient.Clear();
		}
		private void ThreadServerServer() {
			System.Net.Sockets.Socket socServer = null;
			System.Threading.AutoResetEvent areServerAcceptAsyncCompleted = new System.Threading.AutoResetEvent(false);
			System.Net.Sockets.SocketAsyncEventArgs eaServerAcceptAsync = new System.Net.Sockets.SocketAsyncEventArgs();
			System.Net.Sockets.Socket socClientToSend = null;
			System.IO.Pipes.NamedPipeServerStream psServer = null;
			System.Diagnostics.Process pClient = null;
			System.Threading.AutoResetEvent areWaitForExitExited = new System.Threading.AutoResetEvent(false);
			void ServerAcceptAsyncCompleted(object o, System.Net.Sockets.SocketAsyncEventArgs e) {
				areServerAcceptAsyncCompleted.Set();
			};
			void ClientWaitForExitExited(object o, System.EventArgs e) {
				areWaitForExitExited.Set();
			};
			void ReleaseClientProcess() {
				if(pClient != null) {
					pClient.EnableRaisingEvents = false;
					pClient.Exited -= ClientWaitForExitExited;
					pClient.Close();
					pClient.Dispose();
					pClient = null;
				}
			};
			eaServerAcceptAsync.Completed += ServerAcceptAsyncCompleted;
			try {
				socServer = Service.CreateListenSocket(this.iPort);
				if(!socServer.AcceptAsync(eaServerAcceptAsync)) {
					ServerAcceptAsyncCompleted(socServer, eaServerAcceptAsync);
				}

				psServer = new System.IO.Pipes.NamedPipeServerStream("{2DCF2389-4969-483D-AA13-58FD8DDDD2D5}", System.IO.Pipes.PipeDirection.Out, 1, System.IO.Pipes.PipeTransmissionMode.Message, System.IO.Pipes.PipeOptions.Asynchronous, 4096, 4096);

				System.Threading.WaitHandle[] tabEventServer = new System.Threading.WaitHandle[] {
					Service.mreStop,
					Service.areSessionChanged,
					Service.ewhClientConnecting,
					areServerAcceptAsyncCompleted,
					areWaitForExitExited
				};
				do {
					switch(System.Threading.WaitHandle.WaitAny(tabEventServer, -1)) {
						case 0:
							break;
						case 1:
							if(psServer.IsConnected && Service.ewhClientStarted.WaitOne(System.TimeSpan.Zero) && pClient != null && !pClient.HasExited) {
								Service.ewhSessionChanged.Set();
							} else if(socClientToSend != null) {
								if(!psServer.IsConnected || pClient == null || pClient.HasExited) {
									areWaitForExitExited.Reset();
									OpenUserInteractiveProcess(System.Reflection.Assembly.GetExecutingAssembly().Location, "-c");
								}
							}
							break;
						case 2:
							ReleaseClientProcess();
							if(psServer.IsConnected) {
								psServer.Disconnect();
							}
							System.IAsyncResult arWaitForConnection = psServer.BeginWaitForConnection(null, null);
							if(System.Threading.WaitHandle.WaitAny(new System.Threading.WaitHandle[] { Service.mreStop, arWaitForConnection.AsyncWaitHandle }, 30000) != 1) {
								throw new System.TimeoutException("Client process signaled but did not connect to the pipe");
							}
							psServer.EndWaitForConnection(arWaitForConnection);
							pClient = psServer.GetClientProcess();
							pClient.Exited += ClientWaitForExitExited;
							pClient.EnableRaisingEvents = true;
							Service.Log("Client process connected [" + pClient.Id + "]");
							if(socClientToSend != null) {
								Service.WriteSocketInformation(psServer, socClientToSend.DuplicateAndClose(pClient.Id));
								Service.ewhServerMessage.Set();
								socClientToSend.Dispose();
								socClientToSend = null;
							}
							break;
						case 3:
							if(eaServerAcceptAsync.SocketError != System.Net.Sockets.SocketError.Success) {
								if(!Service.AcceptFailed(socServer, eaServerAcceptAsync)) {
									ServerAcceptAsyncCompleted(socServer, eaServerAcceptAsync);
								}
								break;
							}
							if(socClientToSend != null) {
								socClientToSend.Close();
								socClientToSend.Dispose();
								socClientToSend = null;
							}
							if(psServer.IsConnected && Service.ewhClientStarted.WaitOne(System.TimeSpan.Zero) && pClient != null && !pClient.HasExited) {
								Service.WriteSocketInformation(psServer, eaServerAcceptAsync.AcceptSocket.DuplicateAndClose(pClient.Id));
								Service.ewhServerMessage.Set();
								eaServerAcceptAsync.AcceptSocket.Dispose();
							} else {
								socClientToSend = eaServerAcceptAsync.AcceptSocket;
								if(!psServer.IsConnected || pClient == null || pClient.HasExited) {
									Service.Log("Starting client process in the active console session");
									areWaitForExitExited.Reset();
									OpenUserInteractiveProcess(System.Reflection.Assembly.GetExecutingAssembly().Location, "-c");
								}
							}
							eaServerAcceptAsync.AcceptSocket = null;
							if(!socServer.AcceptAsync(eaServerAcceptAsync)) {
								ServerAcceptAsyncCompleted(socServer, eaServerAcceptAsync);
							}
							break;
						case 4:
							Service.Warn("Client process exited");
							if(socClientToSend != null) {
								OpenUserInteractiveProcess(System.Reflection.Assembly.GetExecutingAssembly().Location, "-c");
							}
							break;
						default:
							throw new System.Exception("Unmanaged handle error");
					}
				} while(!Service.mreStop.WaitOne(System.TimeSpan.Zero));
			} finally {
				Service.ewhServerDisconnecting.Set();

				ReleaseClientProcess();
				areWaitForExitExited.Close();
				areWaitForExitExited.Dispose();

				if(socClientToSend != null) {
					socClientToSend.Close();
					socClientToSend.Dispose();
				}

				if(psServer != null) {
					if(psServer.IsConnected) {
						psServer.Disconnect();
					}
					psServer.Close();
					psServer.Dispose();
				}

				eaServerAcceptAsync.Completed -= ServerAcceptAsyncCompleted;
				eaServerAcceptAsync.Dispose();
				areServerAcceptAsyncCompleted.Close();
				areServerAcceptAsyncCompleted.Dispose();
				if(socServer != null) {
					socServer.Close();
					socServer.Dispose();
				}
			}
		}
		private void ThreadServerBoth() {
			System.Net.Sockets.Socket socBoth = null;
			System.Threading.AutoResetEvent areBothAcceptAsyncCompleted = new System.Threading.AutoResetEvent(false);
			System.Net.Sockets.SocketAsyncEventArgs eaBothAcceptAsync = new System.Net.Sockets.SocketAsyncEventArgs();
			System.Collections.Generic.List<System.Threading.Thread> liClientBoth = new System.Collections.Generic.List<System.Threading.Thread>();
			void BothAcceptAsyncCompleted(object o, System.Net.Sockets.SocketAsyncEventArgs e) {
				areBothAcceptAsyncCompleted.Set();
			};
			eaBothAcceptAsync.Completed += BothAcceptAsyncCompleted;
			try {
				socBoth = Service.CreateListenSocket(this.iPort);
				if(!socBoth.AcceptAsync(eaBothAcceptAsync)) {
					BothAcceptAsyncCompleted(socBoth, eaBothAcceptAsync);
				}

				System.Threading.WaitHandle[] tabEventBoth = new System.Threading.WaitHandle[] {
					Service.mreStop,
					Service.ewhServerStarted,
					areBothAcceptAsyncCompleted
				};
				do {
					switch(System.Threading.WaitHandle.WaitAny(tabEventBoth, -1)) {
						case 0:
							break;
						case 1:
							Service.mreStop.Set();
							this.ServiceRestart();
							break;
						case 2:
							if(eaBothAcceptAsync.SocketError != System.Net.Sockets.SocketError.Success) {
								if(!Service.AcceptFailed(socBoth, eaBothAcceptAsync)) {
									BothAcceptAsyncCompleted(socBoth, eaBothAcceptAsync);
								}
								break;
							}
							System.Net.Sockets.Socket socClient = eaBothAcceptAsync.AcceptSocket;

							System.Threading.Thread thrClient = new System.Threading.Thread(delegate () {
								this.ThreadClient(socClient);
							});
							thrClient.Start();
							liClientBoth.RemoveAll(delegate (System.Threading.Thread thr) {
								return !thr.IsAlive;
							});
							liClientBoth.Add(thrClient);

							eaBothAcceptAsync.AcceptSocket = null;
							if(!socBoth.AcceptAsync(eaBothAcceptAsync)) {
								BothAcceptAsyncCompleted(socBoth, eaBothAcceptAsync);
							}
							break;
						default:
							throw new System.Exception("Unmanaged handle error");
					}
				} while(!Service.mreStop.WaitOne(System.TimeSpan.Zero));
			} finally {
				Service.mreStop.Set();
				Service.JoinClientThreads(liClientBoth);

				eaBothAcceptAsync.Completed -= BothAcceptAsyncCompleted;
				eaBothAcceptAsync.Dispose();
				areBothAcceptAsyncCompleted.Close();
				areBothAcceptAsyncCompleted.Dispose();
				if(socBoth != null) {
					socBoth.Close();
					socBoth.Dispose();
				}
			}
		}
		private void ThreadServerClient() {
			System.IO.Pipes.NamedPipeClientStream psClient = new System.IO.Pipes.NamedPipeClientStream(".", "{2DCF2389-4969-483D-AA13-58FD8DDDD2D5}", System.IO.Pipes.PipeDirection.In, System.IO.Pipes.PipeOptions.Asynchronous);
			System.Collections.Generic.List<System.Threading.Thread> liClient = new System.Collections.Generic.List<System.Threading.Thread>();
			try {
				Service.ewhClientConnecting.Set();
				while(!psClient.IsConnected) {
					try {
						psClient.Connect(1000);
					} catch(System.TimeoutException) {
						if(Service.mreStop.WaitOne(System.TimeSpan.Zero)) {
							return;
						}
					}
				}
				Service.Log("Connected to service pipe");

				System.Threading.WaitHandle[] tabEventClient = new System.Threading.WaitHandle[] {
					Service.mreStop,
					Service.ewhSessionChanged,
					Service.ewhServerDisconnecting,
					Service.ewhServerMessage
				};
				do {
					switch(System.Threading.WaitHandle.WaitAny(tabEventClient, -1)) {
						case 0:
							break;
						case 1:
							Service.mreStop.Set();
							System.Threading.Tasks.Task.Run(delegate () {
								this.ServiceStop();
								System.Windows.Forms.Application.Exit();
							});
							break;
						case 2:
							Service.mreStop.Set();
							if(!(System.Array.IndexOf<string>(System.Environment.GetCommandLineArgs(), "-c") < 0) && System.Windows.Forms.Application.OpenForms.Count == 0) {
								System.Threading.Tasks.Task.Run(delegate () {
									this.ServiceStop();
									System.Windows.Forms.Application.Exit();
								});
							} else {
								this.ServiceRestart();
							}
							break;
						case 3:
							System.Net.Sockets.Socket socClient = new System.Net.Sockets.Socket(Service.ReadSocketInformation(psClient));

							System.Threading.Thread thrClient = new System.Threading.Thread(delegate () {
								this.ThreadClient(socClient);
							});
							thrClient.Start();
							liClient.RemoveAll(delegate (System.Threading.Thread thr) {
								return !thr.IsAlive;
							});
							liClient.Add(thrClient);
							break;
						default:
							throw new System.Exception("Unmanaged handle error");
					}
				} while(!Service.mreStop.WaitOne(System.TimeSpan.Zero));
			} finally {
				psClient.Close();
				psClient.Dispose();

				Service.mreStop.Set();
				Service.JoinClientThreads(liClient);
			}
		}
		private static int MessageLength(byte ucType) {
			switch(ucType) {
				case (byte)MagicRemoteService.MessageType.PositionRelative:
				case (byte)MagicRemoteService.MessageType.PositionAbsolute:
					return 5;
				case (byte)MagicRemoteService.MessageType.Wheel:
				case (byte)MagicRemoteService.MessageType.Unicode:
					return 3;
				case (byte)MagicRemoteService.MessageType.Visible:
					return 2;
				case (byte)MagicRemoteService.MessageType.Key:
					return 4;
				default:
					return 1;
			}
		}
		private static byte[] FrameText(string strText) {
			byte[] tabText = System.Text.Encoding.UTF8.GetBytes(strText);
			byte[] tabHeader;
			if(tabText.Length < 126) {
				tabHeader = new byte[] { (0b1000 << 4) | (0x1 << 0), (byte)tabText.Length };
			} else if(tabText.Length <= 0xFFFF) {
				tabHeader = new byte[] { (0b1000 << 4) | (0x1 << 0), 126, (byte)(tabText.Length >> 8), (byte)tabText.Length };
			} else {
				tabHeader = new byte[] { (0b1000 << 4) | (0x1 << 0), 127, 0, 0, 0, 0, (byte)(tabText.Length >> 24), (byte)(tabText.Length >> 16), (byte)(tabText.Length >> 8), (byte)tabText.Length };
			}
			byte[] tabFrame = new byte[tabHeader.Length + tabText.Length];
			System.Buffer.BlockCopy(tabHeader, 0, tabFrame, 0, tabHeader.Length);
			System.Buffer.BlockCopy(tabText, 0, tabFrame, tabHeader.Length, tabText.Length);
			return tabFrame;
		}
		// Text frames from the TV app carry JSON: {"t":"hello","sdk":version} once connected, then forwarded log messages {"t":"log","l":level,"m":message}.
		// The PC only sends text frames ({"t":"loglevel","l":level}) after the hello, as older TV apps would show them as a notification.
		// Returns the message type.
		private static string ProcessTextMessage(string strMessage, string strClient) {
			try {
				using(System.Text.Json.JsonDocument jdMessage = System.Text.Json.JsonDocument.Parse(strMessage)) {
					System.Text.Json.JsonElement jeRoot = jdMessage.RootElement;
					string strType = jeRoot.ValueKind == System.Text.Json.JsonValueKind.Object && jeRoot.TryGetProperty("t", out System.Text.Json.JsonElement jeType) && jeType.ValueKind == System.Text.Json.JsonValueKind.String ? jeType.GetString() : null;
					if(strType == "hello") {
						Service.Log("TV app on socket " + strClient + " supports log forwarding (webOS SDK " + (jeRoot.TryGetProperty("sdk", out System.Text.Json.JsonElement jeSdk) && jeSdk.ValueKind == System.Text.Json.JsonValueKind.String ? jeSdk.GetString() : "?") + ")");
						return strType;
					} else if(strType == "log") {
						int iLevel = jeRoot.TryGetProperty("l", out System.Text.Json.JsonElement jeLevel) && jeLevel.ValueKind == System.Text.Json.JsonValueKind.Number && jeLevel.TryGetInt32(out int iValue) ? iValue : (int)MagicRemoteService.LogLevel.Information;
						MagicRemoteService.LogLevel llLevel = (MagicRemoteService.LogLevel)System.Math.Max((int)MagicRemoteService.LogLevel.Error, System.Math.Min((int)MagicRemoteService.LogLevel.Debug, iLevel));
						string strLog = jeRoot.TryGetProperty("m", out System.Text.Json.JsonElement jeLog) && jeLog.ValueKind == System.Text.Json.JsonValueKind.String ? jeLog.GetString() : "";
						// Only the TV's warnings and errors go to the Event Log, everything goes to the log file
						MagicRemoteService.Logger.Write(llLevel, "TV " + strClient + ": " + strLog, llLevel <= MagicRemoteService.LogLevel.Warning);
						return strType;
					}
				}
			} catch(System.Text.Json.JsonException) {
			}
			Service.Warn("Unprocessed text message on socket " + strClient + " [" + strMessage + "]");
			return null;
		}
		private static bool TrySend(System.Net.Sockets.Socket socClient, byte[] tabData, string strClient) {
			try {
				socClient.Send(tabData);
				return true;
			} catch(System.Exception eException) when(eException is System.Net.Sockets.SocketException || eException is System.ObjectDisposedException) {
				Service.LogIfDebug("Send failed on socket " + strClient + ": " + eException.Message);
				return false;
			}
		}
		private void ThreadClient(System.Net.Sockets.Socket socClient) {
			string strClient = "[" + socClient.GetHashCode() + "]";
			try {
				strClient = socClient.RemoteEndPoint + " " + strClient;
			} catch(System.Exception) {
			}
			string strStopReason = "service stopping";
			bool bClientClosed = false;
			byte[] tabData = new byte[65536];
			int iBuffered = 0;
			int iLogLevelSent = -1;
			bool bTextCapable = false;
			MagicRemoteService.ConnectionInfo ciConnection = null;
			System.Threading.AutoResetEvent areClientReceiveAsyncCompleted = new System.Threading.AutoResetEvent(false);
			System.Threading.ManualResetEvent mreClientStop = new System.Threading.ManualResetEvent(false);
			System.Net.Sockets.SocketAsyncEventArgs eaClientReceiveAsync = new System.Net.Sockets.SocketAsyncEventArgs();
			System.Timers.Timer tUserInput = null;
			System.Timers.Timer tPongUserInput = null;
			System.Timers.Timer tInactivity = null;
			System.Timers.Timer tVideoInput = null;
			System.Timers.Timer tPong = null;
			System.Timers.Timer tPing = null;
			void ClientReceiveAsyncCompleted(object o, System.Net.Sockets.SocketAsyncEventArgs e) {
				areClientReceiveAsyncCompleted.Set();
			};
			void ClientStop(string strReason) {
				if(!mreClientStop.WaitOne(System.TimeSpan.Zero)) {
					strStopReason = strReason;
					mreClientStop.Set();
				}
			};
			// Tell the TV app which of its log messages to forward, so debug traffic is only sent when wanted
			void SendLogLevel() {
				int iLevel = (int)MagicRemoteService.Logger.Level;
				if(bTextCapable && iLevel != iLogLevelSent && Service.TrySend(socClient, Service.FrameText("{\"t\":\"loglevel\",\"l\":" + iLevel + "}"), strClient)) {
					iLogLevelSent = iLevel;
				}
			};
			void PowerSettingNotificationArrived(WinApi.PowerBroadcastSetting pbs) {
				if(pbs.PowerSetting == WinApi.User32.GUID_MONITOR_POWER_ON && tVideoInput != null) {
					try {
						switch(pbs.Data) {
							case 0:
								if(this.bVideoInput) {
									tVideoInput.Start();
								}
								break;
							case 1:
								if(this.bVideoInput) {
									tVideoInput.Stop();
								}
								break;
						}
					} catch(System.ObjectDisposedException) {
						// Connection closed while the notification was being delivered
					}
				}
			};
			try {
				Service.Log("Socket accepted " + strClient);
				eaClientReceiveAsync.SetBuffer(tabData, 0, tabData.Length);
				eaClientReceiveAsync.Completed += ClientReceiveAsyncCompleted;
				if(!socClient.ReceiveAsync(eaClientReceiveAsync)) {
					ClientReceiveAsyncCompleted(socClient, eaClientReceiveAsync);
				}

				tUserInput = new System.Timers.Timer {
					Interval = 10,
					AutoReset = true
				};
				tUserInput.Elapsed += delegate (object oSource, System.Timers.ElapsedEventArgs eElapsed) {
					WinApi.LastInputInfo lii = new WinApi.LastInputInfo();
					lii.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(lii);
					if(!WinApi.User32.GetLastInputInfo(ref lii)) {
					} else if(((uint)System.Environment.TickCount - lii.dwTime) < 10) {
						tUserInput.Stop();
						if(bClientClosed) {
							tUserInput.Dispose();
						}
						System.Diagnostics.Process pProcess = new System.Diagnostics.Process();
						pProcess.StartInfo.FileName = "shutdown";
						pProcess.StartInfo.Arguments = "/a";
						pProcess.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
						pProcess.Start();
						pProcess.Dispose();
						Service.Log("Client user input activity on socket " + strClient + ", shutdown aborted");
					}
				};
				tPongUserInput = new System.Timers.Timer {
					Interval = 5000,
					AutoReset = false
				};
				tPongUserInput.Elapsed += delegate (object oSource, System.Timers.ElapsedEventArgs eElapsed) {
					ClientStop("no answer to inactivity ping within 5s");
					Service.TrySend(socClient, Service.tabClose, strClient);
					Service.Warn("Client timeout pong inactivity on socket " + strClient);
				};
				// The TV is asked 5 minutes before the timeout; clamp so a timeout of 5 minutes or less still gives a valid interval
				tInactivity = new System.Timers.Timer {
					Interval = System.Math.Max(60000, this.iTimeoutInactivity - 300000),
					AutoReset = false
				};
				tInactivity.Elapsed += delegate (object oSource, System.Timers.ElapsedEventArgs eElapsed) {
					Service.Log("Client timeout inactivity on socket " + strClient);
					if(Service.TrySend(socClient, Service.tabPingUserInput, strClient)) {
						tPongUserInput.Start();
					} else {
						ClientStop("send failed (inactivity ping)");
					}
				};

				tVideoInput = new System.Timers.Timer {
					Interval = System.Math.Max(60000, this.iTimeoutVideoInput - 300000),
					AutoReset = false
				};
				tVideoInput.Elapsed += delegate (object oSource, System.Timers.ElapsedEventArgs eElapsed) {
					Service.Log("Client timeout video input on socket " + strClient);
					if(Service.TrySend(socClient, Service.tabPingUserInput, strClient)) {
						tPongUserInput.Start();
					} else {
						ClientStop("send failed (video input ping)");
					}
				};

				tPong = new System.Timers.Timer {
					Interval = 5000,
					AutoReset = false
				};
				tPong.Elapsed += delegate (object oSource, System.Timers.ElapsedEventArgs eElapsed) {
					ClientStop("no pong within 5s, TV unreachable");
					Service.TrySend(socClient, Service.tabClose, strClient);
					Service.Warn("Client timeout pong on socket " + strClient);
				};
				tPing = new System.Timers.Timer {
					Interval = 30000,
					AutoReset = true
				};
				tPing.Elapsed += delegate (object oSource, System.Timers.ElapsedEventArgs eElapsed) {
					SendLogLevel();
					if(Service.TrySend(socClient, Service.tabPing, strClient)) {
						tPong.Start();
					} else {
						ClientStop("send failed (ping)");
					}
				};

				MagicRemoteService.Application.naehPowerSettingNotificationArrived += PowerSettingNotificationArrived;

				System.Collections.Generic.Dictionary<ushort, WinApi.Input[]> dBindDown = new System.Collections.Generic.Dictionary<ushort, WinApi.Input[]>();
				System.Collections.Generic.Dictionary<ushort, WinApi.Input[]> dBindUp = new System.Collections.Generic.Dictionary<ushort, WinApi.Input[]>();
				System.Collections.Generic.Dictionary<ushort, byte[][]> dBindActionDown = new System.Collections.Generic.Dictionary<ushort, byte[][]>();
				System.Collections.Generic.Dictionary<ushort, string[]> dBindCommandDown = new System.Collections.Generic.Dictionary<ushort, string[]>();
				foreach(System.Collections.Generic.KeyValuePair<ushort, Bind[]> kvp in this.dBind) {
					if(kvp.Value != null) {
						System.Collections.Generic.List<WinApi.Input> liBindDown = new System.Collections.Generic.List<WinApi.Input>();
						System.Collections.Generic.List<WinApi.Input> liBindUp = new System.Collections.Generic.List<WinApi.Input>();
						System.Collections.Generic.List<byte[]> liBindActionDown = new System.Collections.Generic.List<byte[]>();
						System.Collections.Generic.List<string> liBindCommandDown = new System.Collections.Generic.List<string>();
						foreach(Bind b in kvp.Value) {
							switch(b) {
								case MagicRemoteService.BindMouse bm:
									switch(bm.bmvValue) {
										case BindMouseValue.Left:
											liBindDown.Add(new WinApi.Input {
												type = WinApi.InputType.INPUT_MOUSE,
												u = new WinApi.InputDummyUnionName {
													mi = new WinApi.MouseInput {
														dwFlags = WinApi.MouseInputFlags.MOUSEEVENTF_LEFTDOWN,
														dwExtraInfo = System.IntPtr.Zero
													}
												}
											});
											liBindUp.Add(new WinApi.Input {
												type = WinApi.InputType.INPUT_MOUSE,
												u = new WinApi.InputDummyUnionName {
													mi = new WinApi.MouseInput {
														dwFlags = WinApi.MouseInputFlags.MOUSEEVENTF_LEFTUP,
														dwExtraInfo = System.IntPtr.Zero
													}
												}
											});
											break;
										case BindMouseValue.Right:
											liBindDown.Add(new WinApi.Input {
												type = WinApi.InputType.INPUT_MOUSE,
												u = new WinApi.InputDummyUnionName {
													mi = new WinApi.MouseInput {
														dwFlags = WinApi.MouseInputFlags.MOUSEEVENTF_RIGHTDOWN,
														dwExtraInfo = System.IntPtr.Zero
													}
												}
											});
											liBindUp.Add(new WinApi.Input {
												type = WinApi.InputType.INPUT_MOUSE,
												u = new WinApi.InputDummyUnionName {
													mi = new WinApi.MouseInput {
														dwFlags = WinApi.MouseInputFlags.MOUSEEVENTF_RIGHTUP,
														dwExtraInfo = System.IntPtr.Zero
													}
												}
											});
											break;
										case BindMouseValue.Middle:
											liBindDown.Add(new WinApi.Input {
												type = WinApi.InputType.INPUT_MOUSE,
												u = new WinApi.InputDummyUnionName {
													mi = new WinApi.MouseInput {
														dwFlags = WinApi.MouseInputFlags.MOUSEEVENTF_MIDDLEDOWN,
														dwExtraInfo = System.IntPtr.Zero
													}
												}
											});
											liBindUp.Add(new WinApi.Input {
												type = WinApi.InputType.INPUT_MOUSE,
												u = new WinApi.InputDummyUnionName {
													mi = new WinApi.MouseInput {
														dwFlags = WinApi.MouseInputFlags.MOUSEEVENTF_MIDDLEUP,
														dwExtraInfo = System.IntPtr.Zero
													}
												}
											});
											break;
									}
									break;
								case MagicRemoteService.BindKeyboard bk:
									liBindDown.Add(new WinApi.Input {
										type = WinApi.InputType.INPUT_KEYBOARD,
										u = new WinApi.InputDummyUnionName {
											ki = new WinApi.KeybdInput {
												wVk = bk.ucVirtualKey,
												wScan = bk.ucScanCode,
												dwFlags = bk.bExtended ? (WinApi.KeybdInputFlags.KEYEVENTF_EXTENDEDKEY | WinApi.KeybdInputFlags.KEYEVENTF_KEYDOWN) : WinApi.KeybdInputFlags.KEYEVENTF_KEYDOWN,
												dwExtraInfo = System.IntPtr.Zero
											}
										}
									});
									liBindUp.Add(new WinApi.Input {
										type = WinApi.InputType.INPUT_KEYBOARD,
										u = new WinApi.InputDummyUnionName {
											ki = new WinApi.KeybdInput {
												wVk = bk.ucVirtualKey,
												wScan = bk.ucScanCode,
												dwFlags = bk.bExtended ? (WinApi.KeybdInputFlags.KEYEVENTF_EXTENDEDKEY | WinApi.KeybdInputFlags.KEYEVENTF_KEYUP) : WinApi.KeybdInputFlags.KEYEVENTF_KEYUP,
												dwExtraInfo = System.IntPtr.Zero
											}
										}
									});
									break;
								case MagicRemoteService.BindAction ba:
									liBindActionDown.Add(new byte[] {
										(0b1000 << 4) | (0x2 << 0),
										0x01,
										(byte)ba.bavValue
									});
									break;
								case MagicRemoteService.BindCommand bc:
									liBindCommandDown.Add(bc.strCommand);
									break;
							}
						}
						if(liBindDown.Count > 0) {
							dBindDown.Add(kvp.Key, liBindDown.ToArray());
						}
						if(liBindUp.Count > 0) {
							dBindUp.Add(kvp.Key, liBindUp.ToArray());
						}
						if(liBindActionDown.Count > 0) {
							dBindActionDown.Add(kvp.Key, liBindActionDown.ToArray());
						}
						if(liBindCommandDown.Count > 0) {
							dBindCommandDown.Add(kvp.Key, liBindCommandDown.ToArray());
						}
					}
				};

				WinApi.Input[] piPositionRelative = new WinApi.Input[] {
					new WinApi.Input {
						type = WinApi.InputType.INPUT_MOUSE,
						u = new WinApi.InputDummyUnionName {
							mi = new WinApi.MouseInput {
								dwFlags = WinApi.MouseInputFlags.MOUSEEVENTF_MOVE,
								dwExtraInfo = System.IntPtr.Zero
							}
						}
					}
				};
				WinApi.Input[] piPositionAbsolute = new WinApi.Input[] {
					new WinApi.Input {
						type = WinApi.InputType.INPUT_MOUSE,
						u = new WinApi.InputDummyUnionName {
							mi = new WinApi.MouseInput {
								dwFlags = WinApi.MouseInputFlags.MOUSEEVENTF_ABSOLUTE | WinApi.MouseInputFlags.MOUSEEVENTF_VIRTUALDESK | WinApi.MouseInputFlags.MOUSEEVENTF_MOVE,
								dwExtraInfo = System.IntPtr.Zero
							}
						}
					}
				};
				WinApi.Input[] piWheel = new WinApi.Input[] {
					new WinApi.Input {
						type = WinApi.InputType.INPUT_MOUSE,
						u = new WinApi.InputDummyUnionName {
							mi = new WinApi.MouseInput {
								dwFlags = WinApi.MouseInputFlags.MOUSEEVENTF_WHEEL,
								dwExtraInfo = System.IntPtr.Zero
							}
						}
					}
				};
				WinApi.Input[] piUnicode = new WinApi.Input[] {
					new WinApi.Input {
						type = WinApi.InputType.INPUT_KEYBOARD,
						u = new WinApi.InputDummyUnionName {
							ki = new WinApi.KeybdInput {
								dwFlags = WinApi.KeybdInputFlags.KEYEVENTF_UNICODE | WinApi.KeybdInputFlags.KEYEVENTF_KEYDOWN,
								dwExtraInfo = System.IntPtr.Zero
							}
						}
					}, new WinApi.Input {
						type = WinApi.InputType.INPUT_KEYBOARD,
						u = new WinApi.InputDummyUnionName {
							ki = new WinApi.KeybdInput {
								dwFlags = WinApi.KeybdInputFlags.KEYEVENTF_UNICODE | WinApi.KeybdInputFlags.KEYEVENTF_KEYUP,
								dwExtraInfo = System.IntPtr.Zero
							}
						}
					}
				};

				MagicRemoteService.Screen scrDisplay = MagicRemoteService.Screen.PrimaryScreen;
				System.Threading.Tasks.Task.Run(delegate () {
					try {
						System.Net.IPAddress iaClient = ((System.Net.IPEndPoint)socClient.RemoteEndPoint).Address;
						MagicRemoteService.WebOSCLIDevice wocdClient = System.Array.Find<MagicRemoteService.WebOSCLIDevice>(MagicRemoteService.WebOSCLI.SetupDeviceList(), delegate (MagicRemoteService.WebOSCLIDevice wocd) {
							return wocd.DeviceInfo.IP.Equals(iaClient);
						});
						if(wocdClient == null) {
							Service.Log("No TV configured with IP " + iaClient + ", using primary display for socket " + strClient);
						} else {
							Microsoft.Win32.RegistryKey rkMagicRemoteServiceDevice = (MagicRemoteService.Program.bElevated ? Microsoft.Win32.Registry.LocalMachine : Microsoft.Win32.Registry.CurrentUser).OpenSubKey(@"Software\MagicRemoteService\Device\" + wocdClient.Name);
							if(rkMagicRemoteServiceDevice != null && MagicRemoteService.Screen.AllScreen.TryGetValue((uint)(int)rkMagicRemoteServiceDevice.GetValue("Display", 0), out MagicRemoteService.Screen scr) && scr.Active) {
								scrDisplay = scr;
							}
						}
					} catch(System.Exception eException) {
						Service.Warn("Display lookup failed, using primary display for socket " + strClient + ": " + eException.Message);
					}
				});

				System.Threading.WaitHandle[] tabEvent = new System.Threading.WaitHandle[] {
					Service.mreStop,
					mreClientStop,
					areClientReceiveAsyncCompleted
				};
				bool bHandshake = false;
				while(!bHandshake && !mreClientStop.WaitOne(System.TimeSpan.Zero)) {
					switch(System.Threading.WaitHandle.WaitAny(tabEvent, -1, true)) {
						case 0:
							ClientStop("service stopping");
							break;
						case 1:
							break;
						case 2:
							if(eaClientReceiveAsync.SocketError != System.Net.Sockets.SocketError.Success) {
								ClientStop("receive error " + eaClientReceiveAsync.SocketError + " before handshake");
								break;
							}
							if(eaClientReceiveAsync.BytesTransferred == 0) {
								ClientStop("connection closed by TV before handshake");
								break;
							}
							iBuffered += eaClientReceiveAsync.BytesTransferred;
							string strRequest = System.Text.Encoding.ASCII.GetString(tabData, 0, iBuffered);
							if(iBuffered >= 4 && !strRequest.StartsWith("GET ")) {
								ClientStop("not a WebSocket handshake");
								Service.Warn("Connexion refused on socket " + strClient + ", not a WebSocket handshake");
								break;
							}
							int iEndRequest = strRequest.IndexOf("\r\n\r\n");
							if(iEndRequest < 0) {
								// The request can arrive in several TCP segments
								if(iBuffered == tabData.Length) {
									ClientStop("handshake too large");
									break;
								}
								eaClientReceiveAsync.SetBuffer(iBuffered, tabData.Length - iBuffered);
								if(!socClient.ReceiveAsync(eaClientReceiveAsync)) {
									ClientReceiveAsyncCompleted(socClient, eaClientReceiveAsync);
								}
								break;
							}
							strRequest = strRequest.Substring(0, iEndRequest);
							Service.LogIfDebug("WebSocket handshake on socket " + strClient + ":\r\n" + strRequest);
							System.Text.RegularExpressions.Match mKey = System.Text.RegularExpressions.Regex.Match(strRequest, @"^Sec-WebSocket-Key:[ \t]*(\S+)[ \t]*\r?$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
							if(!mKey.Success) {
								ClientStop("handshake without Sec-WebSocket-Key");
								Service.Warn("Connexion refused on socket " + strClient + ", handshake without Sec-WebSocket-Key");
								break;
							}
							using(System.Security.Cryptography.SHA1 sha1 = System.Security.Cryptography.SHA1.Create()) {
								socClient.Send(System.Text.Encoding.UTF8.GetBytes(
									"HTTP/1.1 101 Switching Protocols\r\n" +
									"Connection: Upgrade\r\n" +
									"Upgrade: websocket\r\n" +
									"Sec-WebSocket-Accept: " + System.Convert.ToBase64String(sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(mKey.Groups[1].Value + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"))) + "\r\n\r\n"));
							}
							bHandshake = true;

							//TODO Something to ask TV if cursor visible
							Service.Log("Client connected on socket " + strClient);
							ciConnection = new MagicRemoteService.ConnectionInfo {
								Client = strClient,
								ConnectedAt = System.DateTime.Now,
								LastMessageAt = System.DateTime.Now
							};
							lock(Service.liConnection) {
								Service.liConnection.Add(ciConnection);
							}
							Service.AddConnectionHistory("Connected " + strClient);
							tPing.Start();
							if(this.bInactivity) {
								tInactivity.Start();
							}

							// A client must wait for the handshake response before sending frames, so anything after the request is kept for the frame parser
							iBuffered -= iEndRequest + 4;
							System.Buffer.BlockCopy(tabData, iEndRequest + 4, tabData, 0, iBuffered);
							eaClientReceiveAsync.SetBuffer(iBuffered, tabData.Length - iBuffered);
							if(!socClient.ReceiveAsync(eaClientReceiveAsync)) {
								ClientReceiveAsyncCompleted(socClient, eaClientReceiveAsync);
							}
							break;
						default:
							throw new System.Exception("Unmanaged handle error");
					}
				}
				while(!mreClientStop.WaitOne(System.TimeSpan.Zero)) {
					switch(System.Threading.WaitHandle.WaitAny(tabEvent, -1, true)) {
						case 0:
							ClientStop("service stopping");
							Service.TrySend(socClient, Service.tabClose, strClient);
							break;
						case 1:
							break;
						case 2:
							if(eaClientReceiveAsync.SocketError != System.Net.Sockets.SocketError.Success) {
								ClientStop("receive error " + eaClientReceiveAsync.SocketError);
								break;
							}
							if(eaClientReceiveAsync.BytesTransferred == 0) {
								// Graceful TCP close without a WebSocket close frame (TV switched off, app killed, network change)
								ClientStop("connection closed by TV without close frame");
								break;
							}
							iBuffered += eaClientReceiveAsync.BytesTransferred;
							ulong ulLenMessage = (ulong)iBuffered;
							ulong ulOffsetFrame = 0;
							// Only complete frames are processed; a frame split across TCP reads stays in the buffer until the rest arrives
							while(ulLenMessage - ulOffsetFrame >= 2 && !mreClientStop.WaitOne(System.TimeSpan.Zero)) {
								ulong ulLenHeader = ((tabData[ulOffsetFrame + 1] & 0b01111111) == 0b01111111 ? 10UL : (tabData[ulOffsetFrame + 1] & 0b01111111) == 0b01111110 ? 4UL : 2UL) + ((tabData[ulOffsetFrame + 1] & 0b10000000) == 0b10000000 ? 4UL : 0UL);
								if(ulLenMessage - ulOffsetFrame < ulLenHeader) {
									break;
								}
								bool bFin = (tabData[ulOffsetFrame] & 0b10000000) == 0b10000000;
								bool bRsv1 = (tabData[ulOffsetFrame] & 0b01000000) == 0b01000000;
								bool bRsv2 = (tabData[ulOffsetFrame] & 0b00100000) == 0b00100000;
								bool bRsv3 = (tabData[ulOffsetFrame] & 0b00010000) == 0b00010000;
								byte ucOpcode = (byte)(tabData[ulOffsetFrame] & 0b00001111);

								bool bMask = (tabData[ulOffsetFrame + 1] & 0b10000000) == 0b10000000;
								ulong ulLenData;
								ulong ulOffsetMask;
								if((tabData[ulOffsetFrame + 1] & 0b01111111) == 0b01111111) {
									ulLenData = System.BitConverter.ToUInt64(new byte[] { tabData[ulOffsetFrame + 9], tabData[ulOffsetFrame + 8], tabData[ulOffsetFrame + 7], tabData[ulOffsetFrame + 6], tabData[ulOffsetFrame + 5], tabData[ulOffsetFrame + 4], tabData[ulOffsetFrame + 3], tabData[ulOffsetFrame + 2] }, 0);
									ulOffsetMask = ulOffsetFrame + 10;
								} else if((tabData[ulOffsetFrame + 1] & 0b01111111) == 0b01111110) {
									ulLenData = System.BitConverter.ToUInt16(new byte[] { tabData[ulOffsetFrame + 3], tabData[ulOffsetFrame + 2] }, 0);
									ulOffsetMask = ulOffsetFrame + 4;
								} else {
									ulLenData = (byte)(tabData[ulOffsetFrame + 1] & 0b01111111);
									ulOffsetMask = ulOffsetFrame + 2;
								}

								if(ulLenData > (ulong)tabData.Length - ulLenHeader) {
									ClientStop("frame too large (" + ulLenData + " bytes)");
									break;
								}
								if(ulLenMessage - ulOffsetFrame < ulLenHeader + ulLenData) {
									break;
								}
								ciConnection.LastMessageAt = System.DateTime.Now;
								System.Threading.Interlocked.Increment(ref ciConnection.MessageCount);
								ulong ulOffsetData;
								if(bMask) {
									ulOffsetData = ulOffsetMask + 4;
									for(ulong ul = 0; ul < ulLenData; ul++) {
										tabData[ulOffsetData + ul] ^= tabData[ulOffsetMask + (ul % 4)];
									}
								} else {
									ulOffsetData = ulOffsetMask;
								}
								if(!bFin) {
									Service.Warn("Unable to process split frame on socket " + strClient);
								} else {
									switch(ucOpcode) {
										case (byte)MagicRemoteService.WebSocketOpCode.Continuation:
											Service.Warn("Unable to process split frame on socket " + strClient);
											break;
										case (byte)MagicRemoteService.WebSocketOpCode.Text:
											if(ulLenData != 0) {
												if(Service.ProcessTextMessage(System.Text.Encoding.UTF8.GetString(tabData, (int)ulOffsetData, (int)ulLenData), strClient) == "hello") {
													bTextCapable = true;
													ciConnection.LogForwarding = true;
													SendLogLevel();
												}
											}
											break;
										case (byte)MagicRemoteService.WebSocketOpCode.Binary:
											tPing.Stop();
											tPing.Start();
											if(this.bInactivity) {
												tInactivity.Stop();
												tInactivity.Start();
											}
											if(ulLenData < (ulong)Service.MessageLength(tabData[ulOffsetData + 0])) {
												Service.Warn("Truncated binary message [0x" + System.BitConverter.ToString(tabData, (int)ulOffsetData, (int)ulLenData).Replace("-", string.Empty) + "] on socket " + strClient);
											} else {
												switch(tabData[ulOffsetData + 0]) {
													case (byte)MagicRemoteService.MessageType.PositionRelative:
														piPositionRelative[0].u.mi.dx = System.BitConverter.ToInt16(tabData, (int)ulOffsetData + 1);
														piPositionRelative[0].u.mi.dy = System.BitConverter.ToInt16(tabData, (int)ulOffsetData + 3);
														Service.SendInputAdmin(piPositionRelative);
														break;
													case (byte)MagicRemoteService.MessageType.PositionAbsolute:
														piPositionAbsolute[0].u.mi.dx = ((scrDisplay.Bounds.X + ((scrDisplay.Bounds.Width * System.BitConverter.ToUInt16(tabData, (int)ulOffsetData + 1)) / 1920)) * 65535) / MagicRemoteService.Screen.DesktopBounds.Width;
														piPositionAbsolute[0].u.mi.dy = ((scrDisplay.Bounds.Y + ((scrDisplay.Bounds.Height * System.BitConverter.ToUInt16(tabData, (int)ulOffsetData + 3)) / 1080)) * 65535) / MagicRemoteService.Screen.DesktopBounds.Height;
														Service.SendInputAdmin(piPositionAbsolute);
														break;
													case (byte)MagicRemoteService.MessageType.Wheel:
														piWheel[0].u.mi.mouseData = (uint)(-System.BitConverter.ToInt16(tabData, (int)ulOffsetData + 1) * 3);
														Service.SendInputAdmin(piWheel);
														Service.LogIfDebug("Processed binary message send/wheel [0x" + System.BitConverter.ToString(tabData, (int)ulOffsetData, (int)ulLenData).Replace("-", string.Empty) + "], sY: " + (-System.BitConverter.ToInt16(tabData, (int)ulOffsetData + 1)).ToString());
														break;

													case (byte)MagicRemoteService.MessageType.Visible:
														if(System.BitConverter.ToBoolean(tabData, (int)ulOffsetData + 1)) {
															MagicRemoteService.SystemCursor.SetMagicRemoteServiceSystemCursor();
															MagicRemoteService.SystemCursor.SetMagicRemoteServiceMouseSpeedAccel();
														} else {
															MagicRemoteService.SystemCursor.SetDefaultSystemCursor();
															MagicRemoteService.SystemCursor.SetDefaultMouseSpeedAccel();
														}
														Service.LogIfDebug("Processed binary message send/visible [0x" + System.BitConverter.ToString(tabData, (int)ulOffsetData, (int)ulLenData).Replace("-", string.Empty) + "], bV: " + System.BitConverter.ToBoolean(tabData, (int)ulOffsetData + 1).ToString());
														break;
													case (byte)MagicRemoteService.MessageType.Key:
														ushort usCode = System.BitConverter.ToUInt16(tabData, (int)ulOffsetData + 1);
														if((tabData[ulOffsetData + 3] & 0x01) == 0x01) {
															if(dBindDown.TryGetValue(usCode, out WinApi.Input[] arrInput)) {
																Service.SendInputAdmin(arrInput);
																Service.LogIfDebug("Processed binary message send/key [0x" + System.BitConverter.ToString(tabData, (int)ulOffsetData, (int)ulLenData).Replace("-", string.Empty) + "], usC: " + System.BitConverter.ToUInt16(tabData, (int)ulOffsetData + 1).ToString() + ", bS: " + System.BitConverter.ToBoolean(tabData, (int)ulOffsetData + 3).ToString());
															} else if(dBindActionDown.TryGetValue(usCode, out byte[][] arr2Byte)) {
																foreach(byte[] arrByte in arr2Byte) {
																	socClient.Send(arrByte);
																}
																Service.LogIfDebug("Processed binary message send/key action [0x" + System.BitConverter.ToString(tabData, (int)ulOffsetData, (int)ulLenData).Replace("-", string.Empty) + "], usC: " + System.BitConverter.ToUInt16(tabData, (int)ulOffsetData + 1).ToString() + ", bS: " + System.BitConverter.ToBoolean(tabData, (int)ulOffsetData + 3).ToString());
															} else if(dBindCommandDown.TryGetValue(usCode, out string[] arrString)) {
																foreach(string strCommand in arrString) {
																	System.Diagnostics.Process pCommand = new System.Diagnostics.Process();
																	pCommand.StartInfo.FileName = "cmd";
																	pCommand.StartInfo.Arguments = "/c " + strCommand;
																	pCommand.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
																	pCommand.Start();
																}
																Service.LogIfDebug("Processed binary message send/key command [0x" + System.BitConverter.ToString(tabData, (int)ulOffsetData, (int)ulLenData).Replace("-", string.Empty) + "], usC: " + System.BitConverter.ToUInt16(tabData, (int)ulOffsetData + 1).ToString() + ", bS: " + System.BitConverter.ToBoolean(tabData, (int)ulOffsetData + 3).ToString());
															} else {
																Service.LogIfDebug("Unprocessed binary message send/key [0x" + System.BitConverter.ToString(tabData, (int)ulOffsetData, (int)ulLenData).Replace("-", string.Empty) + "], usC: " + System.BitConverter.ToUInt16(tabData, (int)ulOffsetData + 1).ToString() + ", bS: " + System.BitConverter.ToBoolean(tabData, (int)ulOffsetData + 3).ToString());
															}
														} else {
															if(dBindUp.TryGetValue(usCode, out WinApi.Input[] arrInput)) {
																Service.SendInputAdmin(arrInput);
																Service.LogIfDebug("Processed binary message send/key [0x" + System.BitConverter.ToString(tabData, (int)ulOffsetData, (int)ulLenData).Replace("-", string.Empty) + "], usC: " + System.BitConverter.ToUInt16(tabData, (int)ulOffsetData + 1).ToString() + ", bS: " + System.BitConverter.ToBoolean(tabData, (int)ulOffsetData + 3).ToString());
															} else {
																Service.LogIfDebug("Unprocessed binary message send/key [0x" + System.BitConverter.ToString(tabData, (int)ulOffsetData, (int)ulLenData).Replace("-", string.Empty) + "], usC: " + System.BitConverter.ToUInt16(tabData, (int)ulOffsetData + 1).ToString() + ", bS: " + System.BitConverter.ToBoolean(tabData, (int)ulOffsetData + 3).ToString());
															}
														}
														break;
													case (byte)MagicRemoteService.MessageType.Unicode:
														ushort usScan = System.BitConverter.ToUInt16(tabData, (int)ulOffsetData + 1);
														piUnicode[0].u.ki.wScan = usScan;
														piUnicode[1].u.ki.wScan = usScan;
														Service.SendInputAdmin(piUnicode);
														Service.LogIfDebug("Processed binary message send/unicode [0x" + System.BitConverter.ToString(tabData, (int)ulOffsetData, (int)ulLenData).Replace("-", string.Empty) + "], usC: " + System.Text.Encoding.UTF8.GetString(tabData, (int)ulOffsetData + 1, 2));
														break;
													case (byte)MagicRemoteService.MessageType.Shutdown:
														System.Diagnostics.Process pProcess = new System.Diagnostics.Process();
														pProcess.StartInfo.FileName = "shutdown";
														pProcess.StartInfo.Arguments = "/s /t 0";
														pProcess.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
														pProcess.Start();
														Service.LogIfDebug("Processed binary message send/shutdown [0x" + System.BitConverter.ToString(tabData, (int)ulOffsetData, (int)ulLenData).Replace("-", string.Empty) + "]");
														break;
													default:
														Service.Warn("Uprocessed binary message [0x" + System.BitConverter.ToString(tabData, (int)ulOffsetData, (int)ulLenData).Replace("-", string.Empty) + "]");
														break;
												}
											}
											break;
										case (byte)MagicRemoteService.WebSocketOpCode.ConnectionClose:
											if(bMask) {
												tabData[ulOffsetFrame + 1] = (byte)(tabData[ulOffsetFrame + 1] & 0b01111111);
												for(ulong ul = 0; ul < ulLenData; ul++) {
													tabData[ulOffsetMask + ul] = tabData[ulOffsetData + ul];
												}
											}
											socClient.Send(tabData, (int)ulOffsetFrame, (int)(ulOffsetMask - ulOffsetFrame + ulLenData), System.Net.Sockets.SocketFlags.None);
											ClientStop("close frame received from TV");
											Service.Log("Client disconnected on socket " + strClient);
											break;
										case (byte)MagicRemoteService.WebSocketOpCode.Ping:
											tabData[ulOffsetFrame] = (byte)((tabData[ulOffsetFrame] & 0xF0) | (0x0A & 0x0F));
											if(bMask) {
												tabData[ulOffsetFrame + 1] = (byte)(tabData[ulOffsetFrame + 1] & 0b01111111);
												for(ulong ul = 0; ul < ulLenData; ul++) {
													tabData[ulOffsetMask + ul] = tabData[ulOffsetData + ul];
												}
											}
											socClient.Send(tabData, (int)ulOffsetFrame, (int)(ulOffsetMask - ulOffsetFrame + ulLenData), System.Net.Sockets.SocketFlags.None);
											Service.LogIfDebug("Ping received on socket " + strClient);
											break;
										case (byte)MagicRemoteService.WebSocketOpCode.Pong:
											if(ulLenData != 0) {
												switch(tabData[ulOffsetData + 0]) {
													case 0x01:
														tPongUserInput.Stop();
														System.Diagnostics.Process pProcess = new System.Diagnostics.Process();
														pProcess.StartInfo.FileName = "shutdown";
														pProcess.StartInfo.Arguments = "/s /t 300";
														pProcess.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
														pProcess.Start();
														tUserInput.Start();
														Service.LogIfDebug("Pong incativity received on socket " + strClient);
														break;
													default:
														Service.Warn("Unprocessed pong message [0x" + System.BitConverter.ToString(tabData, (int)ulOffsetData, (int)ulLenData).Replace("-", string.Empty) + "]");
														break;
												}
											} else {
												tPong.Stop();
												Service.LogIfDebug("Pong received on socket " + strClient);
											}
											break;
										default:
											Service.Warn("Unprocessed message [0x" + System.BitConverter.ToString(tabData, (int)ulOffsetData, (int)ulLenData).Replace("-", string.Empty) + "], " + System.Text.Encoding.Default.GetString(tabData, (int)ulOffsetData, (int)ulLenData));
											break;
									}
								}
								ulOffsetFrame = ulOffsetData + ulLenData;
							}
							if(mreClientStop.WaitOne(System.TimeSpan.Zero)) {
								break;
							}

							iBuffered = (int)(ulLenMessage - ulOffsetFrame);
							if(iBuffered > 0 && ulOffsetFrame > 0) {
								System.Buffer.BlockCopy(tabData, (int)ulOffsetFrame, tabData, 0, iBuffered);
							}
							eaClientReceiveAsync.SetBuffer(iBuffered, tabData.Length - iBuffered);
							if(!socClient.ReceiveAsync(eaClientReceiveAsync)) {
								ClientReceiveAsyncCompleted(socClient, eaClientReceiveAsync);
							}
							break;
						default:
							throw new System.Exception("Unmanaged handle error");
					}
				}
			} catch(System.Exception eException) {
				strStopReason = "error: " + eException.Message;
				Service.Error("Client thread failure on socket " + strClient + ": " + eException.ToString());
			} finally {
				MagicRemoteService.Application.naehPowerSettingNotificationArrived -= PowerSettingNotificationArrived;
				foreach(System.Timers.Timer t in new System.Timers.Timer[] { tPing, tPong, tInactivity, tVideoInput, tPongUserInput }) {
					if(t != null) {
						t.Stop();
						t.Dispose();
					}
				}
				// A pending "shutdown /t 300" must still be aborted by local user input after the TV has gone, so leave that watcher running until it fires
				bClientClosed = true;
				if(tUserInput != null && !tUserInput.Enabled) {
					tUserInput.Dispose();
				}
				try {
					MagicRemoteService.SystemCursor.SetDefaultSystemCursor();
					MagicRemoteService.SystemCursor.SetDefaultMouseSpeedAccel();
				} catch(System.Exception eException) {
					Service.Warn("Unable to restore system cursor: " + eException.Message);
				}
				eaClientReceiveAsync.Completed -= ClientReceiveAsyncCompleted;
				eaClientReceiveAsync.Dispose();
				socClient.Close();
				socClient.Dispose();
				areClientReceiveAsyncCompleted.Close();
				mreClientStop.Close();
				Service.Log("Socket closed " + strClient + " (" + strStopReason + ")");
				if(ciConnection == null) {
					Service.AddConnectionHistory("Closed before handshake " + strClient + " (" + strStopReason + ")");
				} else {
					lock(Service.liConnection) {
						Service.liConnection.Remove(ciConnection);
					}
					Service.AddConnectionHistory("Disconnected " + strClient + " after " + Service.FormatDuration(System.DateTime.Now - ciConnection.ConnectedAt) + ", " + ciConnection.MessageCount + " messages (" + strStopReason + ")");
				}
			}
		}
	}
	public static class PipeExtension {
		public static System.Diagnostics.Process GetServerProcess(this System.IO.Pipes.NamedPipeClientStream psClient) {
			WinApi.Kernel32.GetNamedPipeServerProcessId(psClient.SafePipeHandle.DangerousGetHandle(), out uint uiProcessId);
			return System.Diagnostics.Process.GetProcessById((int)uiProcessId);
		}
		public static System.Diagnostics.Process GetClientProcess(this System.IO.Pipes.NamedPipeServerStream psServer) {
			WinApi.Kernel32.GetNamedPipeClientProcessId(psServer.SafePipeHandle.DangerousGetHandle(), out uint uiProcessId);
			return System.Diagnostics.Process.GetProcessById((int)uiProcessId);
		}
	}
}
