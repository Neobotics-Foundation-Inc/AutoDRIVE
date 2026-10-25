using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

// Renders a drive on the Purdue testbed as video frames at the simulator's Ultra quality:
// the two-car scene (NeoRacerTwoCars), each car where the odometry logged from its ROS
// domain during a run puts it (a CSV of wall time, x, y, yaw per /odom message, the
// AutoDRIVE bridge's odometry: metres and radians from where the car started, x forward and
// y left), its wheels rolling and steering as CoSimWheels turns a posed car's. Seen by a
// fixed camera across the mat from its near-left corner, where the track's photo was taken.
// Batchmode, with graphics (not -nographics):
//   -executeMethod NeoRacerReplayVideo.Run -neoracerOdom car1.csv car2.csv
//   -neoracerFrom 10 -neoracerTo 32 -neoracerFrames <folder>
// From and to are seconds after the first logged message; frames are 60 a second.
public static class NeoRacerReplayVideo
{
    const string Scene = "Assets/Scenes/NeoRacer - Purdue Testbed - Two Cars.unity";
    const int Fps = 60;
    const int Width = 1920, Height = 1080;
    const int Settle = 30; // renders of the first frame, for exposure to settle

    // A logged drive: times, and the car's odometry at each
    class Drive
    {
        public readonly List<double> T = new List<double>();
        public readonly List<Vector3> Pose = new List<Vector3>(); // x, y, yaw (unwrapped)

        public static Drive Load(string path)
        {
            var d = new Drive();
            foreach (string line in File.ReadAllLines(path))
            {
                string[] f = line.Split(',');
                if (f[0] == "t") continue;
                float yaw = float.Parse(f[3], CultureInfo.InvariantCulture);
                if (d.Pose.Count > 0) yaw = d.Pose[d.Pose.Count - 1].z + Mathf.DeltaAngle(d.Pose[d.Pose.Count - 1].z * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                d.T.Add(double.Parse(f[0], CultureInfo.InvariantCulture));
                d.Pose.Add(new Vector3(float.Parse(f[1], CultureInfo.InvariantCulture), float.Parse(f[2], CultureInfo.InvariantCulture), yaw));
            }
            return d;
        }

        // The odometry at wall time t: the first before the drive's start, the last after its end
        public Vector3 At(double t)
        {
            int i = T.BinarySearch(t);
            if (i >= 0) return Pose[i];
            i = ~i;
            if (i == 0) return Pose[0];
            if (i == T.Count) return Pose[T.Count - 1];
            float u = (float)((t - T[i - 1]) / (T[i] - T[i - 1]));
            return Vector3.Lerp(Pose[i - 1], Pose[i], u);
        }
    }

    // A car of the scene, posed from its odometry, its wheels turned as CoSimWheels turns them
    class Car
    {
        readonly VehicleController vehicle;
        readonly Transform body;
        readonly Vector3 start; // body's place at the start
        readonly Quaternion startRotation;
        readonly Vector3 sensor; // where the odometry is measured (the GNSS receiver), in the body's frame
        readonly Vector3 sensorStart;
        readonly Transform[] wheels;
        readonly Vector3[] rest = new Vector3[4];
        readonly Quaternion[] restRotations = new Quaternion[4];
        readonly float[] radii = new float[4];
        readonly float[] spins = new float[4];
        readonly Vector3[] lastCentres = new Vector3[4];
        readonly Vector3 axle;
        Vector3 windowAxle;
        float windowYaw, curvature;
        bool posed;

        public Car(VehicleController vehicle, GPS gnss)
        {
            this.vehicle = vehicle;
            body = vehicle.VehicleRigidBody.transform;
            start = body.position;
            startRotation = body.rotation;
            sensorStart = gnss.VehicleTransform.position;
            sensor = body.InverseTransformPoint(sensorStart);
            wheels = new[] { vehicle.FrontLeftWheelTransform, vehicle.FrontRightWheelTransform,
                             vehicle.RearLeftWheelTransform, vehicle.RearRightWheelTransform };
            for (int i = 0; i < 4; i++)
            {
                rest[i] = body.InverseTransformPoint(wheels[i].position);
                restRotations[i] = Quaternion.Inverse(body.rotation) * wheels[i].rotation;
                radii[i] = wheels[i].GetComponent<MeshFilter>().sharedMesh.bounds.extents.y * wheels[i].lossyScale.y;
            }
            axle = (rest[2] + rest[3]) / 2;
        }

        // Odometry (x forward, y left, yaw anticlockwise from the start) to the body's pose
        public void Pose(Vector3 odometry)
        {
            Quaternion rotation = startRotation * Quaternion.Euler(0f, -odometry.z * Mathf.Rad2Deg, 0f);
            Vector3 receiver = sensorStart + startRotation * new Vector3(-odometry.y, 0f, odometry.x);
            Vector3 place = receiver - rotation * sensor;
            body.SetPositionAndRotation(new Vector3(place.x, start.y, place.z), rotation);
            TurnWheels();
        }

        void TurnWheels()
        {
            Vector3 axleNow = body.TransformPoint(axle);
            float yaw = body.eulerAngles.y;
            if (!posed)
            {
                windowAxle = axleNow;
                windowYaw = yaw;
            }
            else
            {
                float ds = Vector3.Dot(axleNow - windowAxle, body.forward);
                if (Mathf.Abs(ds) >= CoSimWheels.TurnWindow)
                {
                    curvature = Mathf.DeltaAngle(windowYaw, yaw) * Mathf.Deg2Rad / ds;
                    windowAxle = axleNow;
                    windowYaw = yaw;
                }
            }
            for (int i = 0; i < 4; i++)
            {
                Vector3 offset = rest[i] - axle;
                float steer = i < 2 ? Mathf.Clamp(Mathf.Rad2Deg * Mathf.Atan2(offset.z * curvature, 1 - offset.x * curvature),
                                                  -vehicle.SteeringLimit, vehicle.SteeringLimit) : 0f;
                Quaternion heading = body.rotation * Quaternion.Euler(0, steer, 0);
                Vector3 centre = body.TransformPoint(rest[i]);
                if (posed)
                {
                    float roll = Vector3.Dot(centre - lastCentres[i], heading * Vector3.forward) / radii[i];
                    spins[i] = Mathf.Repeat(spins[i] + roll * Mathf.Rad2Deg, 360f);
                }
                wheels[i].SetPositionAndRotation(centre, heading * Quaternion.Euler(spins[i], 0, 0) * restRotations[i]);
                lastCentres[i] = centre;
            }
            posed = true;
        }
    }

    public static void Run()
    {
        string[] args = Environment.GetCommandLineArgs();
        int odom = Array.IndexOf(args, "-neoracerOdom");
        var drives = new[] { Drive.Load(args[odom + 1]), Drive.Load(args[odom + 2]) };
        float from = float.Parse(args[Array.IndexOf(args, "-neoracerFrom") + 1], CultureInfo.InvariantCulture);
        float to = float.Parse(args[Array.IndexOf(args, "-neoracerTo") + 1], CultureInfo.InvariantCulture);
        string dir = args[Array.IndexOf(args, "-neoracerFrames") + 1];
        Directory.CreateDirectory(dir);

        EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
        // Ultra, as the simulator's quality button sets it: Low, High, then Ultra
        foreach (RenderingQuality quality in UnityEngine.Object.FindObjectsOfType<RenderingQuality>(true))
        {
            quality.ToggleRenderingQuality();
            quality.ToggleRenderingQuality();
        }
        // the cars in the socket's order: vehicle 1 is car 1, in ROS domain 1
        Socket socket = UnityEngine.Object.FindObjectOfType<Socket>(true);
        var cars = new Car[2];
        for (int i = 0; i < 2; i++) cars[i] = new Car(socket.VehicleControllers[i], socket.PositioningSystems[i]);

        var go = new GameObject("Video Camera");
        Camera cam = go.AddComponent<Camera>();
        HDAdditionalCameraData hd = go.AddComponent<HDAdditionalCameraData>();
        hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 500f;
        // the camera, in the lane map's frame: eye x, y and height, the point it looks at, and
        // its vertical field of view in degrees
        float[] c = { -0.6f, -1.4f, 4.2f, 2.45f, 2.4f, 48f };
        int given = Array.IndexOf(args, "-neoracerCamera");
        if (given >= 0)
            for (int i = 0; i < c.Length; i++) c[i] = float.Parse(args[given + 1 + i], CultureInfo.InvariantCulture);
        Vector3 eye = new Vector3(-c[1], c[2], c[0]), target = new Vector3(-c[4], 0f, c[3]);
        cam.fieldOfView = c[5];
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
        // a fixed exposure (EV100): the scene's own adapts to what is in view, and from these
        // stills (no sky drawn: the sky is not computed outside the player) it swings with
        // the framing
        int exposureAt = Array.IndexOf(args, "-neoracerExposure");
        float ev = exposureAt >= 0 ? float.Parse(args[exposureAt + 1], CultureInfo.InvariantCulture) : 13f;
        var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
        Exposure exposure = profile.Add<Exposure>(true);
        exposure.mode.Override(ExposureMode.Fixed);
        exposure.fixedExposure.Override(ev);
        var volume = new GameObject("Video Exposure").AddComponent<UnityEngine.Rendering.Volume>();
        volume.isGlobal = true;
        volume.priority = 1000f;
        volume.sharedProfile = profile;

        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        cam.targetTexture = rt;
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);

        double t0 = Math.Min(drives[0].T[0], drives[1].T[0]);
        int frames = Mathf.RoundToInt((to - from) * Fps);
        for (int k = 0; k < frames; k++)
        {
            double t = t0 + from + (double)k / Fps;
            for (int i = 0; i < 2; i++) cars[i].Pose(drives[i].At(t));
            for (int r = 0; r < (k == 0 ? Settle : 1); r++) cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, "frame_" + k.ToString("D5") + ".png"), tex.EncodeToPNG());
        }
        Debug.Log("REPLAY " + frames + " frames, " + from + " to " + to + " s, into " + dir);
        EditorApplication.Exit(0);
    }
}
