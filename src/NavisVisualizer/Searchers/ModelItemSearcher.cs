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

        public void BuildIndex(Document doc, Action<int, int> onProgress = null)
        {
            _index = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);
            _isBuilt = false;

            var allItems = doc.Models.RootItemDescendantsAndSelf.ToList();
            int total = allItems.Count;
            int current = 0;

            foreach (var item in allItems)
            {
                current++;
                onProgress?.Invoke(current, total);

                string spoolId = ExtractSpoolId(item);
                if (string.IsNullOrEmpty(spoolId)) continue;

                if (!_index.TryGetValue(spoolId, out var list))
                {
                    list = new List<ModelItem>();
                    _index[spoolId] = list;
                }
                list.Add(item);
            }

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

        public List<ModelItem> GetUnmatchedItems(Document doc, HashSet<ModelItem> matchedItems)
        {
            return doc.Models.RootItemDescendantsAndSelf
                .Where(item => !matchedItems.Contains(item))
                .ToList();
        }

        private string ExtractSpoolId(ModelItem item)
        {
            string displayName = item.DisplayName?.Trim();
            if (!string.IsNullOrEmpty(displayName))
            {
                string normalized = displayName.TrimStart('/').Trim();
                if (!string.IsNullOrEmpty(normalized))
                    return normalized.ToUpperInvariant();
            }

            return null;
        }

        public void Reset() => _isBuilt = false;
    }
}
