using UnityEngine;
using UnityEngine.Events;

public class GenericEventTrigger : MonoBehaviour
{
    [SerializeField] UnityEvent onAwake;
    [SerializeField] UnityEvent onStart;
    [SerializeField] UnityEvent onEnable;
    [SerializeField] UnityEvent onDisable;
    [SerializeField] UnityEvent onApplicationExit;
    [SerializeField] UnityEvent onApplicationResume;

    private void Awake()
    {
        onAwake?.Invoke();
    }

    private void Start()
    {
        onStart?.Invoke();
    }

    private void OnEnable()
    {
        onEnable?.Invoke();
    }

    private void OnDisable()
    {
        onDisable?.Invoke();
    }

    private void OnApplicationFocus(bool focus)
    {
        if(!focus)
        {
            onApplicationExit?.Invoke();
        }
        else
        {
            onApplicationResume?.Invoke();
        }
    }
}
