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
        private string _lastDocumentId;

        public bool IsIndexBuilt => _isBuilt;
        public int IndexedCount => _index?.Count ?? 0;

        /// <summary>
        /// Check if index needs rebuild (model changed or never built).
        /// </summary>
        public bool NeedsRebuild(Document doc)
        {
            if (!_isBuilt) return true;
            string currentId = GetDocumentId(doc);
            return currentId != _lastDocumentId;
        }

        public void BuildIndex(Document doc, Action<int, int> onProgress = null)
        {
            _index = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);
            _isBuilt = false;
            _lastDocumentId = GetDocumentId(doc);

            int current = 0;

            foreach (var item in doc.Models.RootItemDescendantsAndSelf)
            {
                current++;

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

        public void Reset()
        {
            _isBuilt = false;
            _lastDocumentId = null;
        }

        private string GetDocumentId(Document doc)
        {
            // Use file path + model count as a simple identity check
            try
            {
                string path = doc.FileName ?? "";
                int modelCount = doc.Models.Count;
                return $"{path}|{modelCount}";
            }
            catch
            {
                return Guid.NewGuid().ToString();
            }
        }
    }
}
