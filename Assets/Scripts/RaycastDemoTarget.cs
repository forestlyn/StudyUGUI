using UnityEngine;
using UnityEngine.EventSystems;

// Both the UI and the cube receive events through the same EventSystem interfaces.
public sealed class RaycastDemoTarget : MonoBehaviour, IPointerEnterHandler,
    IPointerExitHandler, IPointerDownHandler, IPointerClickHandler
{
    public PhysicsRaycasterDemo demo;
    public string targetLabel;

    public void OnPointerEnter(PointerEventData data) => Report("Enter", data);
    public void OnPointerExit(PointerEventData data) => Report("Exit", data);
    public void OnPointerDown(PointerEventData data) => Report("Down", data);
    public void OnPointerClick(PointerEventData data) => Report("Click", data);

    private void Report(string action, PointerEventData data)
    {
        var module = data.pointerCurrentRaycast.module;
        demo.Record(targetLabel + " / " + action + " / hit: " +
            (module != null ? module.GetType().Name : "none"));
    }
}
