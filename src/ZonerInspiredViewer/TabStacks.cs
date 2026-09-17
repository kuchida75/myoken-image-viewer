using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace ZonerInspiredViewer
{
    [DataContract]
    internal sealed class TabStackDto
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public bool IsCollapsed { get; set; }

        public TabStackDto Copy() { return (TabStackDto)MemberwiseClone(); }
    }

    internal static class TabStacks
    {
        public static bool ValidName(string name)
        {
            return !String.IsNullOrWhiteSpace(name) && name.Length <= 80 && name == name.Trim() && !name.Any(Char.IsControl);
        }

        public static bool ValidId(string id)
        {
            return !String.IsNullOrWhiteSpace(id) && id.Length <= 128 && !id.Any(Char.IsControl);
        }

        public static void Validate(SessionState state)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (state.TabStacks != null)
            {
                if (state.TabStacks.Count > 20000) throw new InvalidDataException("The backup contains too many tab stacks.");
                foreach (TabStackDto stack in state.TabStacks)
                    if (stack == null || !ValidId(stack.Id) || !ValidName(stack.Name) || !ids.Add(stack.Id))
                        throw new InvalidDataException("The backup contains an invalid or duplicate tab stack.");
            }
            foreach (SessionTabDto tab in state.Tabs)
                if (!String.IsNullOrEmpty(tab.StackId) && !ids.Contains(tab.StackId))
                    throw new InvalidDataException("A tab references a missing stack in the backup.");
        }

        public static void RemoveEmpty(SessionState state)
        {
            if (state.TabStacks == null) return;
            var used = new HashSet<string>(state.Tabs.Select(tab => tab.StackId).Where(id => id != null), StringComparer.OrdinalIgnoreCase);
            state.TabStacks.RemoveAll(stack => !used.Contains(stack.Id));
        }
    }
}
