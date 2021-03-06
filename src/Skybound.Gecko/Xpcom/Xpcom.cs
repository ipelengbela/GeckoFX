﻿#region ***** BEGIN LICENSE BLOCK *****
/* Version: MPL 1.1/GPL 2.0/LGPL 2.1
 *
 * The contents of this file are subject to the Mozilla Public License Version
 * 1.1 (the "License"); you may not use this file except in compliance with
 * the License. You may obtain a copy of the License at
 * http://www.mozilla.org/MPL/
 *
 * Software distributed under the License is distributed on an "AS IS" basis,
 * WITHOUT WARRANTY OF ANY KIND, either express or implied. See the License
 * for the specific language governing rights and limitations under the
 * License.
 *
 * The Original Code is Skybound Software code.
 *
 * The Initial Developer of the Original Code is Skybound Software.
 * Portions created by the Initial Developer are Copyright (C) 2008-2009
 * the Initial Developer. All Rights Reserved.
 *
 * Contributor(s):
 *
 * Alternatively, the contents of this file may be used under the terms of
 * either the GNU General Public License Version 2 or later (the "GPL"), or
 * the GNU Lesser General Public License Version 2.1 or later (the "LGPL"),
 * in which case the provisions of the GPL or the LGPL are applicable instead
 * of those above. If you wish to allow use of your version of this file only
 * under the terms of either the GPL or the LGPL, and not to allow others to
 * use your version of this file under the terms of the MPL, indicate your
 * decision by deleting the provisions above and replace them with the notice
 * and other provisions required by the GPL or the LGPL. If you do not delete
 * the provisions above, a recipient may use your version of this file under
 * the terms of any one of the MPL, the GPL or the LGPL.
 */
#endregion END LICENSE BLOCK

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Forms;
using System.Reflection;
using System.Collections.Generic;

namespace Skybound.Gecko
{
	/// <summary>
	/// Provides low-level access to XPCOM.
	/// </summary>
	public static class Xpcom
	{
		#region Native Methods
		[DllImport("xpcom", CharSet = CharSet.Ansi)]
		static extern int NS_InitXPCOM2(out IntPtr serviceManager, [MarshalAs(UnmanagedType.IUnknown)] object binDirectory, nsIDirectoryServiceProvider appFileLocationProvider);

        [DllImport("xpcom", CharSet = CharSet.Ansi)]
        static extern int NS_ShutdownXPCOM(IntPtr serviceManager);

		[DllImport("xpcom", CharSet = CharSet.Ansi)]
		static extern int NS_NewNativeLocalFile(nsACString path, bool followLinks, [MarshalAs(UnmanagedType.IUnknown)] out object result);
		
		[DllImport("xpcom", CharSet = CharSet.Ansi)]
		static extern int NS_GetComponentManager(out nsInterfaces componentManager);
		
		[DllImport("xpcom", CharSet = CharSet.Ansi)]
		static extern int NS_GetComponentRegistrar(out nsIComponentRegistrar componentRegistrar);
		
		[DllImport("xpcom", EntryPoint="NS_Alloc")]
		public static extern IntPtr Alloc(int size);
		
		[DllImport("xpcom", EntryPoint="NS_Realloc")]
		public static extern IntPtr Realloc(IntPtr ptr, int size);
		
		[DllImport("xpcom", EntryPoint="NS_Free")]
		public static extern void Free(IntPtr ptr);
		#endregion

        #region Pre Load XULRunner Library
        [DllImport("kernel32.dll")]
        private static extern IntPtr LoadLibraryEx(string dllFilePath, IntPtr hFile, uint dwFlags);

        [DllImport("kernel32.dll")]
        public extern static bool FreeLibrary(IntPtr dllPointer);

        //static uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
        //static uint LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE = 0x00000040;
        static uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

        public static IntPtr LoadWin32Library(string dllFilePath)
        {
            try
            {
                System.IntPtr moduleHandle = LoadLibraryEx(dllFilePath, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
                if (moduleHandle == IntPtr.Zero)
                {
                    // I'm getting last dll error
                    int errorCode = Marshal.GetLastWin32Error();
                    throw new ApplicationException(string.Format("There was an error during dll loading : {0}, error - {1}", 
                                                       dllFilePath, 
                                                       errorCode));
                }

                return moduleHandle;
            }
            catch (Exception exc)
            {
                throw new Exception(String.Format("Couldn't load library {0}{1}{2}", dllFilePath, Environment.NewLine, exc.Message), exc);
            }
        }
        #endregion
        
        // gecko version encoded as 2 digits (00-99) for every version part
	    public const long DefaultGeckoVersion = 0x01080000; // 1.8.0.0

        // the best gecko versions library is suited for, these will be sorted to the top of the list
        // during gecko locations discovery
        public const long CompatibleGeckoVersionMin = 0x01080000;
        public const long CompatibleGeckoVersionMax = 0x01090299;

        // gecko used
        private static GeckoAppInfo geckoAppInfo;

	    public static long GeckoVersion
	    {
	        get { return (geckoAppInfo != null)?geckoAppInfo.GeckoVersion:DefaultGeckoVersion; }
	    }

	    public static GeckoAppInfo GeckoInfo
	    {
	        get
	        {
	            return geckoAppInfo;
	        }
	    }
        
        /// <summary>
		/// Initializes XPCOM using the current directory as the XPCOM directory.
		/// </summary>
		public static void Initialize()
		{
            if (_IsInitialized) return;
			Initialize(new GeckoAppInfo());
		}

        /// <summary>
        /// Initializes XPCOM with option to use any available Gecko installed (like Firefox).
        /// <param name="geckoAutoSearch"></param>
        /// </summary>
        public static void Initialize(bool geckoAutoSearch)
        {
            if (_IsInitialized) return;
            if (geckoAutoSearch)
            {
                GeckoAppInfo gai = null;
                GeckoAppDiscovery gad = null;
                DirectoryInfo appDirInfo = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
                DirectoryInfo[] xulrunnerDirs = appDirInfo.GetDirectories("xulrunner*");
                if (xulrunnerDirs != null && xulrunnerDirs.Length > 0)
                {
                    List<String> xulrunnerPaths = new List<String>();
                    Array.ForEach(xulrunnerDirs, dirInfo =>
                    {
                        if (dirInfo.Exists)
                            xulrunnerPaths.Add(dirInfo.FullName);
                    });
                    if (xulrunnerPaths.Count > 0)
                        gad = new GeckoAppDiscovery(xulrunnerPaths.ToArray());
                }
                if (gad == null)
                    gad = new GeckoAppDiscovery();
                if (gad.Geckos != null && gad.Geckos.Count > 0)
                    foreach (GeckoAppInfo appInfo in gad.Geckos)
                    {
                        if (!appInfo.IsGeckoValid)
                            continue;

                        gai = appInfo;
                        if (LoadWin32Library(Path.Combine(appInfo.GeckoPath, "mozcrt19.dll")) != IntPtr.Zero)
                        if (LoadWin32Library(Path.Combine(appInfo.GeckoPath, "AccessibleMarshal.dll")) != IntPtr.Zero)
                        if (LoadWin32Library(Path.Combine(appInfo.GeckoPath, "nspr4.dll")) != IntPtr.Zero)
                        if (LoadWin32Library(Path.Combine(appInfo.GeckoPath, "plc4.dll")) != IntPtr.Zero)
                        if (LoadWin32Library(Path.Combine(appInfo.GeckoPath, "plds4.dll")) != IntPtr.Zero)
                        if (LoadWin32Library(Path.Combine(appInfo.GeckoPath, "nssutil3.dll")) != IntPtr.Zero)
                        if (LoadWin32Library(Path.Combine(appInfo.GeckoPath, "nss3.dll")) != IntPtr.Zero)
                        if (LoadWin32Library(Path.Combine(appInfo.GeckoPath, "ssl3.dll")) != IntPtr.Zero)
                        if (LoadWin32Library(Path.Combine(appInfo.GeckoPath, "smime3.dll")) != IntPtr.Zero)
                        if (LoadWin32Library(Path.Combine(appInfo.GeckoPath, "js3250.dll")) != IntPtr.Zero)
                        if (LoadWin32Library(Path.Combine(appInfo.GeckoPath, "sqlite3.dll")) != IntPtr.Zero)
                        if (LoadWin32Library(Path.Combine(appInfo.GeckoPath, "softokn3.dll")) != IntPtr.Zero)
                        if (LoadWin32Library(Path.Combine(appInfo.GeckoPath, "freebl3.dll")) != IntPtr.Zero)
                        if (LoadWin32Library(Path.Combine(appInfo.GeckoPath, "nssdbm3.dll")) != IntPtr.Zero)
                        if (LoadWin32Library(Path.Combine(appInfo.GeckoPath, "nssckbi.dll")) != IntPtr.Zero)
                        {
                            bool bLoad = false;
                            if (File.Exists(Path.Combine(appInfo.GeckoPath, "xul.dll")))
                              bLoad = (LoadWin32Library(Path.Combine(appInfo.GeckoPath, "xul.dll")) != IntPtr.Zero);
                            else if (File.Exists(Path.Combine(appInfo.GeckoPath, "xpcom_core.dll")))
                              bLoad = (LoadWin32Library(Path.Combine(appInfo.GeckoPath, "xpcom_core.dll")) != IntPtr.Zero);
                            if (bLoad && LoadWin32Library(Path.Combine(appInfo.GeckoPath, "xpcom.dll")) != IntPtr.Zero)
                            {
                                Initialize(gai);
                                return;
                            }
                        }
                    }
                throw new Exception(String.Format("Please install Mozilla Firefox 3.6 or download xulrunner from:{0}http://releases.mozilla.org/pub/mozilla.org/xulrunner/releases/ {0}and unzip into the \"{1}\" application directory.",
                                        Environment.NewLine,
                                        Application.ProductName));
            }
            Initialize(new GeckoAppInfo());
        }

        /// <summary>
        /// Initializes XPCOM using the specified directory.
        /// </summary>
        public static void Initialize(string binDirectory)
        {
            if (_IsInitialized) return;
            Initialize(new GeckoAppInfo(binDirectory));
        }

        /// <summary>
		/// Initializes XPCOM using the specified GeckoAppInfo.
		/// </summary>
		public static void Initialize(GeckoAppInfo geckoInfo)
		{
			if (_IsInitialized)
				return;
			
			string folder = geckoInfo.GeckoPath;
            string binDirectory = (folder != Application.StartupPath) ? folder : null;
			string xpcomPath = Path.Combine(folder, "xpcom.dll");
			
			if (Debugger.IsAttached)
			{
				// make sure this DLL is there
				if (!File.Exists(xpcomPath))
				{
                    if (MessageBox.Show(String.Format("Couldn't find XULRunner in '{0}'. Call Xpcom.Initialize() in your application startup code and specify the directory where XULRunner is installed.{1}{1}If you do not have XULRunner installed, click Yes to open the download page.  Otherwise, click No, and update your application startup code.", 
                                            folder, 
                                            Environment.NewLine),
							"XULRunner Not Found", MessageBoxButtons.YesNo, MessageBoxIcon.Error) == DialogResult.Yes)
					{
						Process.Start("http://releases.mozilla.org/pub/mozilla.org/xulrunner/releases/");
					}
					
					Environment.Exit(0);
				}
			}
			
			if (binDirectory != null)
			{
				Environment.SetEnvironmentVariable("path",
                    String.Format("{0};{1}", Environment.GetEnvironmentVariable("path"), binDirectory), EnvironmentVariableTarget.Process);
			}
			
			object mreAppDir = null;
			
			if (binDirectory != null)
			{
				using (nsACString str = new nsACString(Path.GetFullPath(binDirectory)))
					if (NS_NewNativeLocalFile(str, true, out mreAppDir) != 0)
					{
						throw new Exception("Failed on NS_NewNativeLocalFile");
					}
			}
			
			// temporarily change the current directory so NS_InitEmbedding can find all the DLLs it needs
			String oldCurrent = Environment.CurrentDirectory;
			Environment.CurrentDirectory = folder;
			
			IntPtr serviceManagerPtr;
			//int res = NS_InitXPCOM2(out serviceManagerPtr, mreAppDir, new DirectoryServiceProvider());
			int res = NS_InitXPCOM2(out serviceManagerPtr, mreAppDir, null);
			
			// change back
			Environment.CurrentDirectory = oldCurrent;
			
			if (res != 0)
			{
				throw new Exception("Failed on NS_InitXPCOM2");
			}

            // ok, we initialized xpcom, most like we will work, so we need gecko version from now on
            geckoAppInfo = geckoInfo;
			
			ServiceManager = (nsIServiceManager)Marshal.GetObjectForIUnknown(serviceManagerPtr);
			
			// get some global objects we will need later
			NS_GetComponentManager(out ComponentManager);
			NS_GetComponentRegistrar(out ComponentRegistrar);
			
			// a bug in Mozilla 1.8 (https://bugzilla.mozilla.org/show_bug.cgi?id=309877) causes the PSM to
			// crash when loading a site over HTTPS.  in order to work around this bug, we must register an nsIDirectoryServiceProvider
			// which will provide the location of a profile
			nsIDirectoryService directoryService = GetService<nsIDirectoryService>("@mozilla.org/file/directory_service;1");
			directoryService.RegisterProvider(new ProfileProvider());

		    XULAppInfo.Initialize();
			
			_IsInitialized = true;
		}

	    public static void Shutdown()
        {
            if (!_IsInitialized)
                return;

            _IsInitialized = false;

            ComponentManager = null;
            ComponentRegistrar = null;

            NS_ShutdownXPCOM(Marshal.GetIUnknownForObject(ServiceManager));
            ServiceManager = null;

            XULAppInfo.Shutdown();
            GeckoWebBrowser.WindowCreator.Shutdown();
        }
		
		static bool _IsInitialized;
		
		static nsInterfaces ComponentManager;
		static nsIComponentRegistrar ComponentRegistrar;
		static nsIServiceManager ServiceManager;
		
		/// <summary>
		/// Gets or sets the path to the directory which contains the user profile.
		/// The default directory is Geckofx\DefaultProfile in the user's local application data directory.
		/// </summary>
		public static string ProfileDirectory
		{
			get
			{
				if (_ProfileDirectory == null)
				{
                    string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), String.Format(@"Geckofx\{0:X}\DefaultProfile", GeckoVersion >> 8));
                    if (Directory.Exists(directory))
                        return directory;

                    Directory.CreateDirectory(directory);
					return directory;
				}
				return _ProfileDirectory;
			}
			set
			{
				if (!string.IsNullOrEmpty(value))
				{
					if (!Directory.Exists(value))
					{
						throw new DirectoryNotFoundException();
					}
				}
				_ProfileDirectory = value;
			}
		}
		static string _ProfileDirectory;
		
		/// <summary>
		/// A simple nsIDirectoryServiceProvider which provides the profile directory.
		/// </summary>
		class ProfileProvider : nsIDirectoryServiceProvider
		{
			public nsIFile GetFile(string prop, out bool persistent)
			{
				persistent = false;
				
				if (prop == "ProfD")
				{
					return (nsIFile)NewNativeLocalFile(ProfileDirectory ?? "");
				}
				return null;
			}
		}
		
		public static object NewNativeLocalFile(string filename)
		{
			object result;
			
			using (nsACString str = new nsACString(filename))
				if (NS_NewNativeLocalFile(str, true, out result) == 0)
					return result;
			
			return null;
		}
		
		public static object CreateInstance(Guid classIID)
		{
			Guid iid = typeof(nsISupports).GUID;
			return ComponentManager.CreateInstance(ref classIID, null, ref iid);
		}
		
		public static object CreateInstance(string contractID)
		{
			return CreateInstance<nsISupports>(contractID);
		}
		
		public static TInterfaceType CreateInstance<TInterfaceType>(string contractID)
		{
            Guid iid = multiversion<TInterfaceType>.ActualType.GUID;
			return multiversion<TInterfaceType>.Cast(ComponentManager.CreateInstanceByContractID(contractID, null, ref iid));
		}
		
		public static TInterfaceType QueryInterface<TInterfaceType>(object obj)
		{
			return multiversion<TInterfaceType>.Cast(QueryInterface(obj, multiversion<TInterfaceType>.ActualType.GUID));
		}
		
		public static object QueryInterface(object obj, Guid iid)
		{
			if (obj == null)
				return null;
			
			// get an nsISupports (aka IUnknown) pointer from the objection
			IntPtr pUnk = Marshal.GetIUnknownForObject(obj);
			if (pUnk == IntPtr.Zero)
				return null;
			
			// query interface
			IntPtr ppv;
			Marshal.QueryInterface(pUnk, ref iid, out ppv);
			
			// if QueryInterface didn't work, try using nsIInterfaceRequestor instead
			if (ppv == IntPtr.Zero)
			{
				// QueryInterface the object for nsIInterfaceRequestor
				Guid interfaceRequestorIID = typeof(nsIInterfaceRequestor).GUID;
				IntPtr pInterfaceRequestor;
				Marshal.QueryInterface(pUnk, ref interfaceRequestorIID, out pInterfaceRequestor);
				
				// if we got a pointer to nsIInterfaceRequestor
				if (pInterfaceRequestor != IntPtr.Zero)
				{
					// convert it to a managed interface
					QI_nsIInterfaceRequestor req = (QI_nsIInterfaceRequestor)Marshal.GetObjectForIUnknown(pInterfaceRequestor);
					
					// try to get the requested interface
					req.GetInterface(ref iid, out ppv);
					
					// clean up
					Marshal.ReleaseComObject(req);
					Marshal.Release(pInterfaceRequestor);
				}
			}
			
			object result = (ppv != IntPtr.Zero) ? Marshal.GetObjectForIUnknown(ppv) : null;
			
			Marshal.Release(pUnk);
			if (ppv != IntPtr.Zero)
				Marshal.Release(ppv);
			
			return result;
		}
		
		/// <summary>
		/// A special declaration of nsIInterfaceRequestor used only for QueryInterface, using PreserveSig
		/// to prevent .NET from throwing an exception when the interface doesn't exist.
		/// </summary>
		[Guid("033a1470-8b2a-11d3-af88-00a024ffc08c"), ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		interface QI_nsIInterfaceRequestor
		{
			[PreserveSig] int GetInterface(ref Guid uuid, out IntPtr pUnk);
		}
		
		public static object GetService(Guid classIID)
		{
			Guid iid = typeof(nsISupports).GUID;
			return ServiceManager.GetService(ref classIID, ref iid);
		}
		
		public static object GetService(string contractID)
		{
			return GetService<nsISupports>(contractID);
		}
		
		public static TInterfaceType GetService<TInterfaceType>(string contractID)
		{
            Guid iid = multiversion<TInterfaceType>.ActualType.GUID;
		    var obj = ServiceManager.GetServiceByContractID(contractID, ref iid);
			return multiversion<TInterfaceType>.Cast(obj);
		}
		
		/// <summary>
		/// Registers a factory to be used to instantiate a particular class identified by ClassID, and creates an association of class name and ContractID with the class.
		/// </summary>
		/// <param name="classID">The ClassID of the class being registered.</param>
		/// <param name="className">The name of the class being registered. This value is intended as a human-readable name for the class and need not be globally unique.</param>
		/// <param name="contractID">The ContractID of the class being registered.</param>
		/// <param name="factory">The nsIFactory instance of the class being registered.</param>
		public static void RegisterFactory(Guid classID, string className, string contractID, nsIFactory factory)
		{
			ComponentRegistrar.RegisterFactory(ref classID, className, contractID, factory);
		}
	}
}
