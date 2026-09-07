using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KjTabBar.Helpers;

namespace KjTabBar.Services
{
    internal sealed class ShellMetadataCache
    {
        private sealed class Entry
        {
            internal string Value;
            internal DateTime UpdatedUtc;
        }
        private readonly object _sync = new object();
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _order = new Queue<string>();
        internal event EventHandler Updated;

        internal string Get(string key, Func<string> load, bool background, string fallback)
        {
            lock (_sync)
            {
                Entry entry;
                if (_entries.TryGetValue(key, out entry))
                {
                    if (DateTime.UtcNow - entry.UpdatedUtc < TimeSpan.FromSeconds(30)) return entry.Value;
                    fallback = entry.Value;
                }
                if (background)
                {
                    if (_pending.Count < 64 && _pending.Add(key)) RefreshAsync(key, load);
                    return fallback;
                }
            }
            string value = load();
            Store(key, value);
            return value;
        }

        private async void RefreshAsync(string key, Func<string> load)
        {
            try
            {
                string value = await ComThreadService.Instance.InvokeAsync(load);
                Store(key, value);
                EventHandler handler = Updated;
                if (handler != null) handler(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                AppLogger.LogErrorThrottled("ShellMetadataCache", "Refresh", "Failed to refresh Shell metadata.", ex, TimeSpan.FromMinutes(1));
            }
            finally
            {
                lock (_sync) _pending.Remove(key);
            }
        }

        private void Store(string key, string value)
        {
            lock (_sync)
            {
                if (!_entries.ContainsKey(key))
                {
                    while (_entries.Count >= 256) _entries.Remove(_order.Dequeue());
                    _order.Enqueue(key);
                }
                _entries[key] = new Entry { Value = value, UpdatedUtc = DateTime.UtcNow };
            }
        }
    }
}
