using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

// Renders stills of the Purdue testbed scene (NeoRacerTestbed) through a camera of its
// own, for looking at the track without running the simulator: straight down on the
// whole mat, across it from the corner the photo was taken from, down on each T
// junction, and behind car 1 on its lane. Batchmode, with graphics (not -nographics):
//   -executeMethod NeoRacerTestbedShots.Run -neoracerShotDir <folder>
public static class NeoRacerTestbedShots
{
    const string Scene = "Assets/Scenes/NeoRacer - Purdue Testbed - Two Cars.unity";
    const string MapFile = "Assets/Environments/Purdue Testbed/purdue_testbed.json";
    const int Frames = 12; // renders per still, for exposure and anti-aliasing to settle

    // A map point at a height, in Unity (as NeoRacerTestbed lays the track out).
    static Vector3 U(float x, float y, float h) => new Vector3(-y, h, x);

    public static void Run()
    {
        string[] args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-neoracerShotDir");
        string dir = args[at + 1];
        Directory.CreateDirectory(dir);
        var map = new JSONObject(File.ReadAllText(MapFile));
        float w = map.GetField("mat").GetField("width").f, l = map.GetField("mat").GetField("length").f;
        float lane = map.GetField("lane_width").f;
        EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);

        var go = new GameObject("Shot Camera");
        Camera cam = go.AddComponent<Camera>();
        HDAdditionalCameraData hd = go.AddComponent<HDAdditionalCameraData>();
        hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 500f;

        Vector3 middle = U(w / 2, l / 2, 0f);
        // straight down, map x to the right and map y up, as the lane map's picture
        cam.fieldOfView = 40f;
        Shot(cam, dir, "top", 1400, 1400, U(w / 2, l / 2, 0.55f * Mathf.Max(w, l) / Mathf.Tan(20f * Mathf.Deg2Rad)),
             Quaternion.LookRotation(Vector3.down, new Vector3(-1f, 0f, 0f)));
        // across the mat from beyond its near-left corner, where the photo was taken
        Vector3 eye = U(-0.18f * l, -0.255f * l, 0.4f * l);
        cam.fieldOfView = 50f;
        Shot(cam, dir, "corner", 1920, 1080, eye, Quaternion.LookRotation(middle - eye));
        // down on each T junction, its stop lines and the paths through it
        int k = 1;
        foreach (JSONObject j in map.GetField("junctions").list)
        {
            Vector3 c = U(j.GetField("at").list[0].f, j.GetField("at").list[1].f, 0f);
            Vector3 e = c + new Vector3(2.6f, 3.8f, -2.6f) * lane;
            cam.fieldOfView = 45f;
            Shot(cam, dir, "junction" + k++, 1920, 1080, e, Quaternion.LookRotation(c - e));
        }
        // from where a photo of the track was taken, if given (a JSON file: pos, fwd, up in
        // Unity, vfov in degrees; trace_track_photo's camera, solved from the mat's corners)
        int photo = Array.IndexOf(args, "-neoracerPhotoCam");
        if (photo >= 0)
        {
            var pose = new JSONObject(File.ReadAllText(args[photo + 1]));
            Vector3 V(string key) => new Vector3(pose.GetField(key).list[0].f, pose.GetField(key).list[1].f,
                                                 pose.GetField(key).list[2].f);
            cam.fieldOfView = pose.GetField("vfov").f;
            Shot(cam, dir, "photo_view", 1428, 1071, V("pos"), Quaternion.LookRotation(V("fwd"), V("up")));
        }
        // behind car 1, low, the way it faces
        VehicleController car = null;
        foreach (VehicleController v in UnityEngine.Object.FindObjectsOfType<VehicleController>(true))
            if (car == null || v.transform.root.name.Length < car.transform.root.name.Length) car = v;
        Transform body = car.VehicleRigidBody.transform;
        Vector3 behind = body.position - body.forward * 1.1f + Vector3.up * 0.55f;
        cam.fieldOfView = 55f;
        Shot(cam, dir, "chase", 1920, 1080, behind,
             Quaternion.LookRotation(body.position + body.forward * 3f - behind));
        EditorApplication.Exit(0);
    }

    static void Shot(Camera cam, string dir, string name, int width, int height, Vector3 at, Quaternion look)
    {
        cam.transform.SetPositionAndRotation(at, look);
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        cam.targetTexture = rt;
        for (int i = 0; i < Frames; i++) cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        rt.Release();
        Debug.Log("SHOT " + name + " " + width + "x" + height);
    }
}
