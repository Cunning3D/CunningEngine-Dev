#if UNITY_EDITOR
using UnityEngine;

namespace Unity.Splines.Examples
{
    public enum PreviewObjectType
    {
        Road,
        Junction
    }

    [ExecuteInEditMode]
    public sealed class PreviewObjectMarker : MonoBehaviour
    {
        [HideInInspector]
        public PreviewObjectType previewType;

        [HideInInspector]
        public int groupIndex = -1;
    }
}
#endif
