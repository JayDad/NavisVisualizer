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

        /// <summary>
        /// General BuildIndex — recursive walk, stops when children have no tags.
        /// Used by Spool/Hydrotest.
        /// </summary>
        public void BuildIndex(Document doc, Action<int, int> onProgress = null)
        {
            _index = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);
            _isBuilt = false;
            _lastDocumentId = GetDocumentId(doc);

            foreach (var model in doc.Models)
                WalkAndIndex(model.RootItem);

            _isBuilt = true;
        }

        /// <summary>
        /// Level-targeted BuildIndex — finds the tree level where known tags exist,
        /// then indexes ONLY that level. Much faster for Equipment models.
        /// </summary>
        public void BuildIndexForTags(Document doc, HashSet<string> knownTags)
        {
            _index = new Dictionary<string, List<ModelItem>>(StringComparer.OrdinalIgnoreCase);
            _isBuilt = false;
            _lastDocumentId = GetDocumentId(doc);

            // Normalize tags for comparison
            var normalizedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in knownTags)
            {
                string t = tag.Trim().TrimStart('/').ToUpperInvariant();
                if (!string.IsNullOrEmpty(t))
                    normalizedTags.Add(t);
            }

            // Step 1: Find the depth where first tag match occurs
            int targetDepth = -1;
            foreach (var model in doc.Models)
            {
                targetDepth = FindTagDepth(model.RootItem, normalizedTags, 0);
                if (targetDepth >= 0) break;
            }

            if (targetDepth < 0)
            {
                // No tags found — fallback to general index
                foreach (var model in doc.Models)
                    WalkAndIndex(model.RootItem);
            }
            else
            {
                // Step 2: Index only at the target depth
                foreach (var model in doc.Models)
                    IndexAtDepth(model.RootItem, 0, targetDepth);
            }

            _isBuilt = true;
        }

        /// <summary>
        /// Walk tree to find the depth where a known tag first appears.
        /// </summary>
        private int FindTagDepth(ModelItem item, HashSet<string> tags, int depth)
        {
            string name = item.DisplayName?.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                string key = name.TrimStart('/').Trim().ToUpperInvariant();
                // Check exact match or prefix match (tag/VENSKID → tag)
                if (tags.Contains(key))
                    return depth;
                int slash = key.IndexOf('/');
                if (slash > 0 && tags.Contains(key.Substring(0, slash)))
                    return depth;
            }

            // Recurse into children (but limit depth to avoid going too deep)
            if (depth > 20) return -1;

            foreach (var child in item.Children)
            {
                int found = FindTagDepth(child, tags, depth + 1);
                if (found >= 0) return found;
            }

            return -1;
        }

        /// <summary>
        /// Index all nodes at the target depth only.
        /// </summary>
        private void IndexAtDepth(ModelItem item, int currentDepth, int targetDepth)
        {
            if (currentDepth == targetDepth)
            {
                // Index this node
                string name = item.DisplayName?.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    string key = name.TrimStart('/').Trim();
                    if (!string.IsNullOrEmpty(key))
                    {
                        key = key.ToUpperInvariant();
                        AddToIndex(key, item);

                        int slash = key.IndexOf('/');
                        if (slash > 0)
                            AddToIndex(key.Substring(0, slash), item);
                    }
                }
                return; // Don't go deeper
            }

            // Haven't reached target depth yet — keep going
            foreach (var child in item.Children)
                IndexAtDepth(child, currentDepth + 1, targetDepth);
        }

        private void WalkAndIndex(ModelItem item)
        {
            string name = item.DisplayName?.Trim();
            bool isTagLike = !string.IsNullOrEmpty(name) && ContainsDigit(name);

            if (isTagLike)
            {
                string key = name.TrimStart('/').Trim();
                if (!string.IsNullOrEmpty(key))
                {
                    key = key.ToUpperInvariant();
                    AddToIndex(key, item);

                    int slash = key.IndexOf('/');
                    if (slash > 0)
                        AddToIndex(key.Substring(0, slash), item);
                }

                bool hasTagChild = false;
                foreach (var child in item.Children)
                {
                    string childName = child.DisplayName?.Trim();
                    if (!string.IsNullOrEmpty(childName) && ContainsDigit(childName))
                    {
                        hasTagChild = true;
                        break;
                    }
                }

                if (!hasTagChild)
                    return;
            }

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
                throw new InvalidOperationException("인덱스가 빌드되지 않았습니다.");

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
