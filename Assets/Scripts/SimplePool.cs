using System.Collections.Generic;
using UnityEngine;

public class SimplePool<T> where T : Component
{
    private readonly T prefab;
    private readonly Transform parent;
    private readonly Queue<T> inactive = new Queue<T>();
    private readonly List<T> active = new List<T>();

    public IReadOnlyList<T> Active => active;

    public SimplePool(T prefab, Transform parent, int prewarm = 0)
    {
        this.prefab = prefab;
        this.parent = parent;
        for (int i = 0; i < prewarm; i++)
        {
            var instance = Object.Instantiate(prefab, parent);
            instance.gameObject.SetActive(false);
            inactive.Enqueue(instance);
        }
    }

    public T Get()
    {
        T instance;
        if (inactive.Count > 0)
        {
            instance = inactive.Dequeue();
        }
        else
        {
            instance = Object.Instantiate(prefab, parent);
        }
        instance.gameObject.SetActive(true);
        active.Add(instance);
        return instance;
    }

    public void Release(T instance)
    {
        if (instance == null) return;
        if (active.Remove(instance))
        {
            instance.gameObject.SetActive(false);
            inactive.Enqueue(instance);
        }
    }
}
