namespace MagicRemoteService {
	static class SystemCursor {
		private static readonly System.IntPtr hMagicRemoteServiceCursor = SystemCursor.GetCursor(MagicRemoteService.Properties.Resources.MagicRemoteServiceCursor);
		private static readonly WinApi.OemCursorRessourceId[] arrCursor = new WinApi.OemCursorRessourceId[] {
			WinApi.OemCursorRessourceId.OCR_NORMAL,
			WinApi.OemCursorRessourceId.OCR_IBEAM,
			WinApi.OemCursorRessourceId.OCR_WAIT,
			WinApi.OemCursorRessourceId.OCR_CROSS,
			WinApi.OemCursorRessourceId.OCR_UP,
			WinApi.OemCursorRessourceId.OCR_HAND,
			WinApi.OemCursorRessourceId.OCR_NO,
			WinApi.OemCursorRessourceId.OCR_APPSTARTING
		};
		private static readonly System.Collections.Generic.IDictionary<WinApi.OemCursorRessourceId, System.IntPtr> dSystemCursor = new System.Collections.Generic.Dictionary<WinApi.OemCursorRessourceId, System.IntPtr>();
		private static readonly object oLock = new object();
		private static readonly int iMagicRemoteServiceMouseSpeed = 10;
		private static readonly int[] arrMagicRemoteServiceMouseAccel = new int[3] { 0, 0, 0 };
		// The user's settings, saved once when the remote takes over and restored once when it gives back control
		private static bool bDefaultMouseSaved = false;
		private static readonly int[] arrDefaultMouseSpeed = new int[1];
		private static readonly int[] arrDefaultMouseAccel = new int[3];
		// Written while the remote's cursor or mouse settings are active, so a later start can undo them if this process died without restoring
		private const string strRecoveryKey = @"Software\MagicRemoteService\CursorRecovery";
		private static Microsoft.Win32.RegistryKey RootKey {
			get {
				return MagicRemoteService.Program.bElevated ? Microsoft.Win32.Registry.LocalMachine : Microsoft.Win32.Registry.CurrentUser;
			}
		}
		private static void SetRecovery(string strName, object oValue) {
			try {
				using(Microsoft.Win32.RegistryKey rkRecovery = MagicRemoteService.SystemCursor.RootKey.CreateSubKey(MagicRemoteService.SystemCursor.strRecoveryKey)) {
					if(oValue == null) {
						rkRecovery.DeleteValue(strName, false);
					} else {
						rkRecovery.SetValue(strName, oValue);
					}
				}
			} catch(System.Exception eException) {
				MagicRemoteService.Service.LogIfDebug("Unable to update cursor recovery data: " + eException.Message);
			}
		}
		public static void RecoverAfterCrash() {
			lock(MagicRemoteService.SystemCursor.oLock) {
				try {
					using(Microsoft.Win32.RegistryKey rkRecovery = MagicRemoteService.SystemCursor.RootKey.OpenSubKey(MagicRemoteService.SystemCursor.strRecoveryKey)) {
						if(rkRecovery == null || rkRecovery.ValueCount == 0) {
							return;
						}
						if(rkRecovery.GetValue("Cursor") != null) {
							// Reloads the cursor scheme from the registry (the system default scheme when running with the service's account)
							WinApi.User32.SystemParametersInfo(WinApi.SystemParametersInfoAction.SPI_SETCURSORS, 0, System.IntPtr.Zero, 0);
							MagicRemoteService.Service.Warn("The remote cursor was still set from a previous run, system cursors reloaded");
						}
						if(rkRecovery.GetValue("MouseSpeed") is int iSpeed && rkRecovery.GetValue("MouseAccel") is string strAccel) {
							string[] arrAccel = strAccel.Split(',');
							if(arrAccel.Length == 3 && int.TryParse(arrAccel[0], out int i0) && int.TryParse(arrAccel[1], out int i1) && int.TryParse(arrAccel[2], out int i2)) {
								WinApi.User32.SystemParametersInfo(WinApi.SystemParametersInfoAction.SPI_SETMOUSESPEED, 0, new System.IntPtr(iSpeed), 0);
								MagicRemoteService.SystemCursor.SystemParametersInfo(WinApi.SystemParametersInfoAction.SPI_SETMOUSE, new int[] { i0, i1, i2 });
								MagicRemoteService.Service.Warn("The remote mouse settings were still set from a previous run, restored speed " + iSpeed + " and acceleration " + strAccel);
							}
						}
					}
					MagicRemoteService.SystemCursor.RootKey.DeleteSubKey(MagicRemoteService.SystemCursor.strRecoveryKey, false);
				} catch(System.Exception eException) {
					MagicRemoteService.Service.Warn("Unable to recover the cursor from a previous run: " + eException.Message);
				}
			}
		}
		private static bool SystemParametersInfo(WinApi.SystemParametersInfoAction spia, int[] arrParam) {
			System.Runtime.InteropServices.GCHandle gh = System.Runtime.InteropServices.GCHandle.Alloc(arrParam, System.Runtime.InteropServices.GCHandleType.Pinned);
			try {
				return WinApi.User32.SystemParametersInfo(spia, 0, gh.AddrOfPinnedObject(), 0);
			} finally {
				gh.Free();
			}
		}
		private static System.IntPtr GetCursor(byte[] arrCursor) {
			string strCursor = System.IO.Path.GetTempFileName();
			System.IO.File.WriteAllBytes(strCursor, arrCursor);
			System.IntPtr hCursor = WinApi.User32.LoadCursorFromFile(strCursor);
			System.IO.File.Delete(strCursor);
			return hCursor;
		}
		public static void SetMagicRemoteServiceSystemCursor() {
			lock(MagicRemoteService.SystemCursor.oLock) {
				foreach(WinApi.OemCursorRessourceId ocri in MagicRemoteService.SystemCursor.arrCursor) {
					if(!MagicRemoteService.SystemCursor.dSystemCursor.ContainsKey(ocri)) {
						MagicRemoteService.SystemCursor.dSystemCursor.Add(ocri, WinApi.User32.CopyIcon(WinApi.User32.LoadCursor(System.IntPtr.Zero, ocri)));
					}
					WinApi.User32.SetSystemCursor(WinApi.User32.CopyIcon(MagicRemoteService.SystemCursor.hMagicRemoteServiceCursor), ocri);
				}
				MagicRemoteService.SystemCursor.SetRecovery("Cursor", 1);
			}
		}
		public static void SetDefaultSystemCursor() {
			lock(MagicRemoteService.SystemCursor.oLock) {
				foreach(WinApi.OemCursorRessourceId ocri in MagicRemoteService.SystemCursor.arrCursor) {
					if(MagicRemoteService.SystemCursor.dSystemCursor.TryGetValue(ocri, out System.IntPtr hSystemCursor)) {
						WinApi.User32.SetSystemCursor(hSystemCursor, ocri);
						MagicRemoteService.SystemCursor.dSystemCursor.Remove(ocri);
					}
				}
				MagicRemoteService.SystemCursor.SetRecovery("Cursor", null);
			}
		}
		public static void SetMagicRemoteServiceMouseSpeedAccel() {
			lock(MagicRemoteService.SystemCursor.oLock) {
				// Only save on the first call, a repeated "cursor visible" message would otherwise save the remote's own settings as the user's
				if(!MagicRemoteService.SystemCursor.bDefaultMouseSaved) {
					if(!MagicRemoteService.SystemCursor.SystemParametersInfo(WinApi.SystemParametersInfoAction.SPI_GETMOUSESPEED, MagicRemoteService.SystemCursor.arrDefaultMouseSpeed) || !MagicRemoteService.SystemCursor.SystemParametersInfo(WinApi.SystemParametersInfoAction.SPI_GETMOUSE, MagicRemoteService.SystemCursor.arrDefaultMouseAccel)) {
						MagicRemoteService.Service.Warn("Unable to read the mouse speed and acceleration, leaving them unchanged");
						return;
					}
					MagicRemoteService.SystemCursor.bDefaultMouseSaved = true;
					MagicRemoteService.SystemCursor.SetRecovery("MouseSpeed", MagicRemoteService.SystemCursor.arrDefaultMouseSpeed[0]);
					MagicRemoteService.SystemCursor.SetRecovery("MouseAccel", string.Join(",", MagicRemoteService.SystemCursor.arrDefaultMouseAccel));
					MagicRemoteService.Service.LogIfDebug("Saved mouse speed " + MagicRemoteService.SystemCursor.arrDefaultMouseSpeed[0] + " and acceleration " + string.Join(",", MagicRemoteService.SystemCursor.arrDefaultMouseAccel));
				}
				WinApi.User32.SystemParametersInfo(WinApi.SystemParametersInfoAction.SPI_SETMOUSESPEED, 0, new System.IntPtr(MagicRemoteService.SystemCursor.iMagicRemoteServiceMouseSpeed), 0);
				MagicRemoteService.SystemCursor.SystemParametersInfo(WinApi.SystemParametersInfoAction.SPI_SETMOUSE, MagicRemoteService.SystemCursor.arrMagicRemoteServiceMouseAccel);
			}
		}
		public static void SetDefaultMouseSpeedAccel() {
			lock(MagicRemoteService.SystemCursor.oLock) {
				if(MagicRemoteService.SystemCursor.bDefaultMouseSaved) {
					WinApi.User32.SystemParametersInfo(WinApi.SystemParametersInfoAction.SPI_SETMOUSESPEED, 0, new System.IntPtr(MagicRemoteService.SystemCursor.arrDefaultMouseSpeed[0]), 0);
					MagicRemoteService.SystemCursor.SystemParametersInfo(WinApi.SystemParametersInfoAction.SPI_SETMOUSE, MagicRemoteService.SystemCursor.arrDefaultMouseAccel);
					MagicRemoteService.SystemCursor.bDefaultMouseSaved = false;
					MagicRemoteService.SystemCursor.SetRecovery("MouseSpeed", null);
					MagicRemoteService.SystemCursor.SetRecovery("MouseAccel", null);
				}
			}
		}
	}
}
