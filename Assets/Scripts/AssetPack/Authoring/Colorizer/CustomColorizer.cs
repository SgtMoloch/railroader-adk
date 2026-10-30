using UnityEngine;

namespace Railroader.AssetTools.Colorizer
{
    [AddComponentMenu("Railroader/Asset Tools/Colorizer Marker")]
    [HelpURL("https://docs.railroadergame.com/modding/concepts/colorable-components/")]
    public sealed class CustomColorizer : MonoBehaviour
    {

        [Tooltip("Zero-based material slot on this object's MeshRenderer or SkinnedMeshRenderer. Add another component for another slot.")]
        [Min(0)]
        public int materialSlot;

        [Tooltip("Meshes with the same style class share a color control. If source material and label match too, the baker also reuses one generated material. Use lowercase letters, numbers and hyphens.")]
        public string styleClass = "body";
        [ColorUsage(false)]
        public Color defaultColor = Color.white;
        [Min(0.01f)]
        public float colorGamma = 1f;
        public bool showInCustomize = true;
        public string controlLabel = "Body";
    }
}
