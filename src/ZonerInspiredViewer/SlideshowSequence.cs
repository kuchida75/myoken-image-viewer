using System;
using System.Collections.Generic;
using System.Linq;

namespace ZonerInspiredViewer
{
    internal sealed class SlideshowSequence
    {
        private readonly Random _random;
        private readonly List<string> _source;
        private List<string> _cycle;
        private List<string> _nextCycle;
        private List<string> _previousCycle;
        private int _index;
        private readonly bool _shuffle;

        public SlideshowSequence(IEnumerable<string> paths, bool shuffle, string current, Random random = null)
        {
            _random = random ?? new Random();
            _shuffle = shuffle;
            _source = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            StartCycle(null);
            int currentIndex = _cycle.FindIndex(p => String.Equals(p, current, StringComparison.OrdinalIgnoreCase));
            if (currentIndex >= 0)
            {
                if (shuffle) { string first = _cycle[0]; _cycle[0] = _cycle[currentIndex]; _cycle[currentIndex] = first; }
                else _index = currentIndex;
            }
        }

        public int Count { get { return _cycle.Count; } }
        public int Position { get { return Count == 0 ? 0 : _index + 1; } }
        public string Current { get { return Count == 0 ? null : _cycle[_index]; } }

        public string Next()
        {
            string previous = Current;
            if (Count == 0) return null;
            _index++;
            if (_index >= Count) StartCycle(previous);
            return Current;
        }

        public IEnumerable<string> Nearby(int radius)
        {
            if (Count == 0) yield break;
            for (int offset = -Math.Min(radius, Count - 1); offset <= Math.Min(radius, Count); offset++)
            {
                int index = _index + offset;
                if (index < 0 && _previousCycle != null) { yield return _previousCycle[index + Count]; continue; }
                if (index < 0) index += Count;
                if (index < Count) yield return _cycle[index];
                else
                {
                    if (_nextCycle == null) _nextCycle = CreateCycle(_cycle[Count - 1]);
                    yield return _nextCycle[index - Count];
                }
            }
        }

        private void StartCycle(string previous)
        {
            _previousCycle = _cycle;
            _cycle = _nextCycle ?? CreateCycle(previous);
            _nextCycle = null;
            _index = 0;
        }

        public string Previous()
        {
            if (Count == 0) return null;
            if (_index > 0) _index--;
            else
            {
                if (_previousCycle != null)
                {
                    _nextCycle = _cycle; _cycle = _previousCycle; _previousCycle = null;
                }
                else _nextCycle = _cycle;
                _index = Count - 1;
            }
            return Current;
        }

        private List<string> CreateCycle(string previous)
        {
            var cycle = new List<string>(_source);
            if (!_shuffle) return cycle;
            for (int i = cycle.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                string item = cycle[i]; cycle[i] = cycle[j]; cycle[j] = item;
            }
            if (cycle.Count > 1 && String.Equals(cycle[0], previous, StringComparison.OrdinalIgnoreCase))
            {
                string item = cycle[0]; cycle[0] = cycle[1]; cycle[1] = item;
            }
            return cycle;
        }
    }
}
