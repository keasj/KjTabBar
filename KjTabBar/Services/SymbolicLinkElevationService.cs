using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using KjTabBar.Helpers;

namespace KjTabBar.Services
{
    internal static class SymbolicLinkElevationService
    {
        internal const string StartupArgument = "--kjtb-create-symbolic-links";

        internal static bool IsWorkerRequest(string[] args)
        {
            return args != null && args.Length > 0 && args[0] == StartupArgument;
        }

        internal static int Create(List<Tuple<string, string, bool>> links, IntPtr owner)
        {
            string requestPath = null;
            try
            {
                requestPath = Path.GetTempFileName();
                string[] values = new string[links.Count * 3];
                for (int i = 0; i < links.Count; i++)
                {
                    values[i * 3] = links[i].Item1;
                    values[i * 3 + 1] = links[i].Item2;
                    values[i * 3 + 2] = links[i].Item3 ? "1" : "0";
                }
                byte[] payload;
                using (MemoryStream stream = new MemoryStream())
                {
                    ShellWorkerProtocol.Write(stream, values);
                    payload = stream.ToArray();
                }
                File.WriteAllBytes(requestPath, payload);
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = typeof(SymbolicLinkElevationService).Assembly.Location,
                    Arguments = StartupArgument + " \"" + requestPath + "\" " + ComputeHash(payload),
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    ErrorDialogParentHandle = owner
                };
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null) return 1;
                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) return 1223;
                AppLogger.LogError("SymbolicLinkElevationService", "Failed to launch elevated link creation.", ex);
                return 1;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("SymbolicLinkElevationService", "Failed to create elevated links.", ex);
                return 1;
            }
            finally
            {
                try { if (requestPath != null) File.Delete(requestPath); }
                catch (Exception ex) { AppLogger.LogError("SymbolicLinkElevationService", "Failed to remove link request.", ex); }
            }
        }

        internal static int Run(string[] args)
        {
            try
            {
                if (args == null || args.Length != 3 || !IsWorkerRequest(args)) return 1;
                byte[] payload = File.ReadAllBytes(args[1]);
                // Bind the elevated operation to the exact request prepared before the UAC prompt.
                if (!string.Equals(ComputeHash(payload), args[2], StringComparison.Ordinal)) return 1;
                string[] values;
                using (MemoryStream stream = new MemoryStream(payload, false))
                {
                    values = ShellWorkerProtocol.Read(stream);
                    if (stream.Position != stream.Length) return 1;
                }
                return Execute(values, NativeMethods.CreateSymbolicLink);
            }
            catch (Exception)
            {
                return 1;
            }
        }

        internal static int Execute(string[] values, Func<string, string, uint, bool> createLink)
        {
            if (values == null || values.Length == 0 || values.Length % 3 != 0) return 1;
            for (int i = 0; i < values.Length; i += 3)
            {
                if (string.IsNullOrEmpty(values[i]) || string.IsNullOrEmpty(values[i + 1]) ||
                    !Path.IsPathRooted(values[i]) || !Path.IsPathRooted(values[i + 1]) ||
                    (values[i + 2] != "0" && values[i + 2] != "1")) return 1;
            }
            int result = 0;
            for (int i = 0; i < values.Length; i += 3)
            {
                uint flags = values[i + 2] == "1" ? NativeMethods.SYMBOLIC_LINK_FLAG_DIRECTORY : NativeMethods.SYMBOLIC_LINK_FLAG_FILE;
                // CreateSymbolicLink never replaces an existing destination.
                if (!createLink(values[i], values[i + 1], flags)) result = 1;
            }
            return result;
        }

        private static string ComputeHash(byte[] payload)
        {
            using (SHA256 hash = SHA256.Create())
            {
                return Convert.ToBase64String(hash.ComputeHash(payload));
            }
        }
    }
}
