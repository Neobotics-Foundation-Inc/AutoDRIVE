using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds a two-car scene: a NeoRacer scene with a second NeoRacer by the first as
// AutoDRIVE's vehicle 2, for two real cars co-simulated at once (v2v_view.py
// --autodrive CAR1 CAR2). "NeoRacer - AuE-X930 - Two Cars" has car 2 beside car 1;
// "NeoRacer - Purdue Testbed - Two Cars" (built after NeoRacerTestbed) 0.65 m behind
// it on its lane (a 17 cm gap between the cars).
//
// Car 2 is a fresh instance of the NeoRacer prefab with car 1's overrides in the scene
// (the HUD its LiDAR draws on: without it the LiDAR throws every physics step, and the
// simulator runs at a sixth of its rate). Every per-vehicle array of the
// scene's Socket gets car 2's component from the same place in its hierarchy as car 1's,
// and a second Reset Manager keeps car 2 kinematic while co-simulation poses it. The lap
// timer and its HUD stay car 1's (car 2's lap timer is off, the Socket's LapTimers keep
// one entry), car 2's camera renders into its own texture, its audio listener is off, and
// it is not tagged Player (cameras that look for the player follow car 1). The build ends
// with an audit of every reference it set. Menu, or batchmode:
//   -executeMethod NeoRacerTwoCars.Build          the AuE-X930 scene
//   -executeMethod NeoRacerTwoCars.BuildTestbed   the Purdue testbed scene
public static class NeoRacerTwoCars
{
    const string AuE = "Assets/Scenes/NeoRacer - AuE-X930.unity";
    const string AuETwo = "Assets/Scenes/NeoRacer - AuE-X930 - Two Cars.unity";
    const string TestbedTwo = "Assets/Scenes/NeoRacer - Purdue Testbed - Two Cars.unity";
    const string Texture2 = "Assets/Render Textures/NeoRacer Camera 2.renderTexture";
    // Socket arrays that stay car 1's: one HUD, one lap count
    static readonly string[] CarOneOnly = { "LapTimers" };

    [MenuItem("AutoDRIVE/NeoRacer: Build Two-Car Scene")]
    static void Menu() => FromMenu(AuE, AuETwo, 0f, 0.6f);

    [MenuItem("AutoDRIVE/NeoRacer: Build Purdue Testbed Two-Car Scene")]
    static void MenuTestbed() => FromMenu(NeoRacerTestbed.Target, TestbedTwo, -0.65f, 0f);

    static void FromMenu(string source, string target, float ahead, float left)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("NeoRacerTwoCars: stop Play mode first");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Debug.Log(Make(source, target, ahead, left) ? "TWO CARS built: " + target : "TWO CARS FAILED: see the errors above");
    }

    // Car 2 beside car 1, 0.6 m to its left
    public static void Build()
    {
        EditorApplication.Exit(Make(AuE, AuETwo, 0f, 0.6f) ? 0 : 1);
    }

    // Car 2 on car 1's lane, 0.65 m behind it
    public static void BuildTestbed()
    {
        EditorApplication.Exit(Make(NeoRacerTestbed.Target, TestbedTwo, -0.65f, 0f) ? 0 : 1);
    }

    // `ahead` and `left`: car 2's place from car 1's, m along and across car 1's heading.
    static bool Make(string source, string target, float ahead, float left)
    {
        var scene = EditorSceneManager.OpenScene(source, OpenSceneMode.Single);
        // the socket's object is inactive until Connect: look through every root, inactive too
        Socket socket = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            socket = socket != null ? socket : root.GetComponentInChildren<Socket>(true);
        SerializedObject s = new SerializedObject(socket);
        VehicleController vc1 = (VehicleController)s.FindProperty("VehicleControllers").GetArrayElementAtIndex(0).objectReferenceValue;
        GameObject root1 = PrefabUtility.GetOutermostPrefabInstanceRoot(vc1.gameObject);
        GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource(root1);

        GameObject root2 = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        // Car 1's overrides in the scene are car 2's too, but for its place and name: among them
        // the HUD its LiDAR draws the scan on, a scene object the prefab cannot hold. Set before
        // anything else on car 2: setting a prefab instance's overrides resets what was changed on
        // it and not yet recorded as one.
        Transform rootAsset = PrefabUtility.GetCorrespondingObjectFromSource(root1.transform);
        List<PropertyModification> mods = new List<PropertyModification>(PrefabUtility.GetPropertyModifications(root2));
        foreach (PropertyModification m in PrefabUtility.GetPropertyModifications(root1))
        {
            if (m.target == rootAsset || m.target == prefab) continue;
            Transform at = m.objectReference is Component mc ? mc.transform : m.objectReference is GameObject mg ? mg.transform : null;
            if (at != null && at.IsChildOf(root1.transform)) m.objectReference = Remap(m.objectReference);
            mods.Add(m);
        }
        PrefabUtility.SetPropertyModifications(root2, mods.ToArray());
        root2.name = root1.name + " 2";
        root2.transform.SetParent(root1.transform.parent, false);
        Transform body1 = vc1.VehicleRigidBody.transform;
        root2.transform.SetPositionAndRotation(root1.transform.position + body1.forward * ahead - body1.right * left,
                                               root1.transform.rotation);
        foreach (Transform t in root2.GetComponentsInChildren<Transform>(true))
            if (t.CompareTag("Player")) t.gameObject.tag = "Untagged";
        foreach (LapTimer l in root2.GetComponentsInChildren<LapTimer>(true)) l.enabled = false;
        foreach (AudioListener a in root2.GetComponentsInChildren<AudioListener>(true)) a.enabled = false;
        foreach (Camera c in root2.GetComponentsInChildren<Camera>(true))
        {
            if (c.targetTexture == null) continue;
            if (AssetDatabase.LoadAssetAtPath<RenderTexture>(Texture2) == null &&
                !AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(c.targetTexture), Texture2))
            {
                Debug.LogError("NeoRacerTwoCars: could not copy " + AssetDatabase.GetAssetPath(c.targetTexture) + " to " + Texture2);
                return false;
            }
            c.targetTexture = AssetDatabase.LoadAssetAtPath<RenderTexture>(Texture2);
        }

        // Car 2's counterpart of an object on car 1: the same child by sibling index at each
        // level (the same names check it), the same component among those of its type.
        Object Remap(Object o)
        {
            Component c = o as Component;
            Transform t1 = c != null ? c.transform : ((GameObject)o).transform;
            List<int> path = new List<int>();
            for (Transform t = t1; t != root1.transform; t = t.parent)
            {
                if (t == null) throw new System.ArgumentException(o.name + " is not part of " + root1.name);
                path.Insert(0, t.GetSiblingIndex());
            }
            Transform t2 = root2.transform;
            foreach (int i in path) t2 = t2.GetChild(i);
            if (path.Count > 0 && t2.name != t1.name) throw new System.ArgumentException("car 2 has " + t2.name + " where car 1 has " + t1.name);
            if (c == null) return t2.gameObject;
            return t2.GetComponents(c.GetType())[System.Array.IndexOf(t1.GetComponents(c.GetType()), c)];
        }

        // Every Socket array holding one of car 1's components gets car 2's after it.
        List<string> grown = new List<string>();
        SerializedProperty p = s.GetIterator();
        p.NextVisible(true);
        do
        {
            if (!p.isArray || p.propertyType != SerializedPropertyType.Generic || p.arraySize != 1) continue;
            SerializedProperty first = p.GetArrayElementAtIndex(0);
            if (first.propertyType != SerializedPropertyType.ObjectReference || first.objectReferenceValue == null) continue;
            Component held = first.objectReferenceValue as Component;
            if (held == null || !held.transform.IsChildOf(root1.transform) || System.Array.IndexOf(CarOneOnly, p.name) >= 0) continue;
            grown.Add(p.name);
        } while (p.NextVisible(false));
        foreach (string name in grown)
        {
            SerializedProperty a = s.FindProperty(name);
            a.arraySize = 2;
            a.GetArrayElementAtIndex(1).objectReferenceValue = Remap(a.GetArrayElementAtIndex(0).objectReferenceValue);
        }

        // A Reset Manager of car 2's own: it keeps car 2 kinematic while posed. It holds no lap
        // timer, which it would index by vehicle alongside the others.
        ResetManager rm1 = (ResetManager)s.FindProperty("ResetManagers").GetArrayElementAtIndex(0).objectReferenceValue;
        GameObject rmObject = new GameObject(rm1.gameObject.name + " 2");
        rmObject.transform.SetParent(rm1.transform.parent, false);
        ResetManager rm2 = rmObject.AddComponent<ResetManager>();
        SerializedObject r1 = new SerializedObject(rm1), r2 = new SerializedObject(rm2);
        foreach (string f in new[] { "Vehicles", "VehicleRigidBodies", "CoSimManagers", "LeftWheelEncoders", "RightWheelEncoders" })
        {
            SerializedProperty a = r1.FindProperty(f), b = r2.FindProperty(f);
            if (a.arraySize != 1)
            {
                Debug.LogError("NeoRacerTwoCars: " + rm1.name + "." + f + " holds " + a.arraySize + " entries; expected car 1's alone");
                return false;
            }
            b.arraySize = 1;
            b.GetArrayElementAtIndex(0).objectReferenceValue = Remap(a.GetArrayElementAtIndex(0).objectReferenceValue);
        }
        r2.FindProperty("LapTimers").arraySize = 0;
        r2.ApplyModifiedPropertiesWithoutUndo();
        SerializedProperty managers = s.FindProperty("ResetManagers");
        managers.arraySize = 2;
        managers.GetArrayElementAtIndex(1).objectReferenceValue = rm2;
        s.ApplyModifiedPropertiesWithoutUndo();

        bool ok = Audit(s, grown, root1, root2, r2, Remap);
        Vector3 offset = root2.transform.position - root1.transform.position;
        float ahead2 = Vector3.Dot(offset, body1.forward), left2 = -Vector3.Dot(offset, body1.right);
        bool placed = Mathf.Abs(ahead2 - ahead) < 1e-3f && Mathf.Abs(left2 - left) < 1e-3f
                      && Quaternion.Angle(root2.transform.rotation, root1.transform.rotation) < 0.01f;
        Debug.Log("TWO CARS car 2 " + ahead2.ToString("F3") + " m ahead of car 1, " + left2.ToString("F3") + " m left, turned "
                  + Quaternion.Angle(root2.transform.rotation, root1.transform.rotation).ToString("F2") + " deg from it" + (placed ? "" : " WRONG"));
        ok &= placed && root2.name == root1.name + " 2";
        if (!ok) return false;
        EditorSceneManager.SaveScene(scene, target);
        AssetDatabase.SaveAssets();
        return true;
    }

    static bool Audit(SerializedObject s, List<string> grown, GameObject root1, GameObject root2, SerializedObject r2,
                      System.Func<Object, Object> remap)
    {
        bool ok = true;
        // Car 2 refers to what car 1 does: the same scene objects and assets, its own parts for
        // car 1's, but for its camera's texture.
        int refs = 0, wrong = 0;
        foreach (Component c1 in root1.GetComponentsInChildren<Component>(true))
        {
            SerializedObject o2 = new SerializedObject(remap(c1));
            SerializedProperty q = new SerializedObject(c1).GetIterator();
            while (q.Next(true))
            {
                if (q.propertyType != SerializedPropertyType.ObjectReference || q.objectReferenceValue == null
                    || q.propertyPath == "m_PrefabInstance" || (c1 is Camera && q.propertyPath == "m_TargetTexture")) continue;
                Object want = q.objectReferenceValue, got = o2.FindProperty(q.propertyPath).objectReferenceValue;
                Transform at = want is Component wc ? wc.transform : want is GameObject wg ? wg.transform : null;
                if (at != null && at.IsChildOf(root1.transform)) want = remap(want);
                refs++;
                if (got == want) continue;
                wrong++;
                Debug.LogError("TWO CARS car 2's " + c1.GetType().Name + " on " + c1.name + "." + q.propertyPath + " is "
                               + (got == null ? "null" : got.name) + "; car 1's is " + want.name);
            }
        }
        Debug.Log("TWO CARS car 2 refers to what car 1 does: " + (refs - wrong) + " of " + refs + " references");
        ok &= wrong == 0;
        s.Update();
        foreach (string name in grown)
        {
            SerializedProperty a = s.FindProperty(name);
            Component one = (Component)a.GetArrayElementAtIndex(0).objectReferenceValue;
            Component two = (Component)a.GetArrayElementAtIndex(1).objectReferenceValue;
            bool right = one.transform.IsChildOf(root1.transform) && two != null && two.transform.IsChildOf(root2.transform)
                         && one.GetType() == two.GetType();
            Debug.Log("TWO CARS Socket." + name + ": " + one.GetType().Name + " " + one.name + " / " + (two == null ? "MISSING" : two.name) + (right ? "" : " WRONG"));
            ok &= right;
        }
        foreach (string name in CarOneOnly)
        {
            int size = s.FindProperty(name).arraySize;
            Debug.Log("TWO CARS Socket." + name + ": car 1 only, " + size + " entry");
            ok &= size == 1;
        }
        SerializedProperty managers = s.FindProperty("ResetManagers");
        ok &= managers.arraySize == 2 && managers.GetArrayElementAtIndex(1).objectReferenceValue != null;
        SerializedProperty it = r2.GetIterator();
        it.NextVisible(true);
        do
        {
            if (!it.isArray || it.propertyType != SerializedPropertyType.Generic || it.arraySize == 0) continue;
            Component held = (Component)it.GetArrayElementAtIndex(0).objectReferenceValue;
            bool right = held != null && held.transform.IsChildOf(root2.transform);
            Debug.Log("TWO CARS Reset Manager 2." + it.name + ": " + (held == null ? "MISSING" : held.name) + (right ? "" : " WRONG"));
            ok &= right;
        } while (it.NextVisible(false));

        int tagged = 0, listeners = 0;
        foreach (Transform t in root2.GetComponentsInChildren<Transform>(true)) if (t.CompareTag("Player")) tagged++;
        foreach (AudioListener l in root2.GetComponentsInChildren<AudioListener>(true)) if (l.enabled) listeners++;
        Debug.Log("TWO CARS car 2: tagged Player " + tagged + ", audio listeners on " + listeners);
        ok &= tagged == 0 && listeners == 0;

        // Anything else in the scene that lists car 1 alone would leave car 2 out: name it.
        foreach (MonoBehaviour m in Object.FindObjectsOfType<MonoBehaviour>(true))
        {
            if (m is Socket || m is ResetManager || m.transform.IsChildOf(root1.transform) || m.transform.IsChildOf(root2.transform)) continue;
            SerializedProperty q = new SerializedObject(m).GetIterator();
            q.NextVisible(true);
            do
            {
                if (!q.isArray || q.propertyType != SerializedPropertyType.Generic || q.arraySize != 1) continue;
                SerializedProperty e = q.GetArrayElementAtIndex(0);
                if (e.propertyType == SerializedPropertyType.ObjectReference && e.objectReferenceValue is Component held && held.transform.IsChildOf(root1.transform))
                    Debug.LogWarning("TWO CARS " + m.GetType().Name + " on " + m.name + " lists car 1 alone in " + q.name);
            } while (q.NextVisible(false));
        }
        Debug.Log("TWO CARS audit " + (ok ? "PASS" : "FAIL"));
        return ok;
    }
}
