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
				}
			}
		}
	}
}
