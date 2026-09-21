using System;
using System.Collections.Generic;

namespace KjTabBar.ViewModels
{
    internal sealed class ClosedTabHistory
    {
        private const int MaxHistoryCount = 50;
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

        private void AddBatch(ClosedTabBatch batch)
        {
            _history.Add(batch);
            if (_history.Count > MaxHistoryCount)
            {
                _history.RemoveAt(0);
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