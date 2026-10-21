using System.Reflection;
using UnityEditor;
using UnityEngine;

// Checks CoSimWheels on the NeoRacer prefab: poses the car along straights and
// arcs, forward and in reverse, and measures how far each tyre's contact point
// moves per step, relative to how far its hub moves. A wheel that rolls the right
// way and steers along its path leaves its contact point where it was (a ratio
// near 0); spinning the wrong way moves it twice the step (2), and a wrong steer
// slides it sideways. Batchmode:
//   -executeMethod CoSimWheelsCheck.Run
public static class CoSimWheelsCheck
{
    const string Prefab = "Assets/Prefabs/NeoRacer/NeoRacer.prefab";
    const float Step = 0.002f; // m of rear axle travel per frame
    const int Steps = 60;
    const float MaxSlip = 0.1f;
    static readonly string[] Corners = { "FL", "FR", "RL", "RR" };

    public static void Run()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(Prefab);
        CoSimWheels wheels = root.GetComponentInChildren<CoSimWheels>(true);
        VehicleController vc = wheels.Vehicle;
        Transform body = vc.VehicleRigidBody.transform;
        Transform[] w = { vc.FrontLeftWheelTransform, vc.FrontRightWheelTransform, vc.RearLeftWheelTransform, vc.RearRightWheelTransform };
        MethodInfo start = typeof(CoSimWheels).GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo late = typeof(CoSimWheels).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
        start.Invoke(wheels, null);
        float[] radii = new float[4];
        Vector3 axle = Vector3.zero;
        for (int i = 0; i < 4; i++)
        {
            radii[i] = w[i].GetComponent<MeshFilter>().sharedMesh.bounds.extents.y * w[i].lossyScale.y;
            if (i >= 2) axle += body.InverseTransformPoint(w[i].position) / 2;
        }
        wheels.CoSim.enabled = true;
        late.Invoke(wheels, null);

        bool ok = true;
        // Curvature 1/m of the rear axle's path, right turns positive; direction 1 forward, -1 reverse.
        (string name, float curvature, int direction)[] runs =
        {
            ("straight forward", 0f, 1), ("straight reverse", 0f, -1),
            ("left 1 m forward", -1f, 1), ("right 1 m forward", 1f, 1),
            ("left 0.6 m forward", -1f / 0.6f, 1), ("left 1 m reverse", -1f, -1),
        };
        foreach (var run in runs)
        {
            float[] worst = new float[4];
            float[] steer = new float[4];
            for (int s = 0; s < Steps; s++)
            {
                Vector3[] hubs = new Vector3[4];
                Vector3[] contacts = new Vector3[4];
                Vector3[] marks = new Vector3[4];
                for (int i = 0; i < 4; i++)
                {
                    hubs[i] = w[i].position;
                    contacts[i] = hubs[i] - body.up * radii[i];
                    marks[i] = w[i].InverseTransformPoint(contacts[i]); // the tyre's material point now on the ground
                }
                float ds = run.direction * Step;
                if (run.curvature == 0f)
                {
                    body.position += body.forward * ds;
                }
                else
                {
                    Vector3 centre = body.TransformPoint(axle) + body.right / run.curvature;
                    body.RotateAround(centre, Vector3.up, Mathf.Rad2Deg * run.curvature * ds);
                }
                late.Invoke(wheels, null);
                for (int i = 0; i < 4; i++)
                {
                    float hubMove = (w[i].position - hubs[i]).magnitude;
                    float slip = (w[i].TransformPoint(marks[i]) - contacts[i]).magnitude / hubMove;
                    if (s > 0) worst[i] = Mathf.Max(worst[i], slip);
                    steer[i] = Vector3.SignedAngle(body.right, w[i].right, body.up); // the axle turns with steer only
                }
            }
            string line = "WHEELS " + run.name + ":";
            for (int i = 0; i < 4; i++)
            {
                line += " " + Corners[i] + " slip " + worst[i].ToString("F3") + " steer " + steer[i].ToString("F1");
                if (worst[i] > MaxSlip) ok = false;
            }
            Debug.Log(line);
        }
        Debug.Log("WHEELS " + (ok ? "PASS" : "FAIL") + " (contact point moves under " + MaxSlip + " of the hub's step)");
        PrefabUtility.UnloadPrefabContents(root);
        EditorApplication.Exit(ok ? 0 : 1);
    }
}
