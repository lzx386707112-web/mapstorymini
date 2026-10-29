using UnityEngine;

// Field artwork is loaded directly from the original IMG resources.
public sealed class FieldView : MonoBehaviour
{
    FieldMap map;
    Transform root;
    public bool HasMap => map != null;
    public void Show(FieldMap value)
    {
        if (value == null) { Clear(); return; }
        var next = new GameObject("Field_" + value.id).transform;
        next.SetParent(transform, false);
        try { next.gameObject.AddComponent<NativeMapView>().Show(value.id); }
        catch { next.gameObject.SetActive(false); Destroy(next.gameObject); throw; }
        Clear(); map = value; root = next;
    }
    public void SyncDynamicNpcs()
    { if(root!=null && map!=null)root.GetComponent<NativeMapView>().SyncDynamicNpcs(map); }
    public void Clear()
    {
        if (root != null) { root.gameObject.SetActive(false); Destroy(root.gameObject); }
        root = null; map = null;
    }
}
