using UnityEngine;

namespace CoDriver
{
    public static class WorldSigns
    {
        public static bool Place(string asset, Transform parent)
        {
            var source = Resources.Load<GameObject>("WorldArt/Signs/" + asset);
            if (!source) return false;
            var model = Object.Instantiate(source, parent);
            // Authored face points +Z after glTF conversion; approaching drivers
            // look along the lane's +Z and need to see the sign's -Z face.
            model.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var pole = parent.gameObject.AddComponent<BoxCollider>();
            pole.center = new Vector3(0, 1.1f, 0);
            pole.size = new Vector3(.08f, 2.2f, .08f);
            return true;
        }
    }
}
