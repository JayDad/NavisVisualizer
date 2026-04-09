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

            // Recursive walk — stops descending into children of tag-like nodes
            foreach (var model in doc.Models)
            {
                WalkAndIndex(model.RootItem);
            }

            _isBuilt = true;
        }

        private void WalkAndIndex(ModelItem item)
        {
            string displayName = item.DisplayName?.Trim();
            bool indexed = false;

            if (!string.IsNullOrEmpty(displayName) && ContainsDigit(displayName))
            {
                string key = displayName.TrimStart('/').Trim();
                if (!string.IsNullOrEmpty(key))
                {
                    key = key.ToUpperInvariant();
                    AddToIndex(key, item);
                    indexed = true;

                    // Prefix key for Equipment tag matching (before first '/')
                    int slashIdx = key.IndexOf('/');
                    if (slashIdx > 0)
                        AddToIndex(key.Substring(0, slashIdx), item);
                }
            }

            if (indexed)
            {
                // Tag-like node found — index direct children (for sub-tags like /VENSKID)
                // but do NOT recurse deeper (skip geometry)
                foreach (var child in item.Children)
                {
                    string childName = child.DisplayName?.Trim();
                    if (!string.IsNullOrEmpty(childName) && ContainsDigit(childName))
                    {
                        string childKey = childName.TrimStart('/').Trim();
                        if (!string.IsNullOrEmpty(childKey))
                        {
                            childKey = childKey.ToUpperInvariant();
                            AddToIndex(childKey, child);

                            int slashIdx = childKey.IndexOf('/');
                            if (slashIdx > 0)
                                AddToIndex(childKey.Substring(0, slashIdx), child);
                        }
                    }
                }
                return; // STOP — don't recurse into geometry children
            }

            // Not a tag node — keep recursing
            foreach (var child in item.Children)
                WalkAndIndex(child);
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

        public Dictionary<string, List<ModelItem>> FindByTagPrefix(IEnumerable<string> tagNos)
        {
            return FindBySpoolIds(tagNos);
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
