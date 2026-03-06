using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;

namespace AeonHacs;

/// <summary>
/// A sorted collection that automatically monitors items for property changes,
/// repositions them if necessary, and fires CollectionChanged notifications.
/// Encapsulates the collection to prevent manual index insertions/moves 
/// from breaking the sorted invariant.
/// </summary>
public class SortedObservableList<T> : ICollection<T>, IReadOnlyList<T>, INotifyCollectionChanged, INotifyPropertyChanged
{
    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly List<T> items = [];
    private readonly IComparer<T> comparer;

    public SortedObservableList() : this(comparer: null) { }

    public SortedObservableList(IComparer<T>? comparer)
    {
        this.comparer = comparer ?? Comparer<T>.Default;
    }

    public int Count => items.Count;

    public bool IsReadOnly => false;

    public T this[int index] => items[index];

    public void Add(T item)
    {
        int index = items.BinarySearch(item, comparer);
        if (index < 0) index = ~index;

        items.Insert(index, item);

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged("Item[]");
    }

    public bool Remove(T item)
    {
        int index = items.IndexOf(item);
        if (index < 0) return false;

        items.RemoveAt(index);

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged("Item[]");

        return true;
    }

    public void Clear()
    {
        if (items.Count == 0) return;

        items.Clear();

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged("Item[]");
    }

    public bool Contains(T item) => items.Contains(item);

    public void CopyTo(T[] array, int arrayIndex) => items.CopyTo(array, arrayIndex);

    public IEnumerator<T> GetEnumerator() => items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();

    public int IndexOf(T item) => items.IndexOf(item);

    private void OnCollectionChanged(NotifyCollectionChangedEventArgs e) => CollectionChanged?.Invoke(this, e);
    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
