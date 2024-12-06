/* 
This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.
You should have received a copy of the GNU General Public License along with this program. If not, see <https://www.gnu.org/licenses/>.
*/

// coment the next line for releases build
#define FORCE_DEBUG
#if UNITY_EDITOR
#define DEBUG_ACTIVE
#elif FORCE_DEBUG
#define DEBUG_ACTIVE
#endif

//#define EXTERNAL_STORAGE_ACTIVE

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Android;

public static class ConfigManager
{
    //paths
#if UNITY_EDITOR
    public static  string BasePrivateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "cabs");
    //public static  string BasePublicDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "publiccabs");

    public static string BaseAppDir = BaseDir + "/data";
#else
    public static string Bundle = "com.curif.AgeOfJoy";
    public static string BaseAppDir = "/data/data/" + Bundle;
    //public static  string BasePublicDir = "/storage/emulated/0/Documents/AgeOfJoy";
    public static  string BasePrivateDir = "/sdcard/Android/data/" + Bundle;
#endif
    public static string BaseDir = BasePrivateDir;

    // Private backing fields for properties
    public static string Cabinets = Path.Combine(BaseDir, "cabinets"); //$"{BaseDir}/cabinets"; //compressed
    public static string CabinetsDB = Path.Combine(BaseDir, "cabinetsdb"); //uncompressed cabinets
    public static string SystemDir = Path.Combine(BaseDir, "system");
    public static string RomsDir = Path.Combine(BaseDir, "downloads");
    public static string GameSaveDir = Path.Combine(BaseDir, "save");
    public static string GameStatesDir = Path.Combine(BaseDir, "startstates");
    public static string ConfigDir = Path.Combine(BaseDir, "configuration");
    public static string ConfigControllersDir = Path.Combine(ConfigDir, "controllers");
    public static string ConfigControllerSchemesDir = Path.Combine(ConfigDir, "controllers/schemes");
    public static string AGEBasicDir = Path.Combine(BaseDir, "AGEBasic");
    public static string DebugDir = Path.Combine(BaseDir, "debug");
    public static string SamplesDir = Path.Combine(SystemDir, "samples");
    public static string MameConfigDir = Path.Combine(GameSaveDir, "cfg");
    public static string nvramDir = Path.Combine(GameSaveDir, "nvram");
    public static string MusicDir = Path.Combine(BaseDir, "music");
    public static string CoresDir = Path.Combine(BaseDir, "cores");
    public static string InternalCoresDir = Path.Combine(BaseAppDir, "usercores");
    public static string ConfigCoresDir = Path.Combine(ConfigDir, "cores");


    /*
     * 
     * Quest is not allowing to use another path different to
     * the application path so to maintain this code is an overhead.
     * 
     * 
    // Private backing fields for properties
    private static string _Cabinets;
    private static string _CabinetsDB;
    private static string _SystemDir;
    private static string _RomsDir;
    private static string _GameSaveDir;
    private static string _GameStatesDir;
    private static string _ConfigDir;
    private static string _ConfigControllersDir;
    private static string _ConfigControllerSchemesDir;
    private static string _AGEBasicDir;
    private static string _DebugDir;
    private static string _SamplesDir;
    private static string _MameConfigDir;
    private static string _nvramDir;
    private static string _MusicDir;
    private static string _CoresDir;
    private static string _InternalCoresDir;
    private static string _ConfigCoresDir;

    public static ConfigInformation configuration;

    // Properties with exception handling
    public static string Cabinets => Init.PermissionGranted ? _Cabinets : throw new InvalidOperationException("Player does not grant folder permissions.");
    public static string CabinetsDB => Init.PermissionGranted ? _CabinetsDB : throw new InvalidOperationException("Player does not grant folder permissions.");
    public static string SystemDir => Init.PermissionGranted ? _SystemDir : throw new InvalidOperationException("Player does not grant folder permissions.");
    public static string RomsDir => Init.PermissionGranted ? _RomsDir : throw new InvalidOperationException("Player does not grant folder permissions.");
    public static string GameSaveDir => Init.PermissionGranted ? _GameSaveDir : throw new InvalidOperationException("Player does not grant folder permissions.");
    public static string GameStatesDir => Init.PermissionGranted ? _GameStatesDir : throw new InvalidOperationException("Player does not grant folder permissions.");
    public static string ConfigDir => Init.PermissionGranted ? _ConfigDir : throw new InvalidOperationException("Player does not grant folder permissions.");
    public static string ConfigControllersDir => Init.PermissionGranted ? _ConfigControllersDir : throw new InvalidOperationException("Player does not grant folder permissions.");
    public static string ConfigControllerSchemesDir => Init.PermissionGranted ? _ConfigControllerSchemesDir : throw new InvalidOperationException("Player does not grant folder permissions.");
    public static string AGEBasicDir => Init.PermissionGranted ? _AGEBasicDir : throw new InvalidOperationException("Player does not grant folder permissions.");
    public static string DebugDir => Init.PermissionGranted ? _DebugDir : throw new InvalidOperationException("Player does not grant folder permissions.");
    public static string SamplesDir => Init.PermissionGranted ? _SamplesDir : throw new InvalidOperationException("Player does not grant folder permissions.");
    public static string MameConfigDir => Init.PermissionGranted ? _MameConfigDir : throw new InvalidOperationException("Player does not grant folder permissions.");
    public static string nvramDir => Init.PermissionGranted ? _nvramDir : throw new InvalidOperationException("Player does not grant folder permissions.");
    public static string MusicDir => Init.PermissionGranted ? _MusicDir : throw new InvalidOperationException("Player does not grant folder permissions.");
    public static string CoresDir => Init.PermissionGranted ? _CoresDir : throw new InvalidOperationException("Player does not grant folder permissions.");
    public static string InternalCoresDir => Init.PermissionGranted ? _InternalCoresDir : throw new InvalidOperationException("Player does not grant folder permissions.");
    public static string ConfigCoresDir => Init.PermissionGranted ? _ConfigCoresDir : throw new InvalidOperationException("Player does not grant folder permissions.");
    */
    public static bool DebugActive
    {
        get
        {
#if DEBUG_ACTIVE
            return true;
#else
            return false;
#endif
        }
    }

    //public static bool PermissionStorage { get; set; }

    private static void createFolders()
    {
        /*if (!Init.PermissionGranted)
        {
            throw new InvalidOperationException("[ConfigManager.createFolders] Player does not grant folder permissions.");
        }
        */
        /*
#if UNITY_EDITOR
        CreateFolder(BaseDir);
#endif
        if (publicStorage)
        {
            BaseDir = BasePublicDir;
            CreateFolder(BaseDir);
        }
        */

        WriteConsole($"[ConfigManager] base folder: {BaseDir}");


        CreateFolder(BaseDir);
        CreateFolder(Cabinets);
        CreateFolder(CabinetsDB);
        CreateFolder(ConfigDir);
        CreateFolder(ConfigControllersDir);
        CreateFolder(ConfigControllerSchemesDir);
        CreateFolder(AGEBasicDir);
        CreateFolder(DebugDir);
        CreateFolder(MusicDir);
        CreateFolder(CoresDir);
        CreateFolder(InternalCoresDir);
        CreateFolder(SystemDir);
        CreateFolder(RomsDir);
        CreateFolder(GameSaveDir);
        CreateFolder(SamplesDir);
        CreateFolder(MameConfigDir);
        CreateFolder(nvramDir);
        CreateFolder(ConfigCoresDir);
    }

    public static bool ShouldUseInternalStorage()
    {
#if EXTERNAL_STORAGE_ACTIVE
        return Directory.Exists(Path.Combine(BasePrivateDir, "downloads"));
#else
        return true;
#endif
    }

    //called from Init.cs
    public static void InitFolders()
    {
        /*if (!Init.PermissionGranted)
        {
            throw new Exception("[ConfigManager.InitFolders] Player does not grant folder permissions.");
        }
        */
        WriteConsole($"[ConfigManager] =================  Config Manager START ========================");
        createFolders();
    }

    public static void CreateFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            try
            {
                Directory.CreateDirectory(path);
                WriteConsole($"[ConfigManager.CreateDirectory] created: {path}");
            }
            catch (Exception e)
            {
                WriteConsoleException($"[ConfigManager.CreateDirectory] {path}", e);
            }
        }
    }

    /*
    It didn't works: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.conditionalattribute?view=netstandard-2.1
    fallback to #if DEBUG_ACTIVE but is less performant, because the call exists to the routine.
    */
    // [System.Diagnostics.Conditional("DEBUG_ACTIVE")]

    public static void WriteConsole(string st)
    {
#if DEBUG_ACTIVE
        UnityEngine.Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "[AGE] {0}", st);
#endif
    }
    // [System.Diagnostics.Conditional("DEBUG_ACTIVE")]
    public static void WriteConsoleError(string st)
    {
#if DEBUG_ACTIVE
        UnityEngine.Debug.LogFormat(LogType.Error, LogOption.None, null, "[AGE ERROR] {0}", st);
#endif
    }
    // [System.Diagnostics.Conditional("DEBUG_ACTIVE")]
    public static void WriteConsoleWarning(string st)
    {
#if DEBUG_ACTIVE
        UnityEngine.Debug.LogFormat(LogType.Warning, LogOption.None, null, "[AGE WARNING] {0}", st);
#endif
    }
    // [System.Diagnostics.Conditional("DEBUG_ACTIVE")]
    public static void WriteConsoleException(string st, Exception e)
    {
#if DEBUG_ACTIVE
        UnityEngine.Debug.LogFormat(LogType.Exception, LogOption.None, null,
                    "[AGE ERROR EXCEPTION] {0} Exception {1} StackTrace: \n {2}", st, e, e.StackTrace);
#endif
    }

    public static void AssertWriteConsole(bool mustBe, string st)
    {
#if DEBUG_ACTIVE
        if (! mustBe)
            UnityEngine.Debug.LogFormat(LogType.Error, LogOption.None, null, "[AGE ASSERTION ERROR] {0}", st);
#endif
    }


    public static void WriteConsoleAGEBasic(string st)
    {
        UnityEngine.Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "[AGEBASIC LOG] {0}", st);
    }
    public static void WriteConsoleErrorAGEBasic(string st)
    {
        UnityEngine.Debug.LogFormat(LogType.Error, LogOption.None, null, "[AGEBASIC LOG ERROR] {0}", st);
    }
    public static void WriteConsoleWarningAGEBasic(string st)
    {
        UnityEngine.Debug.LogFormat(LogType.Warning, LogOption.None, null, "[AGEBASIC LOG WARNING] {0}", st);
    }

    public static void AssertWriteConsoleAGEBasic(bool mustBe, string st)
    {
        if (!mustBe)
            UnityEngine.Debug.LogFormat(LogType.Error, LogOption.None, null, "[AGEBASIC ASSERTION] {0}", st);
    }

}
