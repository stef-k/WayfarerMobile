using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace WayfarerMobile.Shared.Collections;

/// <summary>
/// An ObservableCollection that supports batch operations without triggering
/// notifications for each individual item.
/// </summary>
/// <typeparam name="T">The type of elements in the collection.</typeparam>
public class ObservableRangeCollection<T> : ObservableCollection<T>
{
    /// <summary>
    /// Creates a new empty ObservableRangeCollection.
    /// </summary>
    public ObservableRangeCollection() : base()
    {
    }

    /// <summary>
    /// Creates a new ObservableRangeCollection with initial items.
    /// </summary>
    /// <param name="items">The items to populate the collection with.</param>
    public ObservableRangeCollection(IEnumerable<T> items) : base(items)
    {
    }

    /// <summary>
    /// Adds a range of items to the collection, triggering only a single Reset notification.
    /// </summary>
    /// <param name="items">The items to add.</param>
    /// <exception cref="ArgumentNullException">Thrown when items is null.</exception>
    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var itemList = items as IList<T> ?? items.ToList();
        if (itemList.Count == 0)
            return;

        CheckReentrancy();

        foreach (var item in itemList)
        {
            Items.Add(item);
        }

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
    }

    /// <summary>
    /// Clears the collection and adds the specified items, triggering only a single Reset notification.
    /// </summary>
    /// <param name="items">The items to replace the collection with.</param>
    /// <exception cref="ArgumentNullException">Thrown when items is null.</exception>
    public void ReplaceRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        CheckReentrancy();

        Items.Clear();

        var itemList = items as IList<T> ?? items.ToList();
        foreach (var item in itemList)
        {
            Items.Add(item);
        }

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
    }

    /// <summary>
    /// Removes a range of items from the collection, triggering only a single Reset notification.
    /// </summary>
    /// <param name="items">The items to remove.</param>
    /// <exception cref="ArgumentNullException">Thrown when items is null.</exception>
    public void RemoveRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var itemList = items as IList<T> ?? items.ToList();
        if (itemList.Count == 0)
            return;

        CheckReentrancy();

        var removed = false;
        foreach (var item in itemList)
        {
            if (Items.Remove(item))
            {
                removed = true;
            }
        }

        if (removed)
        {
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        }
    }
}
