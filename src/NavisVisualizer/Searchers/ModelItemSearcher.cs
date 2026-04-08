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

                string displayName = item.DisplayName?.Trim();
                if (string.IsNullOrEmpty(displayName)) continue;

                // Skip unnamed leaf geometry (Pipe, Elbow, etc.)
                // but keep leaf nodes with tag-like names (contain digits)
                if (!item.Children.Any() && !ContainsDigit(displayName))
                    continue;

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

        /// <summary>
        /// Find items by Tag No. with prefix matching support.
        /// Exact match first; if not found, searches for keys starting with the tag.
        /// Returns only the first (shallowest) match per tag — skips children.
        /// </summary>
        public Dictionary<string, List<ModelItem>> FindByTagPrefix(IEnumerable<string> tagNos)
        {
            if (!_isBuilt)
                throw new InvalidOperationException("인덱스가 빌드되지 않았습니다.");

            var result = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in tagNos)
            {
                string key = tag.Trim().TrimStart('/').ToUpperInvariant();

                // Exact match
                if (_index.TryGetValue(key, out var exactItems))
                {
                    result[tag] = exactItems;
                    continue;
                }

                // Prefix match: find keys that start with the tag
                // e.g., tag "101210-PBA-10240" matches "101210-PBA-10240/VENSKID"
                var prefixMatched = new List<ModelItem>();
                foreach (var kv in _index)
                {
                    if (kv.Key.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                        prefixMatched.AddRange(kv.Value);
                }
                result[tag] = prefixMatched;
            }
            return result;
        }

        public void Reset()
        {
            _isBuilt = false;
            _lastDocumentId = null;
        }

        private static bool ContainsDigit(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (char.IsDigit(s[i])) return true;
            return false;
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
