using System;
using System.IO;

namespace Radon.Runtime.Utilities;

public static class Constants
{
    static Constants()
    {
        RuntimePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "/Radon";
        LogPath = RuntimePath + "/Logs";
        if (!Directory.Exists(RuntimePath))
        {
            Directory.CreateDirectory(RuntimePath);
        }
        
        if (!Directory.Exists(LogPath))
        {
            Directory.CreateDirectory(LogPath);
        }
        
        LogFile = LogPath + "/Runtime.log";
    }

    public static readonly string RuntimePath;
    public static readonly string LogPath;
    public static readonly string LogFile;
    public const string UserVersion = "1.0.0";
}