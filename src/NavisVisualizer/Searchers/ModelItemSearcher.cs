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

        public bool NeedsRebuild(Document doc)
        {
            if (!_isBuilt) return true;
            return GetDocumentId(doc) != _lastDocumentId;
        }

        public void BuildIndex(Document doc, Action<int, int> onProgress = null)
        {
            _index = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);
            _isBuilt = false;
            _lastDocumentId = GetDocumentId(doc);

            foreach (var item in doc.Models.RootItemDescendantsAndSelf)
            {
                string displayName = item.DisplayName?.Trim();
                if (string.IsNullOrEmpty(displayName)) continue;

                // Skip unnamed leaf geometry (Pipe, Elbow, etc.)
                if (!item.Children.Any() && !ContainsDigit(displayName))
                    continue;

                string key = displayName.TrimStart('/').Trim();
                if (string.IsNullOrEmpty(key)) continue;
                key = key.ToUpperInvariant();

                AddToIndex(key, item);

                // Also register prefix key (before first '/') for Equipment tag matching
                // e.g., "101210-ZZZ-10310/VENSKID" → also indexed under "101210-ZZZ-10310"
                int slashIdx = key.IndexOf('/');
                if (slashIdx > 0)
                {
                    string prefix = key.Substring(0, slashIdx);
                    AddToIndex(prefix, item);
                }
            }

            _isBuilt = true;
        }

        private void AddToIndex(string key, ModelItem item)
        {
            if (!_index.TryGetValue(key, out var list))
            {
                list = new List<ModelItem>();
                _index[key] = list;
            }
            list.Add(item);
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
        /// Find items by Tag No. — uses exact match via pre-built prefix index.
        /// No prefix loop needed since BuildIndex registers prefix keys automatically.
        /// </summary>
        public Dictionary<string, List<ModelItem>> FindByTagPrefix(IEnumerable<string> tagNos)
        {
            return FindBySpoolIds(tagNos); // Same logic — prefix keys already in index
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
