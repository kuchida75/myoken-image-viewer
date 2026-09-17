using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private readonly List<TabStackDto> _tabStacks = new List<TabStackDto>();
        private const string StackDragPrefix = "stack:";

        private TabStackDto FindTabStack(string id)
        {
            return id == null ? null : _tabStacks.FirstOrDefault(stack => String.Equals(stack.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private List<string> StackMembers(string id)
        {
            return _tabOrder.Where(key => _tabs.ContainsKey(key) && String.Equals(_tabs[key].StackId, id, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private void NormalizeTabStacks()
        {
            var valid = new HashSet<string>(_tabStacks.Select(stack => stack.Id), StringComparer.OrdinalIgnoreCase);
            foreach (ImageTabState tab in _tabs.Values)
                if (tab.StackId != null && !valid.Contains(tab.StackId)) tab.StackId = null;
            var groups = _tabOrder.Where(id => _tabs[id].StackId != null)
                .GroupBy(id => _tabs[id].StackId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
            _tabStacks.RemoveAll(stack => !groups.ContainsKey(stack.Id));
            // A stack is one contiguous block; its first member determines the block's position.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (string id in _tabOrder)
            {
                string stack = _tabs[id].StackId;
                if (stack == null) order.Add(id);
                else if (seen.Add(stack)) order.AddRange(groups[stack]);
            }
            _tabOrder.Clear(); _tabOrder.AddRange(order);
        }

        private void RestoreTabStacks(SessionState state)
        {
            _tabStacks.Clear();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TabStackDto stack in (state.TabStacks ?? new List<TabStackDto>()).Take(20000))
                if (stack != null && TabStacks.ValidId(stack.Id) && TabStacks.ValidName(stack.Name) && ids.Add(stack.Id))
                    _tabStacks.Add(stack.Copy());
            NormalizeTabOrder();
        }

        private bool CanChangeStacks { get { return !_isClosed && !_backupBusy && !_fileTransferBusy; } }

        private void EditTabStack(string stackId, string selectedId)
        {
            if (!CanChangeStacks) return;
            TabStackDto stack = FindTabStack(stackId);
            var choices = _tabOrder.Select(id => new StackTabChoice
            {
                Id = id, Label = TabLabel(_tabs[id]), Path = _tabs[id].IsBrowser ? _tabs[id].FolderPath : _tabs[id].Path,
                Selected = stack == null ? id == selectedId : String.Equals(_tabs[id].StackId, stack.Id, StringComparison.OrdinalIgnoreCase)
            }).ToList();
            var dialog = new TabStackWindow(stack == null ? "New stack" : stack.Name, choices, stack == null) { Owner = this };
            if (dialog.ShowDialog() == true) SetTabStack(stackId, dialog.StackName, dialog.SelectedIds);
        }

        private string SetTabStack(string stackId, string name, IEnumerable<string> selectedIds)
        {
            if (!CanChangeStacks || !TabStacks.ValidName(name)) return null;
            var selected = new HashSet<string>(selectedIds.Where(id => id != null && _tabs.ContainsKey(id)), StringComparer.OrdinalIgnoreCase);
            if (selected.Count == 0) return null;
            SaveWorkspaceState();
            TabStackDto stack = FindTabStack(stackId);
            if (stack == null)
            {
                stack = new TabStackDto { Id = Guid.NewGuid().ToString("N"), Name = name };
                _tabStacks.Add(stack);
            }
            stack.Name = name;
            foreach (string id in _tabOrder)
            {
                if (selected.Contains(id)) _tabs[id].StackId = stack.Id;
                else if (String.Equals(_tabs[id].StackId, stack.Id, StringComparison.OrdinalIgnoreCase)) _tabs[id].StackId = null;
            }
            FinishStackChange();
            return stack.Id;
        }

        private void MoveTabToStack(string tabId, string stackId)
        {
            ImageTabState tab;
            if (!CanChangeStacks || !_tabs.TryGetValue(tabId, out tab) || (stackId != null && FindTabStack(stackId) == null)) return;
            SaveWorkspaceState();
            string old = tab.StackId;
            if (String.Equals(old, stackId, StringComparison.OrdinalIgnoreCase)) return;
            List<string> destination = stackId == null ? StackMembers(old) : StackMembers(stackId);
            _tabOrder.Remove(tabId);
            string last = destination.LastOrDefault(id => id != tabId);
            _tabOrder.Insert(last == null ? _tabOrder.Count : _tabOrder.IndexOf(last) + 1, tabId);
            tab.StackId = stackId;
            FinishStackChange();
        }

        private void UnstackTabs(string stackId)
        {
            if (!CanChangeStacks || FindTabStack(stackId) == null) return;
            SaveWorkspaceState();
            foreach (string id in StackMembers(stackId)) _tabs[id].StackId = null;
            _tabStacks.RemoveAll(stack => String.Equals(stack.Id, stackId, StringComparison.OrdinalIgnoreCase));
            FinishStackChange();
        }

        private void RenameTabStack(string stackId, string name)
        {
            TabStackDto stack = FindTabStack(stackId);
            if (!CanChangeStacks || stack == null || !TabStacks.ValidName(name)) return;
            stack.Name = name; FinishStackChange();
        }

        private void ToggleTabStack(string stackId)
        {
            TabStackDto stack = FindTabStack(stackId);
            if (!CanChangeStacks || stack == null) return;
            stack.IsCollapsed = !stack.IsCollapsed; FinishStackChange();
        }

        private void FinishStackChange()
        {
            double offset = _tabScroller.HorizontalOffset;
            RefreshTabStrip(); _tabScroller.ScrollToHorizontalOffset(offset);
            RevealActiveTab(); ScheduleSessionSave();
        }

        private void AddTabStackCommands(ContextMenu menu, string tabId)
        {
            var group = new MenuItem { Header = "Tab stack", Tag = "tab-stack" };
            menu.Items.Add(group);
            Action populate = delegate
            {
                group.Items.Clear();
                ImageTabState tab;
                group.IsEnabled = CanChangeStacks && _tabs.TryGetValue(tabId, out tab);
                if (!group.IsEnabled || !_tabs.TryGetValue(tabId, out tab)) return;
                var create = new MenuItem { Header = "New stack...", Tag = "new-stack" };
                create.Click += delegate { EditTabStack(null, tabId); };
                group.Items.Add(create);
                var move = new MenuItem { Header = "Move to stack", IsEnabled = _tabStacks.Count > 0 };
                foreach (TabStackDto stack in _tabStacks)
                {
                    string id = stack.Id;
                    var item = new MenuItem { Header = new TextBlock { Text = stack.Name }, IsChecked = stack.Id == tab.StackId, Tag = id };
                    item.Click += delegate { MoveTabToStack(tabId, id); };
                    move.Items.Add(item);
                }
                group.Items.Add(move);
                if (tab.StackId != null)
                {
                    string id = tab.StackId;
                    var manage = new MenuItem { Header = "Manage stack..." };
                    manage.Click += delegate { EditTabStack(id, null); }; group.Items.Add(manage);
                    var remove = new MenuItem { Header = "Remove tab from stack", Tag = "remove-from-stack" };
                    remove.Click += delegate { MoveTabToStack(tabId, null); }; group.Items.Add(remove);
                    var unstack = new MenuItem { Header = "Unstack all tabs" };
                    unstack.Click += delegate { UnstackTabs(id); }; group.Items.Add(unstack);
                }
            };
            populate(); menu.Opened += delegate { populate(); };
        }

        private ContextMenu BuildStackContextMenu(string id)
        {
            var menu = new ContextMenu();
            menu.Opened += delegate
            {
                menu.Items.Clear();
                TabStackDto stack = FindTabStack(id);
                if (stack == null) return;
                var toggle = new MenuItem { Header = stack.IsCollapsed ? "Expand stack" : "Collapse stack" };
                toggle.Click += delegate { ToggleTabStack(id); }; menu.Items.Add(toggle);
                var rename = new MenuItem { Header = "Rename stack...", Tag = "rename-stack" };
                rename.Click += delegate
                {
                    string name = TextPromptWindow.Prompt(this, "Rename tab stack", "Stack name", stack.Name);
                    if (name == null) return;
                    name = name.Trim();
                    if (!TabStacks.ValidName(name)) MessageBox.Show(this, "Use a stack name of 1-80 characters without control characters.", "Rename tab stack", MessageBoxButton.OK, MessageBoxImage.Information);
                    else RenameTabStack(id, name);
                };
                menu.Items.Add(rename);
                var manage = new MenuItem { Header = "Manage tabs..." };
                manage.Click += delegate { EditTabStack(id, null); }; menu.Items.Add(manage);
                var unstack = new MenuItem { Header = "Unstack all tabs", Tag = "unstack-tabs" };
                unstack.Click += delegate { UnstackTabs(id); }; menu.Items.Add(unstack);
                menu.Items.Add(new Separator());
                foreach (string member in StackMembers(id))
                {
                    string key = member;
                    ImageTabState tab = _tabs[key];
                    var item = new MenuItem { Header = new TextBlock { Text = TabLabel(tab) }, IsChecked = key == _activeTabId,
                        ToolTip = tab.IsBrowser ? tab.FolderPath : tab.Path, Tag = key };
                    item.Click += delegate { ActivateImageTab(key); }; menu.Items.Add(item);
                }
            };
            return menu;
        }

        private Border CreateStackHeader(TabStackDto stack)
        {
            List<string> members = StackMembers(stack.Id);
            bool active = members.Contains(_activeTabId, StringComparer.OrdinalIgnoreCase);
            int color = ThemeManager.TabColorIndex(stack.Id);
            var border = new Border { Tag = stack, Height = 34, Width = 190, Margin = new Thickness(4, 4, 4, 4),
                BorderThickness = new Thickness(1, active ? 4 : 2, 1, 1), CornerRadius = new CornerRadius(3, 3, 0, 0) };
            ThemeManager.Bind(border, Border.BackgroundProperty, "Tab." + (active ? "Active." : "Fill.") + color);
            ThemeManager.Bind(border, Border.BorderBrushProperty, "Tab.Accent." + color);
            border.ToolTip = stack.Name + " (" + members.Count + " tabs)" + (active ? "\nActive: " + TabLabel(_tabs[_activeTabId]) : "");
            AutomationProperties.SetName(border, "Tab stack " + stack.Name + ", " + members.Count + " tabs");
            border.ContextMenu = BuildStackContextMenu(stack.Id);
            var dock = new DockPanel();
            var toggle = new Button { Width = 26, Height = 24, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Content = new TextBlock { Text = stack.IsCollapsed ? "\uE76C" : "\uE70D", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10 } };
            CommandPresentation.Describe(toggle, stack.IsCollapsed ? "Expand stack" : "Collapse stack", null, stack.Name);
            toggle.Click += delegate { ToggleTabStack(stack.Id); };
            DockPanel.SetDock(toggle, Dock.Left); dock.Children.Add(toggle);
            var menu = new Button { Width = 26, Height = 24, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Content = new TextBlock { Text = "\uE712", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12 } };
            CommandPresentation.Describe(menu, "Stack options", null, "Switch tabs, rename, manage or unstack " + stack.Name + ".");
            menu.Click += delegate { border.ContextMenu.PlacementTarget = menu; border.ContextMenu.IsOpen = true; };
            DockPanel.SetDock(menu, Dock.Right); dock.Children.Add(menu);
            var count = new TextBlock { Text = members.Count.ToString(), Margin = new Thickness(6, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(count, Dock.Right); dock.Children.Add(count);
            dock.Children.Add(new TextBlock { Text = stack.Name, TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            border.Child = dock;
            _tabReorder.Bind(border, StackDragPrefix + stack.Id, delegate { ToggleTabStack(stack.Id); });
            return border;
        }

        private IList<ReorderItem> TabReorderItems()
        {
            return _tabStrip.Children.OfType<Border>().Select(header => new ReorderItem
            {
                Key = header.Tag is TabStackDto ? StackDragPrefix + ((TabStackDto)header.Tag).Id : (string)header.Tag, Element = header
            }).ToList();
        }

        private bool ReorderVisibleTab(string key, int slot)
        {
            if (!CanChangeStacks) return false;
            IList<ReorderItem> visible = TabReorderItems();
            if (slot < 0 || slot > visible.Count) return false;
            if (_tabStacks.Count == 0) return ReorderTab(key, slot);
            string target = slot < visible.Count ? visible[slot].Key : null;
            TabStackDto movingStack = key.StartsWith(StackDragPrefix, StringComparison.Ordinal) ? FindTabStack(key.Substring(StackDragPrefix.Length)) : null;
            TabStackDto targetStack = target != null && target.StartsWith(StackDragPrefix, StringComparison.Ordinal) ? FindTabStack(target.Substring(StackDragPrefix.Length)) : null;
            string targetId = targetStack != null ? StackMembers(targetStack.Id).FirstOrDefault() : target;
            if (movingStack != null)
            {
                List<string> members = StackMembers(movingStack.Id);
                if (members.Contains(targetId)) return false;
                if (targetId != null && _tabs[targetId].StackId != null) targetId = StackMembers(_tabs[targetId].StackId).First();
                string[] before = _tabOrder.ToArray();
                _tabOrder.RemoveAll(id => members.Contains(id));
                _tabOrder.InsertRange(targetId == null ? _tabOrder.Count : _tabOrder.IndexOf(targetId), members);
                if (before.SequenceEqual(_tabOrder)) return false;
            }
            else
            {
                ImageTabState tab;
                if (!_tabs.TryGetValue(key, out tab) || targetId == key) return false;
                string destinationStack = targetStack == null && targetId != null ? _tabs[targetId].StackId : null;
                bool membershipChanged = !String.Equals(tab.StackId, destinationStack, StringComparison.OrdinalIgnoreCase);
                int insertion = targetId == null ? _tabOrder.Count : _tabOrder.IndexOf(targetId);
                bool moved = ReorderList.Move(_tabOrder, key, insertion);
                if (!moved && !membershipChanged) return false;
                tab.StackId = destinationStack;
            }
            SaveWorkspaceState(); FinishStackChange(); return true;
        }
    }
}
