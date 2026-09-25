
namespace MagicRemoteService {
	// Settings tab showing what the PC side is doing: mode, listening port, firewall rule, connected TVs, recent connections and the log
	public class DiagnosticsPage : System.Windows.Forms.TabPage {
		private readonly MagicRemoteService.Service mrsService;
		private readonly System.Windows.Forms.TextBox tbStatus;
		private readonly System.Windows.Forms.TextBox tbLog;
		private readonly System.Windows.Forms.ComboBox cbbLogLevel;
		private readonly System.Windows.Forms.TextBox tbLogPath;
		private readonly System.Windows.Forms.Timer tRefresh;
		private bool bRefreshing;
		private string strLastLog;

		public DiagnosticsPage(MagicRemoteService.Service mrs) {
			this.mrsService = mrs;
			this.Text = "Diagnostics";
			this.Padding = new System.Windows.Forms.Padding(6);
			this.UseVisualStyleBackColor = true;

			System.Drawing.Font fMono = new System.Drawing.Font(System.Drawing.FontFamily.GenericMonospace, 8.25F);

			this.tbStatus = new System.Windows.Forms.TextBox {
				Dock = System.Windows.Forms.DockStyle.Fill,
				Multiline = true,
				ReadOnly = true,
				WordWrap = false,
				ScrollBars = System.Windows.Forms.ScrollBars.Both,
				Font = fMono
			};
			this.tbLog = new System.Windows.Forms.TextBox {
				Dock = System.Windows.Forms.DockStyle.Fill,
				Multiline = true,
				ReadOnly = true,
				WordWrap = false,
				ScrollBars = System.Windows.Forms.ScrollBars.Both,
				Font = fMono
			};

			this.cbbLogLevel = new System.Windows.Forms.ComboBox {
				DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList,
				Width = 110,
				Anchor = System.Windows.Forms.AnchorStyles.Left
			};
			this.cbbLogLevel.Items.AddRange(new object[] { MagicRemoteService.LogLevel.Error, MagicRemoteService.LogLevel.Warning, MagicRemoteService.LogLevel.Information, MagicRemoteService.LogLevel.Debug });
			this.cbbLogLevel.SelectedItem = MagicRemoteService.Logger.Level;
			this.cbbLogLevel.SelectedIndexChanged += this.LogLevel_SelectedIndexChanged;

			this.tbLogPath = new System.Windows.Forms.TextBox {
				ReadOnly = true,
				Text = MagicRemoteService.Logger.strFilePath,
				Dock = System.Windows.Forms.DockStyle.Fill,
				Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right
			};
			System.Windows.Forms.Button btnCopy = new System.Windows.Forms.Button {
				Text = "Copy report",
				AutoSize = true,
				Anchor = System.Windows.Forms.AnchorStyles.Right
			};
			btnCopy.Click += this.Copy_Click;

			System.Windows.Forms.TableLayoutPanel tlpLog = new System.Windows.Forms.TableLayoutPanel {
				Dock = System.Windows.Forms.DockStyle.Fill,
				AutoSize = true,
				ColumnCount = 4,
				RowCount = 1
			};
			tlpLog.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
			tlpLog.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
			tlpLog.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
			tlpLog.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
			tlpLog.Controls.Add(new System.Windows.Forms.Label {
				Text = "Log level",
				AutoSize = true,
				Anchor = System.Windows.Forms.AnchorStyles.Left
			}, 0, 0);
			tlpLog.Controls.Add(this.cbbLogLevel, 1, 0);
			tlpLog.Controls.Add(this.tbLogPath, 2, 0);
			tlpLog.Controls.Add(btnCopy, 3, 0);

			System.Windows.Forms.TableLayoutPanel tlpMain = new System.Windows.Forms.TableLayoutPanel {
				Dock = System.Windows.Forms.DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 3
			};
			tlpMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
			tlpMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
			tlpMain.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
			tlpMain.Controls.Add(this.tbStatus, 0, 0);
			tlpMain.Controls.Add(tlpLog, 0, 1);
			tlpMain.Controls.Add(this.tbLog, 0, 2);
			this.Controls.Add(tlpMain);

			this.tRefresh = new System.Windows.Forms.Timer {
				Interval = 2000
			};
			this.tRefresh.Tick += this.Refresh_Tick;
			this.tRefresh.Start();
		}
		protected override void Dispose(bool disposing) {
			if(disposing) {
				this.tRefresh.Stop();
				this.tRefresh.Dispose();
			}
			base.Dispose(disposing);
		}
		private bool IsShown {
			get {
				return this.Parent is System.Windows.Forms.TabControl tc && tc.SelectedTab == this && this.Visible;
			}
		}
		private void Refresh_Tick(object sender, System.EventArgs e) {
			if(this.IsShown) {
				this.RefreshNow();
			}
		}
		protected override void OnVisibleChanged(System.EventArgs e) {
			base.OnVisibleChanged(e);
			if(this.Visible) {
				this.RefreshNow();
			}
		}
		private async void RefreshNow() {
			this.RefreshLog();
			if(this.bRefreshing) {
				return;
			}
			this.bRefreshing = true;
			try {
				// Port test, service and firewall queries can take a moment, keep them off the UI thread
				string strStatus = await System.Threading.Tasks.Task.Run<string>(delegate () {
					return this.BuildStatus();
				});
				if(!this.IsDisposed) {
					this.tbStatus.Text = strStatus;
				}
			} catch(System.Exception eException) {
				if(!this.IsDisposed) {
					this.tbStatus.Text = "Unable to collect diagnostics: " + eException.Message;
				}
			} finally {
				this.bRefreshing = false;
			}
		}
		private void RefreshLog() {
			string[] arrLog = MagicRemoteService.Logger.GetRecent();
			string strLog = string.Join(System.Environment.NewLine, arrLog, System.Math.Max(0, arrLog.Length - 200), System.Math.Min(200, arrLog.Length));
			if(strLog != this.strLastLog) {
				this.strLastLog = strLog;
				this.tbLog.Text = strLog;
				this.tbLog.SelectionStart = this.tbLog.TextLength;
				this.tbLog.ScrollToCaret();
			}
		}
		private static Microsoft.Win32.RegistryKey RootKey {
			get {
				return MagicRemoteService.Program.bElevated ? Microsoft.Win32.Registry.LocalMachine : Microsoft.Win32.Registry.CurrentUser;
			}
		}
		private string BuildStatus() {
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			sb.AppendLine("MagicRemoteService v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version + " on " + System.Environment.OSVersion + (System.Environment.Is64BitProcess ? " (64-bit process)" : " (32-bit process)"));
			sb.AppendLine("Running as " + System.Security.Principal.WindowsIdentity.GetCurrent().Name + (MagicRemoteService.Program.bElevated ? ", elevated, settings in HKLM" : ", not elevated, settings in HKCU"));

			int iPort = 41230;
			using(Microsoft.Win32.RegistryKey rkMagicRemoteService = DiagnosticsPage.RootKey.OpenSubKey(@"Software\MagicRemoteService")) {
				if(rkMagicRemoteService != null) {
					iPort = (int)rkMagicRemoteService.GetValue("Port", 41230);
				}
			}

			switch(this.mrsService.Type) {
				case MagicRemoteService.ServiceType.Client:
					sb.AppendLine("Mode: connected to the MagicRemoteService Windows service, which listens on port " + iPort);
					break;
				case MagicRemoteService.ServiceType.Both:
					sb.AppendLine("Mode: standalone (Windows service not running), this app listens on port " + iPort);
					break;
				default:
					sb.AppendLine("Mode: " + this.mrsService.Type);
					break;
			}
			sb.AppendLine("Windows service: " + DiagnosticsPage.ServiceStatus());
			sb.AppendLine("Port " + iPort + " on this PC: " + DiagnosticsPage.TestPort(iPort));
			sb.AppendLine("Firewall rule: " + DiagnosticsPage.FirewallStatus());
			sb.AppendLine("PC addresses for the TV: " + DiagnosticsPage.LocalAddresses());

			MagicRemoteService.ConnectionInfo[] arrConnection = MagicRemoteService.Service.GetConnections();
			sb.AppendLine();
			sb.AppendLine("Connected TVs: " + (arrConnection.Length == 0 ? "none" : arrConnection.Length.ToString()));
			foreach(MagicRemoteService.ConnectionInfo ci in arrConnection) {
				sb.AppendLine("  " + ci.Client + " for " + MagicRemoteService.Service.FormatDuration(System.DateTime.Now - ci.ConnectedAt) + ", " + ci.MessageCount + " messages, last " + (int)(System.DateTime.Now - ci.LastMessageAt).TotalSeconds + "s ago" + (ci.LogForwarding ? "" : ", TV app does not forward its log (reinstall the TV app to enable)"));
			}
			string[] arrHistory = MagicRemoteService.Service.GetConnectionHistory();
			sb.AppendLine();
			sb.AppendLine("Recent connections (this process):");
			if(arrHistory.Length == 0) {
				sb.AppendLine("  none");
			}
			for(int i = arrHistory.Length - 1; i >= 0; i--) {
				sb.AppendLine("  " + arrHistory[i]);
			}
			return sb.ToString();
		}
		private static string ServiceStatus() {
			try {
				foreach(System.ServiceProcess.ServiceController sc in System.ServiceProcess.ServiceController.GetServices()) {
					using(sc) {
						if(sc.ServiceName == "MagicRemoteService") {
							return sc.Status + ", start " + sc.StartType;
						}
					}
				}
				return "not installed";
			} catch(System.Exception eException) {
				return "unknown (" + eException.Message + ")";
			}
		}
		private static string TestPort(int iPort) {
			// Connecting to test the port would itself be logged as a TV connection, so only check that something listens on it
			try {
				foreach(System.Net.IPEndPoint ipe in System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()) {
					if(ipe.Port == iPort && ipe.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) {
						return "listening on " + ipe.Address;
					}
				}
				return "NOT listening, the TV cannot connect";
			} catch(System.Exception eException) {
				return "unknown (" + eException.Message + ")";
			}
		}
		private static string FirewallStatus() {
			try {
				dynamic nfp = System.Activator.CreateInstance(System.Type.GetTypeFromProgID("HNetCfg.FwPolicy2"));
				dynamic nfr;
				try {
					nfr = nfp.Rules.Item("MagicRemoteService");
				} catch(System.Exception) {
					return "missing, the TV may be blocked (saving the PC tab as administrator creates it)";
				}
				string strApplication = (string)nfr.ApplicationName;
				string strStatus = (bool)nfr.Enabled ? "present and enabled" : "present but DISABLED";
				if(!string.Equals(strApplication, System.Reflection.Assembly.GetExecutingAssembly().Location, System.StringComparison.OrdinalIgnoreCase)) {
					strStatus += ", but it allows " + strApplication + " instead of this program (save the PC tab as administrator to update it)";
				}
				return strStatus;
			} catch(System.Exception eException) {
				return "unknown (" + eException.Message + ")";
			}
		}
		private static string LocalAddresses() {
			System.Collections.Generic.List<string> liAddress = new System.Collections.Generic.List<string>();
			try {
				foreach(System.Net.NetworkInformation.NetworkInterface ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()) {
					if(ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up || ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) {
						continue;
					}
					foreach(System.Net.NetworkInformation.UnicastIPAddressInformation uipai in ni.GetIPProperties().UnicastAddresses) {
						if(uipai.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) {
							liAddress.Add(uipai.Address + " (" + ni.Name + ")");
						}
					}
				}
			} catch(System.Exception eException) {
				return "unknown (" + eException.Message + ")";
			}
			return liAddress.Count == 0 ? "none" : string.Join(", ", liAddress);
		}
		private void LogLevel_SelectedIndexChanged(object sender, System.EventArgs e) {
			if(this.cbbLogLevel.SelectedItem is MagicRemoteService.LogLevel ll && ll != MagicRemoteService.Logger.Level) {
				try {
					MagicRemoteService.Logger.Level = ll;
					MagicRemoteService.Logger.Write(MagicRemoteService.LogLevel.Information, "Log level set to " + ll);
				} catch(System.Exception eException) {
					System.Windows.Forms.MessageBox.Show(eException.Message, "Log level", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
					this.cbbLogLevel.SelectedItem = MagicRemoteService.Logger.Level;
				}
			}
		}
		private void Copy_Click(object sender, System.EventArgs e) {
			try {
				System.Windows.Forms.Clipboard.SetText(this.tbStatus.Text + System.Environment.NewLine + "Log file: " + MagicRemoteService.Logger.strFilePath + System.Environment.NewLine + System.Environment.NewLine + string.Join(System.Environment.NewLine, MagicRemoteService.Logger.GetRecent()));
			} catch(System.Exception eException) {
				System.Windows.Forms.MessageBox.Show(eException.Message, "Copy report", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
			}
		}
	}
}
