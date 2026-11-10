using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CoDriver.Editor
{
    public static class OverhaulAudit
    {
        public static void DavisPreview()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene
            );
            Art.Initialize();
            var map = MapDefinition.Load("davis-downtown");
            var player = new GameObject("Preview player").AddComponent<Vehicle>();
            player.transform.SetPositionAndRotation(map.Spawn, map.SpawnRotation);
            player.Initialize();
            var town = new GameObject("Davis").AddComponent<Town>();
            town.Map = map;
            town.Build(player.transform);
            var camera = new GameObject("Preview camera").AddComponent<Camera>();
            camera.transform.position = map.Spawn + new Vector3(0, 180, -150);
            camera.transform.LookAt(map.Spawn);
            camera.farClipPlane = 1300;
            camera.fieldOfView = 60;
            var target = new RenderTexture(1280, 720, 24);
            target.Create();
            camera.targetTexture = target;
            for (int i = 0; i < 4; i++)
                camera.Render();
            RenderTexture.active = target;
            var picture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            picture.Apply();
            var directory = System.IO.Path.GetFullPath("../outputs/qualification/overhaul");
            System.IO.Directory.CreateDirectory(directory);
            System.IO.File.WriteAllBytes(
                System.IO.Path.Combine(directory, "Davis-preview.png"),
                picture.EncodeToPNG()
            );
            Debug.Log(
                "DAVIS_PREVIEW_OK lanes=" + map.lanes.Length + " actors=" + town.Traffic.Count
            );
        }

        public static void Models()
        {
            foreach (string name in new[] { "Dog", "Cat", "Eagle" })
            {
                string path = "Assets/External/Resources/Scenery/" + name + ".fbx";
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!source)
                    continue;
                var model = Object.Instantiate(source);
                var clip = AssetDatabase
                    .LoadAllAssetsAtPath(path)
                    .OfType<AnimationClip>()
                    .FirstOrDefault(c => c.name.Contains("Idle"));
                if (clip)
                    clip.SampleAnimation(model, 0);
                foreach (var bone in model.GetComponentsInChildren<Transform>())
                    if (name != "Eagle")
                        Debug.Log(
                            "AXIS "
                                + name
                                + " "
                                + bone.name
                                + " "
                                + model
                                    .transform.InverseTransformPoint(bone.position)
                                    .ToString("F3")
                        );
                Debug.Log("BOUNDS " + name + " " + RealismImporter.Bounds(model));
                Object.DestroyImmediate(model);
            }
            var car = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/External/Resources/Cockpit/Source/CarConcept.glb"
            );
            if (car)
                foreach (var part in car.GetComponentsInChildren<Transform>())
                    Debug.Log("CABIN " + part.name + " " + part.position.ToString("F3"));
        }
    }
}
