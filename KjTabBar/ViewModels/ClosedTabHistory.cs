using System;
using System.Collections.Generic;

namespace KjTabBar.ViewModels
{
    internal sealed class ClosedTabHistory
    {
        internal const int MaxHistoryCount = 255;
        private readonly List<ClosedTabBatch> _history = new List<ClosedTabBatch>();
        private ClosedTabBatch _currentRecordingBatch;

        public bool HasItems
        {
            get { return _history.Count > 0; }
        }

        public void StartBatch()
        {
            _currentRecordingBatch = new ClosedTabBatch();
        }

        public bool EndBatch()
        {
            bool changed = false;
            if (_currentRecordingBatch != null && _currentRecordingBatch.Tabs.Count > 0)
            {
                AddBatch(_currentRecordingBatch);
                changed = true;
            }
            _currentRecordingBatch = null;
            return changed;
        }

        public bool Record(string path, int position)
        {
            if (string.IsNullOrEmpty(path)) return false;

            ClosedTabInfo info = new ClosedTabInfo(path, position);
            if (_currentRecordingBatch != null)
            {
                _currentRecordingBatch.Tabs.Add(info);
                return false;
            }

            ClosedTabBatch batch = new ClosedTabBatch();
            batch.Tabs.Add(info);
            AddBatch(batch);
            return true;
        }

        internal List<ClosedTabInfo> GetRecordedItems()
        {
            List<ClosedTabInfo> items = new List<ClosedTabInfo>();
            foreach (ClosedTabBatch batch in _history) items.AddRange(batch.Tabs);
            return items;
        }
        internal List<List<ClosedTabInfo>> GetBatches()
        {
            List<List<ClosedTabInfo>> batches = new List<List<ClosedTabInfo>>();
            foreach (ClosedTabBatch batch in _history) batches.Add(new List<ClosedTabInfo>(batch.Tabs));
            return batches;
        }

        internal void RestoreBatches(List<List<ClosedTabInfo>> batches)
        {
            _history.Clear();
            foreach (List<ClosedTabInfo> items in batches)
            {
                if (items.Count == 0) continue;
                ClosedTabBatch batch = new ClosedTabBatch();
                batch.Tabs.AddRange(items);
                AddBatch(batch);
            }
        }

        internal bool Contains(ClosedTabInfo item)
        {
            foreach (ClosedTabBatch batch in _history) if (batch.Tabs.Contains(item)) return true;
            return false;
        }

        public List<ClosedTabInfo> PeekLastBatch()
        {
            if (_history.Count == 0) return null;

            int lastIndex = _history.Count - 1;
            ClosedTabBatch batch = _history[lastIndex];
            return new List<ClosedTabInfo>(batch.Tabs);
        }

        public void RemoveRestoredItem(ClosedTabInfo item)
        {
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                ClosedTabBatch batch = _history[i];
                if (!batch.Tabs.Remove(item)) continue;
                if (batch.Tabs.Count == 0) _history.RemoveAt(i);
                return;
            }
        }

        internal void RollbackClose(List<List<ClosedTabInfo>> previousBatches,
            List<ClosedTabInfo> closeRecords, HashSet<ClosedTabInfo> restoredItems)
        {
            // Recover entries evicted by the unconfirmed close without undoing later user operations.
            HashSet<ClosedTabInfo> previousItems = new HashSet<ClosedTabInfo>();
            List<List<ClosedTabInfo>> batches = new List<List<ClosedTabInfo>>();
            foreach (List<ClosedTabInfo> batch in previousBatches)
            {
                previousItems.UnionWith(batch);
                batches.Add(batch.FindAll(item => !restoredItems.Contains(item)));
            }
            foreach (ClosedTabBatch batch in _history)
                batches.Add(batch.Tabs.FindAll(item => !previousItems.Contains(item) && !closeRecords.Contains(item)));
            RestoreBatches(batches);
        }

        private void AddBatch(ClosedTabBatch batch)
        {
            _history.Add(batch);
            int excess = GetRecordedItems().Count - MaxHistoryCount;
            while (excess > 0)
            {
                int removeCount = Math.Min(excess, _history[0].Tabs.Count);
                _history[0].Tabs.RemoveRange(0, removeCount);
                if (_history[0].Tabs.Count == 0) _history.RemoveAt(0);
                excess -= removeCount;
            }
        }
    }

    internal sealed class ClosedTabInfo
    {
        public ClosedTabInfo(string path, int position)
        {
            Path = path;
            Position = position;
        }

        public string Path { get; private set; }
        public int Position { get; private set; }
    }

    internal sealed class ClosedTabBatch
    {
        public List<ClosedTabInfo> Tabs = new List<ClosedTabInfo>();
    }
}