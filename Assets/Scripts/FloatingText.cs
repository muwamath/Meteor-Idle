using TMPro;
using UnityEngine;

[RequireComponent(typeof(TextMeshPro))]
public class FloatingText : MonoBehaviour
{
    [SerializeField] private float lifetime = 1f;
    [SerializeField] private float riseDistance = 1.5f;

    private TextMeshPro text;
    private Vector3 start;
    private float spawnTime;

    private void Awake()
    {
        text = GetComponent<TextMeshPro>();
    }

    public void Show(string message)
    {
        text.text = message;
        start = transform.position;
        spawnTime = Time.time;
        var c = text.color; c.a = 1f; text.color = c;
    }

    private void Update()
    {
        float t = (Time.time - spawnTime) / lifetime;
        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }
        transform.position = start + Vector3.up * (riseDistance * t);
        var c = text.color; c.a = 1f - t; text.color = c;
    }
}
