using System.Collections.ObjectModel;

namespace NetworkMonitor.Services.Common
{
    public static class CollectionReconciler
    {
        public static void MergeUnordered<TItem, TKey>(
            IList<TItem> existing,
            IReadOnlyList<TItem> fresh,
            Func<TItem, TKey> keySelector,
            Action<TItem, TItem> applyValues)
            where TItem : class
            where TKey : notnull
        {
            Dictionary<TKey, TItem> existingByKey = new();

            foreach (TItem item in existing)
            {
                existingByKey[keySelector(item)] = item;
            }

            HashSet<TKey> freshKeys = new();

            foreach (TItem incoming in fresh)
            {
                TKey key = keySelector(incoming);
                freshKeys.Add(key);

                if (existingByKey.TryGetValue(key, out TItem? match))
                {
                    applyValues(match, incoming);
                }
                else
                {
                    existing.Add(incoming);
                }

            }

            for (int index = existing.Count - 1; index >= 0; index--)
            {

                if (!freshKeys.Contains(keySelector(existing[index])))
                {
                    existing.RemoveAt(index);
                }

            }

        }

        public static void SyncOrdered<TItem, TKey>(
            ObservableCollection<TItem> collection,
            IReadOnlyList<TItem> target,
            Func<TItem, TKey> keySelector,
            Action<TItem, TItem> applyValues)
            where TItem : class
            where TKey : notnull
        {
            Dictionary<TKey, TItem> existingByKey = new();

            foreach (TItem item in collection)
            {
                existingByKey[keySelector(item)] = item;
            }

            List<TItem> resolved = new(target.Count);

            foreach (TItem incoming in target)
            {
                TKey key = keySelector(incoming);

                if (existingByKey.TryGetValue(key, out TItem? match))
                {
                    applyValues(match, incoming);
                    resolved.Add(match);
                }
                else
                {
                    resolved.Add(incoming);
                }

            }

            HashSet<TKey> targetKeys = new();

            foreach (TItem item in resolved)
            {
                targetKeys.Add(keySelector(item));
            }

            for (int index = collection.Count - 1; index >= 0; index--)
            {

                if (!targetKeys.Contains(keySelector(collection[index])))
                {
                    collection.RemoveAt(index);
                }

            }

            int correctlyPlaced = 0;
            int comparableCount = collection.Count < resolved.Count ? collection.Count : resolved.Count;

            for (int index = 0; index < comparableCount; index++)
            {

                if (ReferenceEquals(collection[index], resolved[index]))
                {
                    correctlyPlaced++;
                }

            }

            if (resolved.Count > 8 && correctlyPlaced < resolved.Count / 2)
            {
                collection.Clear();

                foreach (TItem item in resolved)
                {
                    collection.Add(item);
                }

                return;
            }

            for (int index = 0; index < resolved.Count; index++)
            {
                TItem desired = resolved[index];

                if (index >= collection.Count)
                {
                    collection.Add(desired);
                }
                else if (!ReferenceEquals(collection[index], desired))
                {
                    int currentIndex = index + 1;

                    while (currentIndex < collection.Count && !ReferenceEquals(collection[currentIndex], desired))
                    {
                        currentIndex++;
                    }

                    if (currentIndex < collection.Count)
                    {
                        collection.Move(currentIndex, index);
                    }
                    else
                    {
                        collection.Insert(index, desired);
                    }

                }

            }

        }
    }
}
