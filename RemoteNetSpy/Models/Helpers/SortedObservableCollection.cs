using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public class SortedObservableCollection<T> : ObservableCollection<T>
{
    private readonly Comparison<T> _comparison;

    public SortedObservableCollection(Comparison<T> comparison)
    {
        _comparison = comparison ?? throw new ArgumentNullException(nameof(comparison));
    }

    protected override void InsertItem(int index, T item)
    {
        // Find the correct position for insertion to keep it sorted
        int insertIndex = 0;
        while (insertIndex < Count && _comparison(this[insertIndex], item) < 0)
        {
            insertIndex++;
        }

        base.InsertItem(insertIndex, item);
    }

    protected override void SetItem(int index, T item)
    {
        base.SetItem(index, item);
        // Ensure sorting is maintained
        Sort();
    }

    private void Sort()
    {
        var sortedItems = new List<T>(this);
        sortedItems.Sort(_comparison);

        for (int i = 0; i < sortedItems.Count; i++)
        {
            Move(IndexOf(sortedItems[i]), i);
        }
    }
}
