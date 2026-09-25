
namespace MagicRemoteService {

	[System.Serializable]
	public class WebOSCLIException : System.Exception {
		public WebOSCLIException() {
		}
		public WebOSCLIException(string strMessage) : base(strMessage) {
		}
		public WebOSCLIException(string strMessage, System.Exception eInner) : base(strMessage, eInner) {
		}
	}
	public class IPAddressJsonConverter : System.Text.Json.Serialization.JsonConverter<System.Net.IPAddress> {
		public override bool CanConvert(System.Type objectType) {
			return objectType == typeof(System.Net.IPAddress);
		}
		public override System.Net.IPAddress Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options) {
			return System.Net.IPAddress.Parse(reader.GetString());
		}
		public override void Write(System.Text.Json.Utf8JsonWriter writer, System.Net.IPAddress value, System.Text.Json.JsonSerializerOptions serializer) {
			writer.WriteStringValue(value.ToString());
		}
	}
	public class UShortJsonConverter : System.Text.Json.Serialization.JsonConverter<ushort> {
		public override bool CanConvert(System.Type objectType) {
			return objectType == typeof(ushort);
		}
		public override ushort Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options) {
			return ushort.Parse(reader.GetString());
		}
		public override void Write(System.Text.Json.Utf8JsonWriter writer, ushort value, System.Text.Json.JsonSerializerOptions options) {
			writer.WriteStringValue(value.ToString());
		}
	}
	public class WebOSCLIDeviceInput {
		public string Id {
			get; set;
		}
		public string Name {
			get; set;
		}
		public string Source {
			get; set;
		}
		public string AppIdShort {
			get; set;
		}
	}
	public class WebOSCLIDeviceInfo {
		[System.Text.Json.Serialization.JsonConverter(typeof(MagicRemoteService.IPAddressJsonConverter))]
		[System.Text.Json.Serialization.JsonPropertyName("ip")]
		public System.Net.IPAddress IP {
			get; set;
		}
		[System.Text.Json.Serialization.JsonConverter(typeof(MagicRemoteService.UShortJsonConverter))]
		[System.Text.Json.Serialization.JsonPropertyName("port")]
		public ushort Port {
			get; set;
		}
		[System.Text.Json.Serialization.JsonPropertyName("user")]
		public string User {
			get; set;
		}
	}
	public class WebOSCLIDeviceDetail {
		[System.Text.Json.Serialization.JsonPropertyName("platform")]
		public string Platform {
			get; set;
		}
		[System.Text.Json.Serialization.JsonPropertyName("privatekey")]
		public string PrivateKey {
			get; set;
		}
		[System.Text.Json.Serialization.JsonPropertyName("passphrase")]
		public string Passphrase {
			get; set;
		}
		[System.Text.Json.Serialization.JsonPropertyName("description")]
		public string Description {
			get; set;
		}
	}
	public class WebOSCLIDevice {

		[System.Text.Json.Serialization.JsonPropertyName("profile")]
		public string Profile {
			get; set;
		}
		[System.Text.Json.Serialization.JsonPropertyName("name")]
		public string Name {
			get; set;
		}
		[System.Text.Json.Serialization.JsonPropertyName("default")]
		public bool Default {
			get; set;
		}
		[System.Text.Json.Serialization.JsonPropertyName("deviceinfo")]
		public MagicRemoteService.WebOSCLIDeviceInfo DeviceInfo {
			get; set;
		}
		[System.Text.Json.Serialization.JsonPropertyName("connection")]
		public string[] Connection {
			get; set;
		}
		[System.Text.Json.Serialization.JsonPropertyName("details")]
		public MagicRemoteService.WebOSCLIDeviceDetail DeviceDetail {
			get; set;
		}
	}

	internal class WebOSCLIDeviceSet {

		[System.Text.Json.Serialization.JsonPropertyName("name")]
		public string Name {
			get; set;
		}
		[System.Text.Json.Serialization.JsonPropertyName("description")]
		public string Description {
			get; set;
		}
		[System.Text.Json.Serialization.JsonConverter(typeof(MagicRemoteService.IPAddressJsonConverter))]
		[System.Text.Json.Serialization.JsonPropertyName("host")]
		public System.Net.IPAddress IP {
			get; set;
		}
		[System.Text.Json.Serialization.JsonConverter(typeof(MagicRemoteService.UShortJsonConverter))]
		[System.Text.Json.Serialization.JsonPropertyName("port")]
		public ushort Port {
			get; set;
		}
		[System.Text.Json.Serialization.JsonPropertyName("username")]
		public string User {
			get; set;
		}
		[System.Text.Json.Serialization.JsonPropertyName("password")]
		public string Password {
			get; set;
		}
		[System.Text.Json.Serialization.JsonPropertyName("privatekey")]
		public string PrivateKey {
			get; set;
		}
		[System.Text.Json.Serialization.JsonPropertyName("passphrase")]
		public string Passphrase {
			get; set;
		}
	}
	internal static class WebOSCLI {
		private const int iDefaultTimeout = 120000;
		private static string ExecWebOSCLICommand(string strCommand, string strArgument, System.Collections.Generic.Dictionary<ushort, string> dInput = null, string strWorkingDirectory = null, int iTimeout = MagicRemoteService.WebOSCLI.iDefaultTimeout) {
			using(System.Diagnostics.Process pProcess = new System.Diagnostics.Process()) {
				pProcess.StartInfo.FileName = "cmd";
				if(!string.IsNullOrEmpty(strWorkingDirectory)) {
					pProcess.StartInfo.WorkingDirectory = strWorkingDirectory;
				}
				pProcess.StartInfo.Arguments = "/c " + strCommand + " " + strArgument;
				pProcess.StartInfo.UseShellExecute = false;
				pProcess.StartInfo.CreateNoWindow = true;
				pProcess.StartInfo.RedirectStandardInput = true;
				pProcess.StartInfo.RedirectStandardError = true;
				pProcess.StartInfo.RedirectStandardOutput = true;
				System.Text.StringBuilder sbErr = new System.Text.StringBuilder();
				System.Text.StringBuilder sbOutput = new System.Text.StringBuilder();
				ushort usOutputLine = 0;
				pProcess.ErrorDataReceived += delegate (object sender, System.Diagnostics.DataReceivedEventArgs e) {
					if(e.Data != null) {
						lock(sbErr) {
							sbErr.AppendLine(e.Data);
						}
					}
				};
				pProcess.OutputDataReceived += delegate (object sender, System.Diagnostics.DataReceivedEventArgs e) {
					if(e.Data != null) {
#if DEBUG
						System.Console.WriteLine(e.Data);
#endif
						ushort usLine;
						lock(sbOutput) {
							sbOutput.AppendLine(e.Data);
							usLine = ++usOutputLine;
						}
						if(dInput != null && dInput.TryGetValue(usLine, out string strInput)) {
							System.Threading.Tasks.Task.Run(async delegate () {
								await System.Threading.Tasks.Task.Delay(10);
								await pProcess.StandardInput.WriteLineAsync(strInput);
							});
						}
					}
				};
				MagicRemoteService.Service.LogIfDebug("webOS CLI: " + strCommand + " " + (strCommand == "ares-setup-device" && strArgument.Contains("password") ? "(arguments hidden)" : strArgument));
				pProcess.Start();
				pProcess.BeginErrorReadLine();
				pProcess.BeginOutputReadLine();
				if(!pProcess.WaitForExit(iTimeout)) {
					// Killing cmd alone would leave node running, taskkill /T ends the whole tree
					try {
						using(System.Diagnostics.Process pKill = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("taskkill", "/T /F /PID " + pProcess.Id) {
							UseShellExecute = false,
							CreateNoWindow = true
						})) {
							pKill.WaitForExit(10000);
						}
					} catch(System.Exception) {
					}
					string strPartial;
					lock(sbOutput) {
						strPartial = sbOutput.ToString().Trim();
					}
					throw new MagicRemoteService.WebOSCLIException(strCommand + " did not finish within " + (iTimeout / 1000) + "s (TV unreachable, or waiting for input?)" + (strPartial.Length == 0 ? "" : System.Environment.NewLine + strPartial));
				}
				// Waits for the redirected output to be fully read
				pProcess.WaitForExit();
				string strErr;
				string strOutput;
				lock(sbErr) {
					strErr = sbErr.ToString().Trim();
				}
				lock(sbOutput) {
					strOutput = sbOutput.ToString();
				}
				if(pProcess.ExitCode != 0) {
					MagicRemoteService.Service.Warn("webOS CLI " + strCommand + " failed with exit code " + pProcess.ExitCode + ": " + strErr + " " + strOutput.Trim());
					// Some ares commands report errors on the standard output, never show an empty message
					throw new MagicRemoteService.WebOSCLIException(strErr.Length != 0 ? strErr : strOutput.Trim().Length != 0 ? strOutput.Trim() : strCommand + " failed with exit code " + pProcess.ExitCode + ", is the webOS CLI installed and on the PATH?");
				}
				return strOutput;
			}
		}
		public static MagicRemoteService.WebOSCLIDeviceInput[] InputList() {
			return new MagicRemoteService.WebOSCLIDeviceInput[] {
				new MagicRemoteService.WebOSCLIDeviceInput() { Id = "HDMI_1", Name = "HDMI 1", Source = "ext://hdmi:1", AppIdShort = "hdmi1" },
				new MagicRemoteService.WebOSCLIDeviceInput() { Id = "HDMI_2", Name = "HDMI 2", Source = "ext://hdmi:2", AppIdShort = "hdmi2" },
				new MagicRemoteService.WebOSCLIDeviceInput() { Id = "HDMI_3", Name = "HDMI 3", Source = "ext://hdmi:3", AppIdShort = "hdmi3" },
				new MagicRemoteService.WebOSCLIDeviceInput() { Id = "HDMI_4", Name = "HDMI 4", Source = "ext://hdmi:4", AppIdShort = "hdmi4" }
			};
		}
		public static MagicRemoteService.WebOSCLIDevice[] SetupDeviceList() {
			System.Collections.Generic.List<string> tabArgument = new System.Collections.Generic.List<string> {
				"-F"
			};
			return System.Text.Json.JsonSerializer.Deserialize<MagicRemoteService.WebOSCLIDevice[]>(MagicRemoteService.WebOSCLI.ExecWebOSCLICommand("ares-setup-device", string.Join(" ", tabArgument)));
		}
		public static void SetupDeviceAdd(MagicRemoteService.WebOSCLIDevice wocdDevice, string strPassword) {
			System.Collections.Generic.List<string> tabArgument = new System.Collections.Generic.List<string> {
				"-a \"" + wocdDevice.Name + "\"",
				"-i \"" + System.Text.Json.JsonSerializer.Serialize<MagicRemoteService.WebOSCLIDeviceSet>(new MagicRemoteService.WebOSCLIDeviceSet {
					Name = wocdDevice.Name,
					Description = wocdDevice.DeviceDetail.Description,
					IP = wocdDevice.DeviceInfo.IP,
					Port = wocdDevice.DeviceInfo.Port,
					User = wocdDevice.DeviceInfo.User,
					Password = strPassword,
					PrivateKey = wocdDevice.DeviceDetail.PrivateKey,
					Passphrase = wocdDevice.DeviceDetail.Passphrase
				}).Replace("\"", "'") + "\""
			};
			MagicRemoteService.WebOSCLI.ExecWebOSCLICommand("ares-setup-device", string.Join(" ", tabArgument));
		}
		public static void SetupDeviceModify(string strDevice, MagicRemoteService.WebOSCLIDevice wocdDevice, string strPassword) {
			System.Collections.Generic.List<string> tabArgument = new System.Collections.Generic.List<string> {
				"-m \"" + strDevice + "\"",
				"-i \"" + System.Text.Json.JsonSerializer.Serialize<MagicRemoteService.WebOSCLIDeviceSet>(new MagicRemoteService.WebOSCLIDeviceSet {
					Name = wocdDevice.Name,
					Description = wocdDevice.DeviceDetail.Description,
					IP = wocdDevice.DeviceInfo.IP,
					Port = wocdDevice.DeviceInfo.Port,
					User = wocdDevice.DeviceInfo.User,
					Password = strPassword,
					PrivateKey = wocdDevice.DeviceDetail.PrivateKey,
					Passphrase = wocdDevice.DeviceDetail.Passphrase
				}).Replace("\"", "'") + "\""
			};
			MagicRemoteService.WebOSCLI.ExecWebOSCLICommand("ares-setup-device", string.Join(" ", tabArgument));
		}
		public static void SetupDeviceRemove(string strDevice) {
			System.Collections.Generic.List<string> tabArgument = new System.Collections.Generic.List<string> {
				"-r \"" + strDevice + "\""
			};
			MagicRemoteService.WebOSCLI.ExecWebOSCLICommand("ares-setup-device", string.Join(" ", tabArgument));
		}
		public static void NovacomGetKey(string strDevice, string strPassphrase) {
			System.Collections.Generic.List<string> tabArgument = new System.Collections.Generic.List<string> {
				"-d \"" + strDevice + "\"",
				"-k"
			};
			MagicRemoteService.WebOSCLI.ExecWebOSCLICommand("ares-novacom", string.Join(" ", tabArgument), new System.Collections.Generic.Dictionary<ushort, string>() { { 1, strPassphrase } });
		}
		public static void NovacomRun(string strDevice, string strCommand) {
			System.Collections.Generic.List<string> tabArgument = new System.Collections.Generic.List<string> {
				"-d \"" + strDevice + "\"",
				"-r \"" + strCommand.Replace("\"", "\\\"") + "\""
			};
			MagicRemoteService.WebOSCLI.ExecWebOSCLICommand("ares-novacom", string.Join(" ", tabArgument));
		}
		public static void Package(string strOutDirectory, string strApplication, string strService = null, string strPackage = null) {
			System.Collections.Generic.List<string> tabArgument = new System.Collections.Generic.List<string> {
				"\"" + strApplication + "\"",
				"-n"
			};
			if(!string.IsNullOrEmpty(strOutDirectory)) {
				tabArgument.Add("-o \"" + strOutDirectory + "\"");
			}
			if(!string.IsNullOrEmpty(strService)) {
				tabArgument.Add("\"" + strService + "\"");
			}
			if(!string.IsNullOrEmpty(strPackage)) {
				tabArgument.Add("\"" + strPackage + "\"");
			}
			MagicRemoteService.WebOSCLI.ExecWebOSCLICommand("ares-package", string.Join(" ", tabArgument), iTimeout: 300000);
		}
		public static void Install(string strDevice, string strPackageFile) {
			System.Collections.Generic.List<string> tabArgument = new System.Collections.Generic.List<string> {
				"-d \"" + strDevice + "\"",
				"\"" + strPackageFile + "\""
			};
			MagicRemoteService.WebOSCLI.ExecWebOSCLICommand("ares-install", string.Join(" ", tabArgument), iTimeout: 300000);
		}
		public static void InstallRemove(string strDevice, string strPackageFile) {
			System.Collections.Generic.List<string> tabArgument = new System.Collections.Generic.List<string> {
				"-d \"" + strDevice + "\"",
				"--remove \"" + strPackageFile + "\""
			};
			MagicRemoteService.WebOSCLI.ExecWebOSCLICommand("ares-install", string.Join(" ", tabArgument));
		}
		public static void Launch(string strDevice, string strApplicationId, string strParameter = null) {
			System.Collections.Generic.List<string> tabArgument = new System.Collections.Generic.List<string> {
				"-d \"" + strDevice + "\"",
				"\"" + strApplicationId + "\""
			};
			if(!string.IsNullOrEmpty(strParameter)) {
				tabArgument.Add("-p \"" + strParameter + "\"");
			}
			MagicRemoteService.WebOSCLI.ExecWebOSCLICommand("ares-launch", string.Join(" ", tabArgument));
		}
		public static void InspectApplication(string strDevice, string strApplicationId) {
			System.Collections.Generic.List<string> tabArgument = new System.Collections.Generic.List<string> {
				"-d \"" + strDevice + "\"",
				"-a \"" + strApplicationId + "\"",
				"-o"
			};
			MagicRemoteService.WebOSCLI.ExecWebOSCLICommand("ares-inspect", string.Join(" ", tabArgument));
		}
		public static void InspectService(string strDevice, string strServiceId) {
			System.Collections.Generic.List<string> tabArgument = new System.Collections.Generic.List<string> {
				"-d \"" + strDevice + "\"",
				"-s \"" + strServiceId + "\"",
				"-o"
			};
			MagicRemoteService.WebOSCLI.ExecWebOSCLICommand("ares-inspect", string.Join(" ", tabArgument));
		}
		public static string DeviceInfo(string strDevice) {
			System.Collections.Generic.List<string> tabArgument = new System.Collections.Generic.List<string> {
				"-d \"" + strDevice + "\"",
				"-i"
			};
			return MagicRemoteService.WebOSCLI.ExecWebOSCLICommand("ares-device", string.Join(" ", tabArgument));
		}
	}
}
