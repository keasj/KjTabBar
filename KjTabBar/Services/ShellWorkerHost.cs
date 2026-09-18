using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using KjTabBar.Models;

namespace KjTabBar.Services
{
    internal static class ShellWorkerHost
    {
        internal const string StartupArgument = "--kjtb-shell-worker";

        internal static bool IsWorkerRequest(string[] args)
        {
            return args != null && args.Length > 0 &&
                string.Equals(args[0], StartupArgument, StringComparison.Ordinal);
        }

        internal static void Run(string[] args)
        {
            int parentId;
            if (args.Length != 2 || !int.TryParse(args[1], out parentId)) return;
            Process parent;
            try { parent = Process.GetProcessById(parentId); }
            catch (ArgumentException) { return; }
            // A blocked COM call must not leave a helper behind after the parent exits.
            Thread parentWatcher = new Thread(delegate()
            {
                try { parent.WaitForExit(); }
                finally { Environment.Exit(0); }
            });
            parentWatcher.IsBackground = true;
            parentWatcher.Start();
            ExplorerManager explorer = new ExplorerManager();
            DesktopShellItemPathCache desktop = new DesktopShellItemPathCache(explorer);
            using (Stream input = Console.OpenStandardInput())
            using (Stream output = Console.OpenStandardOutput())
            {
                while (true)
                {
                    string[] request;
                    try { request = ShellWorkerProtocol.Read(input); }
                    catch (EndOfStreamException) { return; }
                    string[] response;
                    try
                    {
                        string[] values = Execute(request, explorer, desktop);
                        response = new string[values.Length + 1];
                        response[0] = "ok";
                        Array.Copy(values, 0, response, 1, values.Length);
                    }
                    catch (Exception ex)
                    {
                        response = new[] { "error", ex.Message };
                    }
                    ShellWorkerProtocol.Write(output, response);
                }
            }
        }

        private static string[] Execute(string[] request, ExplorerManager explorer, DesktopShellItemPathCache desktop)
        {
            if (request.Length == 0) throw new InvalidDataException("Missing Shell operation.");
            ShellOperation operation = (ShellOperation)int.Parse(request[0], CultureInfo.InvariantCulture);
            string path = request.Length > 1 ? request[1] : string.Empty;
            switch (operation)
            {
                case ShellOperation.DesktopInvokedShortcut:
                    return new[] { DesktopRepeatedLaunchService.ResolveInvokedShortcut(ParseWindow(path), int.Parse(request[2], CultureInfo.InvariantCulture), explorer) };
                case ShellOperation.Ping: return new[] { Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) };
                case ShellOperation.CurrentPath: return new[] { explorer.GetCurrentPath(ParseWindow(path)) };
                case ShellOperation.SelectedItems: return explorer.GetSelectedItems(ParseWindow(path)).ToArray();
                case ShellOperation.SelectItems:
                    List<string> items = new List<string>();
                    for (int i = 2; i < request.Length; i++) items.Add(request[i]);
                    explorer.SelectItems(ParseWindow(path), items);
                    return new string[0];
                case ShellOperation.FolderName: return new[] { explorer.GetFolderName(path) };
                case ShellOperation.ParentFolderName: return new[] { explorer.GetParentFolderName(path) };
                case ShellOperation.ResolveShortcut: return new[] { explorer.ResolveShortcutTarget(path) };
                case ShellOperation.Navigate:
                    return new[] { explorer.Navigate(ParseWindow(path), request[2]) ? "1" : "0" };
                case ShellOperation.NamespaceTitle: return new[] { explorer.GetNamespaceTitleForWorker(path) };
                case ShellOperation.ShellPathAvailable: return new[] { explorer.IsShellPathAvailableForWorker(path) ? "1" : "0" };
                case ShellOperation.PathAvailable: return new[] { explorer.IsTabPathCurrentlyAvailable(path) ? "1" : "0" };
                case ShellOperation.DesktopContains: return new[] { desktop.Contains(path) ? "1" : "0" };
                case ShellOperation.DesktopShortcutMatch:
                    return new[] { ExplorerAbsorptionLogic.IsDesktopShortcutTargetPath(explorer, path) ? "1" : "0" };
                case ShellOperation.PathsAvailable:
                    string[] availability = new string[request.Length - 1];
                    for (int i = 1; i < request.Length; i++) availability[i - 1] = explorer.IsTabPathCurrentlyAvailable(request[i]) ? "1" : "0";
                    return availability;
                case ShellOperation.ReleaseCaches:
                    explorer.ReleaseCachedComObjects();
                    return new string[0];
                case ShellOperation.Icon:
                    return new[] { Convert.ToBase64String(ViewModels.TabItemViewModel.LoadIconBytesForWorker(path, explorer)) };
                default: throw new InvalidDataException("Unsupported Shell operation.");
            }
        }

        private static IntPtr ParseWindow(string value)
        {
            return new IntPtr(long.Parse(value, CultureInfo.InvariantCulture));
        }
    }
}
