using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds "NeoRacer - Purdue Testbed": Purdue's taped lane track for AutoDRIVE, from its
// lane map (neoracer_ros2_driver scripts/lanes/purdue_testbed.json, traced from a photo of
// the track; copied into this folder). It is the NeoRacer scene with Clemson's track taken
// out (its Floor and Infrastructure) and the testbed put in: a wooden floor, the foam mat,
// its white tape, dashed yellow centre lines and red stop lines, each at its traced place,
// width and colour.
//
// The testbed lies in the lane map's own frame: a map point (x, y) is the simulator's x
// forward, y left, which AutoDRIVE's socket puts at Unity (-y, height, x). So a car
// v2v_view.py has locked onto a lane (--lanes) is placed by its map pose as it is.
//
// The NeoRacer starts on a lane, its lap timer off (no laps or checkpoints here). God's
// Eye looks straight down on the mat, north up; Trackcam looks across it from where the
// photo was taken. The build ends with an audit of every reference the old track left
// dangling. Menu, or batchmode:
//   -executeMethod NeoRacerTestbed.Build
public static class NeoRacerTestbed
{
    const string Source = "Assets/Scenes/NeoRacer - AuE-X930.unity";
    public const string Target = "Assets/Scenes/NeoRacer - Purdue Testbed.unity";
    const string Dir = "Assets/Environments/Purdue Testbed";
    const string MapFile = Dir + "/purdue_testbed.json";
    const string Template = "Assets/Models/Vehicle/NeoRacer/Materials/Low Gloss Plastic - Black.mat";
    static readonly string[] OldTrack = { "Floor", "Infrastructure" };
    public const string StartLane = "loop2_fwd";
    public const float StartS = 1f;      // m along it: on the top straight, which runs to 1.4 m
    const float Foam = 0.012f;      // m: the mat's thickness (cosmetic)
    const float FloorSize = 20f;    // m square of floor around the mat (cosmetic)
    const string Wood = "#a9937d";  // the floor beside the mat in the photo
    // Tape lies this far over the mat, each colour a little higher so none flicker
    const float WhiteLift = 0.0006f, YellowLift = 0.0008f, RedLift = 0.0010f;

    [MenuItem("AutoDRIVE/NeoRacer: Build Purdue Testbed Scene")]
    static void Menu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("NeoRacerTestbed: stop Play mode first");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Debug.Log(Make() ? "TESTBED built: " + Target : "TESTBED FAILED: see the errors above");
    }

    public static void Build()
    {
        EditorApplication.Exit(Make() ? 0 : 1);
    }

    // A map point at a height above the floor, in Unity.
    static Vector3 U(Vector2 p, float h) => new Vector3(-p.y, h, p.x);

    static List<Vector2> Points(JSONObject list)
    {
        var pts = new List<Vector2>();
        foreach (JSONObject p in list.list) pts.Add(new Vector2(p.list[0].f, p.list[1].f));
        return pts;
    }

    static bool Make()
    {
        var map = new JSONObject(File.ReadAllText(MapFile));
        float width = map.GetField("mat").GetField("width").f;
        float length = map.GetField("mat").GetField("length").f;
        JSONObject tape = map.GetField("tape"), look = map.GetField("look");

        var scene = EditorSceneManager.OpenScene(Source, OpenSceneMode.Single);
        var roots = new Dictionary<string, GameObject>();
        foreach (GameObject g in scene.GetRootGameObjects()) roots[g.name] = g;
        foreach (string name in OldTrack)
        {
            if (!roots.ContainsKey(name))
            {
                Debug.LogError("NeoRacerTestbed: the NeoRacer scene has no " + name + " to take out");
                return false;
            }
            Object.DestroyImmediate(roots[name]);
        }

        Directory.CreateDirectory(Dir + "/Materials");
        Directory.CreateDirectory(Dir + "/Meshes");
        AssetDatabase.Refresh();
        Material wood = Paint("Floor", Wood, 0.3f), mat = Paint("Mat", look.GetField("mat").str, 0.15f);
        Material white = Paint("Tape White", look.GetField("white").str, 0.45f);
        Material yellow = Paint("Tape Yellow", look.GetField("yellow").str, 0.45f);
        Material red = Paint("Tape Red", look.GetField("red").str, 0.45f);

        var testbed = new GameObject("Purdue Testbed");
        Box(testbed, "Floor", new Vector3(-length / 2, -0.05f, width / 2), new Vector3(FloorSize, 0.1f, FloorSize), wood);
        Box(testbed, "Mat", new Vector3(-length / 2, Foam / 2, width / 2), new Vector3(length, Foam, width), mat);

        var whites = new List<(List<Vector2>, bool)>();
        foreach (JSONObject l in map.GetField("white_lines").list) whites.Add((Points(l.GetField("points")), l.GetField("closed").b));
        Flat(testbed, "White Tape", Strips(whites, tape.GetField("white_width").f, Foam + WhiteLift), white);

        var dashes = new List<(List<Vector2>, bool)>();
        float dash = tape.GetField("dash_length").f, period = tape.GetField("dash_period").f;
        foreach (JSONObject l in map.GetField("yellow_lines").list)
            dashes.AddRange(Dashes(Points(l.GetField("points")), l.GetField("closed").b, dash, period));
        Flat(testbed, "Yellow Dashes", Strips(dashes, tape.GetField("yellow_width").f, Foam + YellowLift), yellow);

        var stops = new List<(List<Vector2>, bool)>();
        foreach (JSONObject s in map.GetField("stop_lines").list)
            stops.Add((new List<Vector2> { new Vector2(s.GetField("a").list[0].f, s.GetField("a").list[1].f),
                                            new Vector2(s.GetField("b").list[0].f, s.GetField("b").list[1].f) }, false));
        Flat(testbed, "Stop Lines", Strips(stops, tape.GetField("red_width").f, Foam + RedLift), red);

        // the car on its lane, facing along it
        if (!roots.ContainsKey("NeoRacer"))
        {
            Debug.LogError("NeoRacerTestbed: the NeoRacer scene has no NeoRacer");
            return false;
        }
        GameObject car = roots["NeoRacer"];
        (Vector2 at, float heading) = LanePose(map, StartLane, StartS);
        car.transform.SetPositionAndRotation(U(at, Foam), Quaternion.Euler(0f, -heading * Mathf.Rad2Deg, 0f));
        foreach (LapTimer timer in car.GetComponentsInChildren<LapTimer>(true))
        {
            timer.enabled = false;
            var laps = new SerializedObject(timer);
            laps.FindProperty("Checkpoints").arraySize = 0;   // the old track's; none on the testbed
            laps.ApplyModifiedPropertiesWithoutUndo();
        }

        // God's Eye straight down on the mat, north up; Trackcam from the photo's corner
        Vector3 middle = U(new Vector2(width / 2, length / 2), 0f);
        if (roots.TryGetValue("God's Eye", out GameObject gods))
        {
            Camera cam = gods.GetComponentInChildren<Camera>(true);
            float half = 0.55f * Mathf.Max(length, width * 9f / 16f);
            float height = cam.orthographic ? 30f : half / Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2f);
            if (cam.orthographic) cam.orthographicSize = half;
            cam.transform.SetPositionAndRotation(middle + Vector3.up * height, Quaternion.Euler(90f, -90f, 0f));
        }
        if (roots.TryGetValue("Trackcam", out GameObject trackcam))
        {
            // a fixed camera here: it no longer follows a piece of the old track from above
            foreach (FollowTarget follow in trackcam.GetComponentsInChildren<FollowTarget>(true))
                Object.DestroyImmediate(follow);
            Camera cam = trackcam.GetComponentInChildren<Camera>(true);
            Vector3 eye = U(new Vector2(-0.12f * width, -0.12f * length), 0.25f * Mathf.Max(width, length));
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(middle - eye));
        }

        int missing = Audit(scene);
        Debug.Log("TESTBED mat " + width.ToString("F2") + " x " + length.ToString("F2") + " m, " + whites.Count +
                  " white lines, " + dashes.Count + " dashes, " + stops.Count + " stop lines; car on " + StartLane +
                  " at " + StartS + " m (" + at.x.ToString("F2") + ", " + at.y.ToString("F2") + "); " +
                  missing + " references left dangling");
        EditorSceneManager.SaveScene(scene, Target);
        AssetDatabase.SaveAssets();
        return missing == 0;
    }

    // A lane's centre point s m along it, and its heading (rad, anticlockwise from the map's x).
    public static (Vector2, float) LanePose(JSONObject map, string id, float s)
    {
        foreach (JSONObject lane in map.GetField("lanes").list)
        {
            if (lane.GetField("id").str != id) continue;
            List<Vector2> pts = Points(lane.GetField("points"));
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                float seg = Vector2.Distance(pts[i], pts[i + 1]);
                if (s <= seg || i + 2 == pts.Count)
                {
                    Vector2 d = pts[i + 1] - pts[i];
                    return (pts[i] + d * Mathf.Clamp01(s / seg), Mathf.Atan2(d.y, d.x));
                }
                s -= seg;
            }
        }
        throw new System.ArgumentException("the lane map has no lane " + id);
    }

    static Material Paint(string name, string hex, float smoothness)
    {
        string path = Dir + "/Materials/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            AssetDatabase.CopyAsset(Template, path);
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
        }
        ColorUtility.TryParseHtmlString(hex, out Color c);
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void Box(GameObject parent, string name, Vector3 centre, Vector3 size, Material m)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(parent.transform, false);
        g.transform.localPosition = centre;
        g.transform.localScale = size;
        g.GetComponent<MeshRenderer>().sharedMaterial = m;
    }

    static void Flat(GameObject parent, string name, Mesh mesh, Material m)
    {
        string path = Dir + "/Meshes/" + name + ".asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        var g = new GameObject(name);
        g.transform.SetParent(parent.transform, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        g.AddComponent<MeshRenderer>().sharedMaterial = m;
    }

    // The pieces of a dashed line: a dash every `period` m along it, `dash` m long.
    static List<(List<Vector2>, bool)> Dashes(List<Vector2> pts, bool closed, float dash, float period)
    {
        if (closed) pts = new List<Vector2>(pts) { pts[0] };
        var cum = new List<float> { 0f };
        for (int i = 1; i < pts.Count; i++) cum.Add(cum[i - 1] + Vector2.Distance(pts[i - 1], pts[i]));
        Vector2 At(float s)
        {
            int i = Mathf.Max(cum.FindLastIndex(c => c <= s), 0);
            if (i >= pts.Count - 1) return pts[pts.Count - 1];
            float seg = cum[i + 1] - cum[i];
            return Vector2.Lerp(pts[i], pts[i + 1], seg > 0 ? (s - cum[i]) / seg : 0f);
        }
        var out_ = new List<(List<Vector2>, bool)>();
        for (float s0 = 0f; s0 + dash <= cum[cum.Count - 1]; s0 += period)
        {
            var piece = new List<Vector2> { At(s0) };
            for (int i = 0; i < pts.Count; i++) if (cum[i] > s0 && cum[i] < s0 + dash) piece.Add(pts[i]);
            piece.Add(At(s0 + dash));
            out_.Add((piece, false));
        }
        return out_;
    }

    // Flat strips `width` m wide along polylines, at a height, facing up.
    static Mesh Strips(List<(List<Vector2>, bool)> lines, float width, float height)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        foreach (var (pts, closed) in lines)
        {
            int n = pts.Count, first = verts.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = pts[closed ? (i - 1 + n) % n : Mathf.Max(i - 1, 0)];
                Vector2 next = pts[closed ? (i + 1) % n : Mathf.Min(i + 1, n - 1)];
                Vector2 way = (next - prev).normalized;
                Vector2 left = new Vector2(-way.y, way.x) * (width / 2f);
                verts.Add(U(pts[i] + left, height));
                verts.Add(U(pts[i] - left, height));
            }
            int segments = closed ? n : n - 1;
            for (int i = 0; i < segments; i++)
            {
                int a = first + 2 * i, b = first + 2 * ((i + 1) % n);
                // left, right, next left; right, next right, next left: facing up in Unity
                tris.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
            }
        }
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        if (mesh.normals[0].y < 0f)
        {
            tris.Reverse();
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
        }
        return mesh;
    }

    // References the scene's components hold to objects that no longer exist.
    static int Audit(UnityEngine.SceneManagement.Scene scene)
    {
        int missing = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Component c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;  // a missing script: not ours to judge here
                var so = new SerializedObject(c);
                SerializedProperty p = so.GetIterator();
                while (p.NextVisible(true))
                {
                    if (p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue == null
                        && p.objectReferenceInstanceIDValue != 0)
                    {
                        missing++;
                        Debug.LogWarning("TESTBED dangling: " + c.GetType().Name + " on " + c.name + "." + p.propertyPath);
                    }
                }
            }
        return missing;
    }
}
