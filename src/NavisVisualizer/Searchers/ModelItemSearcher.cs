using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;

namespace NavisVisualizer.Searchers
{
    public class ModelItemSearcher
    {
        private Dictionary<string, List<ModelItem>> _index;
        private bool _isBuilt = false;

        public bool IsIndexBuilt => _isBuilt;
        public int IndexedCount => _index?.Count ?? 0;

        public void BuildIndex(Document doc, Action<int, int> onProgress = null)
        {
            _index = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);
            _isBuilt = false;

            // Only index group/container nodes (items with children).
            // Leaf geometry items (Pipe, Elbow, etc.) are not matching targets
            // and will be reached via ExpandWithDescendants.
            int current = 0;
            int progressInterval = 0;

            foreach (var item in doc.Models.RootItemDescendantsAndSelf)
            {
                current++;
                if (onProgress != null && ++progressInterval >= 500)
                {
                    progressInterval = 0;
                    onProgress.Invoke(current, 0); // total unknown, report current count
                }

                // Skip leaf geometry — only index containers
                if (!item.Children.Any()) continue;

                string displayName = item.DisplayName?.Trim();
                if (string.IsNullOrEmpty(displayName)) continue;

                string key = displayName.TrimStart('/').Trim();
                if (string.IsNullOrEmpty(key)) continue;
                key = key.ToUpperInvariant();

                if (!_index.TryGetValue(key, out var list))
                {
                    list = new List<ModelItem>();
                    _index[key] = list;
                }
                list.Add(item);
            }

            onProgress?.Invoke(current, current);
            _isBuilt = true;
        }

        public Dictionary<string, List<ModelItem>> FindBySpoolIds(IEnumerable<string> spoolIds)
        {
            if (!_isBuilt)
                throw new InvalidOperationException("인덱스가 빌드되지 않았습니다. BuildIndex를 먼저 호출하세요.");

            var result = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in spoolIds)
            {
                result[id] = _index.TryGetValue(id, out var items)
                    ? items
                    : new List<ModelItem>();
            }
            return result;
        }

        public void Reset() => _isBuilt = false;
    }
}
