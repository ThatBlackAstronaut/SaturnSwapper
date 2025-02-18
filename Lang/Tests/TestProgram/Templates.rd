template KeyValuePair<TKey, TValue>
{
    public TKey Key;
    public TValue Value;

    public KeyValuePair(TKey key, TValue value)
    {
        Key = key;
        Value = value;
    }
}

template Dictionary<TKey, TValue>
{
    private list<KeyValuePair<TKey, TValue>> _list;

    public Dictionary()
    {
        _list = new list<KeyValuePair<TKey, TValue>>();
    }

    public void Add(TKey key, TValue value)
    {
        _list.Add(new KeyValuePair<TKey, TValue>(key, value));
    }
}
