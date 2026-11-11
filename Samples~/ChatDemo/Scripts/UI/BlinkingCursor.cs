using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class BlinkingCursor : MonoBehaviour
{
    private UIDocument _uiDocument;
    private IVisualElementScheduledItem _blinkTask;
    private TextField _activeTextField;

    private void Start()
    {
        _uiDocument = GetComponent<UIDocument>();
        if (_uiDocument != null && _uiDocument.rootVisualElement != null)
        {
            // Find all TextFields in the document to apply the blinking effect
            var textFields = _uiDocument.rootVisualElement.Query<TextField>().Build();
            foreach (var tf in textFields)
            {
                tf.RegisterCallback<FocusInEvent>(OnFocusIn);
                tf.RegisterCallback<FocusOutEvent>(OnFocusOut);
            }
        }
    }

    private void OnDisable()
    {
        if (_uiDocument != null && _uiDocument.rootVisualElement != null)
        {
            var textFields = _uiDocument.rootVisualElement.Query<TextField>().Build();
            foreach (var tf in textFields)
            {
                tf.UnregisterCallback<FocusInEvent>(OnFocusIn);
                tf.UnregisterCallback<FocusOutEvent>(OnFocusOut);
            }
        }

        if (_blinkTask != null)
        {
            _blinkTask.Pause();
            _blinkTask = null;
        }

        if (_activeTextField != null)
        {
            _activeTextField.RemoveFromClassList("transparentCursor");
            _activeTextField = null;
        }
    }

    private void OnFocusIn(FocusInEvent evt)
    {
        if (evt.currentTarget is TextField tf)
        {
            _activeTextField = tf;
            _activeTextField.RemoveFromClassList("transparentCursor");

            if (_blinkTask == null)
            {
                _blinkTask = _uiDocument.rootVisualElement.schedule.Execute(Blink).Every(500);
            }
            else
            {
                _blinkTask.Resume();
            }
        }
    }

    private void OnFocusOut(FocusOutEvent evt)
    {
        if (_blinkTask != null)
        {
            _blinkTask.Pause();
        }

        if (_activeTextField != null)
        {
            _activeTextField.RemoveFromClassList("transparentCursor");
            _activeTextField = null;
        }
    }

    private void Blink()
    {
        if (_activeTextField == null) return;

        if (_activeTextField.ClassListContains("transparentCursor"))
            _activeTextField.RemoveFromClassList("transparentCursor");
        else
            _activeTextField.AddToClassList("transparentCursor");
    }
}
