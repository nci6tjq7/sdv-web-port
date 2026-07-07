using System;
using System.Collections;
using System.Collections.Generic;

namespace SdvWebPort.Vfs.CollectionReplacements;

/// <summary>
/// Replacement collection types for types that the BlazorWebAssembly trimmer
/// strips from System.Collections.wasm (even though they're defined there in
/// the source DLL, the trimmed wasm version removes them).
///
/// SDV's Game1 has fields like Stack<T>, SortedSet<T>, etc. that can't resolve
/// at runtime. We define equivalent types here, and the Cecil rewriter rewrites
/// SDV's typerefs to point at these instead.
///
/// These types are simple wrappers that delegate to the underlying collection.
/// They're not fully compatible with the original types, but they allow SDV's
/// type loading to succeed (which is the Phase 2.8 goal — get past the ctor).
/// </summary>
public static class CollectionReplacements
{
    // Marker — Cecil rewriter identifies replacement types via namespace
}

// Replacement for System.Collections.Generic.Stack<T>
// The original is in System.Collections but stripped from the trimmed wasm.
public class Stack<T> : IEnumerable<T>, System.Collections.ICollection
{
    private readonly List<T> _items = new();

    public int Count => _items.Count;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public void Push(T item) { _items.Add(item); }
    public T Pop()
    {
        if (_items.Count == 0) throw new InvalidOperationException("Stack is empty");
        var item = _items[_items.Count - 1];
        _items.RemoveAt(_items.Count - 1);
        return item;
    }
    public T Peek()
    {
        if (_items.Count == 0) throw new InvalidOperationException("Stack is empty");
        return _items[_items.Count - 1];
    }
    public bool Contains(T item) => _items.Contains(item);
    public void Clear() { _items.Clear(); }
    public T[] ToArray() { _items.Reverse(); return _items.ToArray(); }
    public void CopyTo(T[] array, int arrayIndex)
    {
        if (array == null) throw new ArgumentNullException(nameof(array));
        for (int i = 0; i < _items.Count; i++)
            array[arrayIndex + i] = _items[_items.Count - 1 - i];
    }
    void System.Collections.ICollection.CopyTo(Array array, int index)
    {
        CopyTo((T[])array, index);
    }
    public IEnumerator<T> GetEnumerator()
    {
        for (int i = _items.Count - 1; i >= 0; i--)
            yield return _items[i];
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

// Replacement for System.Collections.Generic.SortedSet<T>
public class SortedSet<T> : ICollection<T>, System.Collections.ICollection
{
    private readonly List<T> _items = new();
    private readonly IComparer<T>? _comparer;

    public SortedSet() { _comparer = Comparer<T>.Default; }
    public SortedSet(IComparer<T>? comparer) { _comparer = comparer ?? Comparer<T>.Default; }

    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public bool Add(T item)
    {
        var idx = _items.BinarySearch(item, _comparer);
        if (idx >= 0) return false;
        _items.Insert(~idx, item);
        return true;
    }
    public bool Remove(T item) => _items.Remove(item);
    public bool Contains(T item) => _items.Contains(item);
    public void Clear() { _items.Clear(); }
    public void CopyTo(T[] array, int arrayIndex) { _items.CopyTo(array, arrayIndex); }
    void System.Collections.ICollection.CopyTo(Array array, int index)
    {
        CopyTo((T[])array, index);
    }
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    void ICollection<T>.Add(T item) => Add(item);
}

// Replacement for System.Collections.Generic.LinkedList<T>
public class LinkedList<T> : ICollection<T>, System.Collections.ICollection
{
    private readonly List<T> _items = new();

    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public void AddLast(T item) { _items.Add(item); }
    public void AddFirst(T item) { _items.Insert(0, item); }
    public void Add(T item) { AddLast(item); }
    public bool Remove(T item) => _items.Remove(item);
    public bool Contains(T item) => _items.Contains(item);
    public void Clear() { _items.Clear(); }
    public void CopyTo(T[] array, int arrayIndex) { _items.CopyTo(array, arrayIndex); }
    void System.Collections.ICollection.CopyTo(Array array, int index)
    {
        CopyTo((T[])array, index);
    }
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

// Replacement for System.Collections.Generic.SortedDictionary<TKey,TValue>
public class SortedDictionary<TKey, TValue> : IDictionary<TKey, TValue>, System.Collections.IDictionary
{
    private readonly SortedList<TKey, TValue> _items;
    public SortedDictionary() { _items = new SortedList<TKey, TValue>(); }
    public SortedDictionary(IComparer<TKey> comparer) { _items = new SortedList<TKey, TValue>(comparer); }

    public TValue this[TKey key] { get => _items[key]; set => _items[key] = value; }
    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public bool IsFixedSize => false;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    public ICollection<TKey> Keys => _items.Keys;
    public ICollection<TValue> Values => _items.Values;
    System.Collections.ICollection System.Collections.IDictionary.Keys => (System.Collections.ICollection)new List<TKey>(_items.Keys);
    System.Collections.ICollection System.Collections.IDictionary.Values => (System.Collections.ICollection)new List<TValue>(_items.Values);

    public void Add(TKey key, TValue value) { _items.Add(key, value); }
    public bool ContainsKey(TKey key) => _items.ContainsKey(key);
    public bool Remove(TKey key) => _items.Remove(key);
    public bool TryGetValue(TKey key, out TValue value) => _items.TryGetValue(key, out value!);
    public void Add(KeyValuePair<TKey, TValue> item) { _items.Add(item.Key, item.Value); }
    public bool Contains(KeyValuePair<TKey, TValue> item) => _items.ContainsKey(item.Key);
    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        foreach (var kvp in _items)
            array[arrayIndex++] = kvp;
    }
    public bool Remove(KeyValuePair<TKey, TValue> item) => _items.Remove(item.Key);
    public void Clear() { _items.Clear(); }
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    void System.Collections.IDictionary.Add(object key, object value) { _items.Add((TKey)key, (TValue)value); }
    void System.Collections.IDictionary.Remove(object key) { _items.Remove((TKey)key); }
    bool System.Collections.IDictionary.Contains(object key) => _items.ContainsKey((TKey)key);
    object System.Collections.IDictionary.this[object key] { get => _items[(TKey)key]; set => _items[(TKey)key] = (TValue)value; }
    System.Collections.IDictionaryEnumerator System.Collections.IDictionary.GetEnumerator() => _items.GetEnumerator() as System.Collections.IDictionaryEnumerator ?? throw new NotImplementedException();
    void System.Collections.ICollection.CopyTo(Array array, int index)
    {
        foreach (var kvp in _items)
            ((KeyValuePair<TKey, TValue>[])array)[index++] = kvp;
    }
}

// Replacement for System.Collections.Generic.SortedList<TKey,TValue>
public class SortedList<TKey, TValue> : IDictionary<TKey, TValue>, System.Collections.IDictionary
{
    private readonly List<KeyValuePair<TKey, TValue>> _items = new();
    private readonly IComparer<TKey> _comparer;

    public SortedList() { _comparer = Comparer<TKey>.Default; }
    public SortedList(IComparer<TKey> comparer) { _comparer = comparer ?? Comparer<TKey>.Default; }

    public TValue this[TKey key]
    {
        get
        {
            var idx = FindIndex(key);
            if (idx < 0) throw new KeyNotFoundException();
            return _items[idx].Value;
        }
        set
        {
            var idx = FindIndex(key);
            if (idx >= 0) _items[idx] = new KeyValuePair<TKey, TValue>(key, value);
            else _items.Insert(~idx, new KeyValuePair<TKey, TValue>(key, value));
        }
    }
    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public bool IsFixedSize => false;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    public ICollection<TKey> Keys => _items.Select(kvp => kvp.Key).ToList();
    public ICollection<TValue> Values => _items.Select(kvp => kvp.Value).ToList();
    System.Collections.ICollection System.Collections.IDictionary.Keys => (System.Collections.ICollection)new List<TKey>(Keys);
    System.Collections.ICollection System.Collections.IDictionary.Values => (System.Collections.ICollection)new List<TValue>(Values);

    private int FindIndex(TKey key)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            var c = _comparer.Compare(_items[i].Key, key);
            if (c == 0) return i;
            if (c > 0) return ~i;
        }
        return ~_items.Count;
    }

    public void Add(TKey key, TValue value)
    {
        var idx = FindIndex(key);
        if (idx >= 0) throw new ArgumentException("Duplicate key");
        _items.Insert(~idx, new KeyValuePair<TKey, TValue>(key, value));
    }
    public bool ContainsKey(TKey key) => FindIndex(key) >= 0;
    public bool Remove(TKey key)
    {
        var idx = FindIndex(key);
        if (idx < 0) return false;
        _items.RemoveAt(idx);
        return true;
    }
    public bool TryGetValue(TKey key, out TValue value)
    {
        var idx = FindIndex(key);
        if (idx < 0) { value = default!; return false; }
        value = _items[idx].Value;
        return true;
    }
    public void Add(KeyValuePair<TKey, TValue> item) { Add(item.Key, item.Value); }
    public bool Contains(KeyValuePair<TKey, TValue> item)
    {
        var idx = FindIndex(item.Key);
        return idx >= 0 && Equals(_items[idx].Value, item.Value);
    }
    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) { _items.CopyTo(array, arrayIndex); }
    public bool Remove(KeyValuePair<TKey, TValue> item)
    {
        return Remove(item.Key);
    }
    public void Clear() { _items.Clear(); }
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    void System.Collections.IDictionary.Add(object key, object value) { Add((TKey)key, (TValue)value); }
    void System.Collections.IDictionary.Remove(object key) { Remove((TKey)key); }
    bool System.Collections.IDictionary.Contains(object key) => ContainsKey((TKey)key);
    object System.Collections.IDictionary.this[object key] { get => this[(TKey)key]; set => this[(TKey)key] = (TValue)value; }
    System.Collections.IDictionaryEnumerator System.Collections.IDictionary.GetEnumerator()
    {
        return new DictEnumerator(_items.GetEnumerator());
    }
    private class DictEnumerator : System.Collections.IDictionaryEnumerator
    {
        private readonly IEnumerator<KeyValuePair<TKey, TValue>> _e;
        public DictEnumerator(IEnumerator<KeyValuePair<TKey, TValue>> e) { _e = e; }
        public object Key => _e.Current.Key;
        public object Value => _e.Current.Value;
        public DictionaryEntry Entry => new DictionaryEntry(_e.Current.Key, _e.Current.Value);
        public object Current => Entry;
        public bool MoveNext() => _e.MoveNext();
        public void Reset() => _e.Reset();
    }
    void System.Collections.ICollection.CopyTo(Array array, int index)
    {
        foreach (var kvp in _items)
            ((KeyValuePair<TKey, TValue>[])array)[index++] = kvp;
    }
}

// Replacement for System.Collections.ObjectModel.ObservableCollection<T>
public class ObservableCollection<T> : List<T>, System.Collections.Specialized.INotifyCollectionChanged
{
    public event System.Collections.Specialized.NotifyCollectionChangedEventHandler? CollectionChanged;
    public new void Add(T item) { base.Add(item); CollectionChanged?.Invoke(this, new System.Collections.Specialized.NotifyCollectionChangedEventArgs(System.Collections.Specialized.NotifyCollectionChangedAction.Add, item)); }
    public new bool Remove(T item) { var r = base.Remove(item); if (r) CollectionChanged?.Invoke(this, new System.Collections.Specialized.NotifyCollectionChangedEventArgs(System.Collections.Specialized.NotifyCollectionChangedAction.Remove, item)); return r; }
}

// Replacement for System.Collections.Concurrent.ConcurrentDictionary<TKey,TValue>
public class ConcurrentDictionary<TKey, TValue> : Dictionary<TKey, TValue>, System.Collections.IDictionary
{
    private readonly object _lock = new();
    public ConcurrentDictionary() { }
    public ConcurrentDictionary(IEqualityComparer<TKey> comparer) : base(comparer) { }
    public new TValue this[TKey key]
    {
        get { lock (_lock) return base[key]; }
        set { lock (_lock) base[key] = value; }
    }
    public bool TryAdd(TKey key, TValue value) { lock (_lock) { if (ContainsKey(key)) return false; base[key] = value; return true; } }
    public bool TryGetValue(TKey key, out TValue value) { if (key == null) { value = default!; return false; } lock (_lock) { if (ContainsKey(key)) { value = base[key]; return true; } value = default!; return false; } }
    public bool TryRemove(TKey key, out TValue value) { lock (_lock) { if (ContainsKey(key)) { value = base[key]; base.Remove(key); return true; } value = default!; return false; } }
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory) { lock (_lock) { if (ContainsKey(key)) return base[key]; var v = valueFactory(key); base[key] = v; return v; } }
    public bool TryUpdate(TKey key, TValue newValue, TValue comparisonValue)
    {
        lock (_lock)
        {
            if (ContainsKey(key) && Equals(base[key], comparisonValue)) { base[key] = newValue; return true; }
            return false;
        }
    }
}

// Replacement for System.Collections.Concurrent.ConcurrentStack<T>
public class ConcurrentStack<T> : Stack<T> { }

// Replacement for System.Collections.Concurrent.ConcurrentBag<T>
public class ConcurrentBag<T> : List<T> { }

// Replacement for System.Collections.Concurrent.ConcurrentQueue<T>
public class ConcurrentQueue<T> : Queue<T> { }
