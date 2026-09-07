using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KjTabBar.Services
{
    internal enum ShellOperation
    {
        Ping, CurrentPath, SelectedItems, SelectItems, FolderName, ParentFolderName,
        ResolveShortcut, Navigate, NamespaceTitle, ShellPathAvailable, PathAvailable,
        DesktopContains, Icon, PathsAvailable, ReleaseCaches, DesktopShortcutMatch
    }

    // Private inherited pipes carry only a fixed set of Shell operations, never executable code.
    internal static class ShellWorkerProtocol
    {
        private const int MaxPayloadLength = 8 * 1024 * 1024;
        internal static void Write(Stream stream, string[] values)
        {
            using (MemoryStream payload = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(payload, Encoding.UTF8, true))
                {
                    writer.Write(values.Length);
                    foreach (string value in values)
                    {
                        writer.Write(value != null);
                        if (value != null) writer.Write(value);
                    }
                }
                if (payload.Length > MaxPayloadLength) throw new InvalidDataException("Shell message is too large.");
                BinaryWriter output = new BinaryWriter(stream, Encoding.UTF8, true);
                output.Write((int)payload.Length);
                payload.Position = 0;
                payload.CopyTo(stream);
                stream.Flush();
            }
        }

        internal static string[] Read(Stream stream)
        {
            BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, true);
            int length = reader.ReadInt32();
            // .NET Framework's Process.StandardInput may emit the UTF-8 preamble when
            // its StreamWriter enables AutoFlush, even when we only use BaseStream.
            if ((length & 0x00FFFFFF) == 0x00BFBBEF)
            {
                length = (int)((uint)length >> 24) | (reader.ReadByte() << 8) |
                    (reader.ReadByte() << 16) | (reader.ReadByte() << 24);
            }
            if (length < 4 || length > MaxPayloadLength) throw new InvalidDataException("Invalid Shell message length: " + length.ToString("X8", CultureInfo.InvariantCulture));
            byte[] data = reader.ReadBytes(length);
            if (data.Length != length) throw new EndOfStreamException();
            using (MemoryStream payload = new MemoryStream(data, false))
            using (BinaryReader input = new BinaryReader(payload, Encoding.UTF8))
            {
                int count = input.ReadInt32();
                if (count < 0 || count > 65536) throw new InvalidDataException("Invalid Shell item count.");
                string[] values = new string[count];
                for (int i = 0; i < count; i++) values[i] = input.ReadBoolean() ? input.ReadString() : null;
                if (payload.Position != payload.Length) throw new InvalidDataException("Trailing Shell message data.");
                return values;
            }
        }
    }

    internal sealed class ShellWorkerClient : IDisposable
    {
        private readonly object _sync = new object();
        private readonly Func<ProcessStartInfo> _createStartInfo;
        private readonly TimeSpan _timeout;
        private Process _process;
        private bool _disposed;

        public ShellWorkerClient()
            : this(CreateStartInfo, TimeSpan.FromSeconds(4))
        {
        }

        internal ShellWorkerClient(Func<ProcessStartInfo> createStartInfo, TimeSpan timeout)
        {
            if (createStartInfo == null) throw new ArgumentNullException("createStartInfo");
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException("timeout");
            _createStartInfo = createStartInfo;
            _timeout = timeout;
        }

        private static ProcessStartInfo CreateStartInfo()
        {
            return new ProcessStartInfo
            {
                FileName = typeof(ShellWorkerClient).Assembly.Location,
                Arguments = ShellWorkerHost.StartupArgument + " " +
                    Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture)
            };
        }

        internal string[] Invoke(ShellOperation operation, params string[] arguments)
        {
            if (!Monitor.TryEnter(_sync, _timeout)) throw new TimeoutException("The Shell worker is busy.");
            try
            {
                if (_disposed) throw new ObjectDisposedException("ShellWorkerClient");
                EnsureStarted();
                string[] request = new string[arguments.Length + 1];
                request[0] = ((int)operation).ToString(CultureInfo.InvariantCulture);
                Array.Copy(arguments, 0, request, 1, arguments.Length);
                Process process = _process;
                Task<string[]> response = Task.Run(delegate
                {
                    ShellWorkerProtocol.Write(process.StandardInput.BaseStream, request);
                    return ShellWorkerProtocol.Read(process.StandardOutput.BaseStream);
                });
                try
                {
                    if (!((IAsyncResult)response).AsyncWaitHandle.WaitOne(_timeout))
                    {
                        StopWorker();
                        // Observe pipe failures after terminating a timed-out helper.
                        response.ContinueWith(task => { Exception ignored = task.Exception; },
                            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                        throw new TimeoutException("The Shell worker operation timed out; the helper was stopped.");
                    }
                    string[] result = response.GetAwaiter().GetResult();
                    if (result.Length == 0 || result[0] != "ok")
                        throw new IOException(result.Length > 1 ? result[1] : "The Shell worker returned an invalid response.");
                    string[] values = new string[result.Length - 1];
                    Array.Copy(result, 1, values, 0, values.Length);
                    return values;
                }
                catch
                {
                    StopWorker();
                    throw;
                }
            }
            finally
            {
                Monitor.Exit(_sync);
            }
        }

        internal bool IsStarted
        {
            get { lock (_sync) return _process != null && !_process.HasExited; }
        }

        private void EnsureStarted()
        {
            if (_process != null && !_process.HasExited) return;
            StopWorker();
            ProcessStartInfo startInfo = _createStartInfo();
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;
            _process = Process.Start(startInfo);
            if (_process == null) throw new IOException("Could not start the Shell worker.");
        }

        private void StopWorker()
        {
            if (_process == null) return;
            // Do not replace a helper until its exit is confirmed.
            if (!_process.HasExited)
            {
                _process.Kill();
                if (!_process.WaitForExit(1000)) throw new IOException("The Shell worker did not exit.");
            }
            _process.Dispose();
            _process = null;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
                StopWorker();
            }
        }
    }
}
