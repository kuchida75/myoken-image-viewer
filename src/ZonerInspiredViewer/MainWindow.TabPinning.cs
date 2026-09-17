using System.Linq;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private void ToggleTabPin(string id)
        {
            ImageTabState state;
            if (id == null || !_tabs.TryGetValue(id, out state)) return;
            SaveWorkspaceState();
            state.IsPinned = !state.IsPinned;
            RefreshTabStrip();
            ScheduleSessionSave();
        }

        private void NormalizeTabOrder()
        {
            string[] order = _tabOrder.Where(id => _tabs.ContainsKey(id)).Distinct(System.StringComparer.OrdinalIgnoreCase).ToArray();
            _tabOrder.Clear();
            _tabOrder.AddRange(order);
            NormalizeTabStacks();
        }

        private bool ReorderTab(string id, int insertionSlot)
        {
            if (_isClosed || _backupBusy || _fileTransferBusy || !_tabs.ContainsKey(id)) return false;
            SaveWorkspaceState();
            if (!ReorderList.Move(_tabOrder, id, insertionSlot)) return false;
            double offset = _tabScroller.HorizontalOffset;
            RefreshTabStrip();
            _tabScroller.ScrollToHorizontalOffset(offset);
            ScheduleSessionSave();
            return true;
        }
    }
}
