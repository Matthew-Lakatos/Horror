#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Eidolon.World;
using Eidolon.Core;

namespace Eidolon.Editor
{
    /// <summary>
    /// Unity Editor Window: Building A Greybox Generator.
    ///
    /// Opens via:  Eidolon → Building A → Generate Greybox
    ///
    /// Creates all 16 rooms as primitive box meshes with correct:
    ///   - Positions and sizes
    ///   - RoomNode components pre-configured (ID, building, type, exits)
    ///   - Trigger collider volumes
    ///   - BuildingAZoneTrigger on key rooms
    ///   - NavMesh Static flags
    ///   - Materials: grey floor, dark wall, coloured safe/med rooms
    ///   - A parent hierarchy matching the design document layout
    ///
    /// Run once on a fresh empty scene. Safe to re-run — detects existing
    /// Building A root and skips duplicate creation.
    /// </summary>
    public class BuildingAGreyboxGenerator : EditorWindow
    {
        // ─── Layout Constants ────────────────────────────────────────────────

        // Floor Y positions
        private const float Y_BASEMENT  = -4.5f;
        private const float Y_GROUND    =  0f;
        private const float Y_UPPER     =  5f;

        // Wall height per floor
        private const float WALL_H      =  4f;

        // ─── Window ──────────────────────────────────────────────────────────

        [MenuItem("Eidolon/Building A/Generate Greybox")]
        public static void ShowWindow()
        {
            var window = GetWindow<BuildingAGreyboxGenerator>("Building A Greybox");
            window.minSize = new Vector2(380, 520);
        }

        private bool _addNavMeshStatic = true;
        private bool _addRoomNodes     = true;
        private bool _addZoneTriggers  = true;
        private bool _addFloorLights   = true;

        private void OnGUI()
        {
            GUILayout.Label("EIDOLON — Building A Greybox Generator", EditorStyles.boldLabel);
            EditorGUILayout.Space(6);

            EditorGUILayout.HelpBox(
                "Generates all 16 rooms as primitive geometry with RoomNode components, " +
                "trigger volumes, and correct exit wiring. Run once on an empty scene.",
                MessageType.Info);

            EditorGUILayout.Space(8);

            _addNavMeshStatic = EditorGUILayout.Toggle("Mark NavMesh Static",    _addNavMeshStatic);
            _addRoomNodes     = EditorGUILayout.Toggle("Add RoomNode Components", _addRoomNodes);
            _addZoneTriggers  = EditorGUILayout.Toggle("Add Zone Triggers",       _addZoneTriggers);
            _addFloorLights   = EditorGUILayout.Toggle("Add Floor Lights",        _addFloorLights);

            EditorGUILayout.Space(12);

            if (GUILayout.Button("GENERATE BUILDING A", GUILayout.Height(36)))
                Generate();

            EditorGUILayout.Space(6);

            if (GUILayout.Button("Clear Building A (destroys all children)", GUILayout.Height(24)))
                ClearExisting();

            EditorGUILayout.Space(12);
            GUILayout.Label("Room Reference", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Ground (Y=0): Lobby, Core Hub, Safe Room, Med Room,\n" +
                "  West Loading Wing, East Processing Hall, Utility Corridor Ring\n\n" +
                "Upper (Y=+5): Stairwell, CCTV Room, Admin Offices, Glass Walkway\n\n" +
                "Basement (Y=-4.5): Stairwell, Generator Room, Maintenance Corridor,\n" +
                "  Drainage Utility, Service Exit\n\n" +
                "Elevator shaft runs through all 3 floors at Core Hub.",
                MessageType.None);
        }

        // ─── Generation ──────────────────────────────────────────────────────

        private void Generate()
        {
            if (FindExistingRoot() != null)
            {
                if (!EditorUtility.DisplayDialog("Building A Greybox",
                        "Building A root already exists. Regenerate?", "Regenerate", "Cancel"))
                    return;
                ClearExisting();
            }

            var root = new GameObject("BuildingA_Root");
            Undo.RegisterCreatedObjectUndo(root, "Generate Building A Greybox");

            var groundRoot   = CreateParent(root, "Ground Floor");
            var upperRoot    = CreateParent(root, "Upper Floor (Mezzanine)");
            var basementRoot = CreateParent(root, "Basement");
            var elevatorRoot = CreateParent(root, "Elevator");

            // ── Define all rooms ────────────────────────────────────────────
            // RoomDef: id, displayLabel, type, building, position, size, exits[]
            var rooms = new List<RoomDef>
            {
                // ── GROUND FLOOR ──────────────────────────────────────────────

                new RoomDef("lobby",
                    "Reception / Lobby", RoomType.AdminOffice, BuildingType.BuildingA,
                    new Vector3(0, Y_GROUND, -14),   new Vector3(10, WALL_H, 8),
                    new[] { "core_hub" }),

                new RoomDef("core_hub",
                    "Core Hub", RoomType.ProcessingFloor, BuildingType.BuildingA,
                    new Vector3(0, Y_GROUND, 0),     new Vector3(14, WALL_H, 14),
                    new[] { "lobby", "safe_room", "med_room", "west_loading", "east_processing",
                             "utility_corridor", "stairwell_up", "basement_stairs" }),

                new RoomDef("safe_room",
                    "Break Room", RoomType.SafeRoom, BuildingType.BuildingA,
                    new Vector3(-10, Y_GROUND, 4),   new Vector3(8, WALL_H, 6),
                    new[] { "core_hub" },
                    isSafe: true),

                new RoomDef("med_room",
                    "Medical Room", RoomType.MedBay, BuildingType.BuildingA,
                    new Vector3(10, Y_GROUND, 4),    new Vector3(7, WALL_H, 6),
                    new[] { "core_hub" }),

                new RoomDef("west_loading",
                    "West Loading Wing", RoomType.StorageVault, BuildingType.BuildingA,
                    new Vector3(-16, Y_GROUND, 0),   new Vector3(14, WALL_H, 18),
                    new[] { "core_hub", "utility_corridor" }),

                new RoomDef("east_processing",
                    "East Processing Hall", RoomType.ProcessingFloor, BuildingType.BuildingA,
                    new Vector3(16, Y_GROUND, 0),    new Vector3(14, WALL_H, 18),
                    new[] { "core_hub", "utility_corridor" }),

                new RoomDef("utility_corridor",
                    "Utility Corridor Ring", RoomType.Corridor, BuildingType.BuildingA,
                    new Vector3(0, Y_GROUND, 10),    new Vector3(28, WALL_H, 4),
                    new[] { "core_hub", "west_loading", "east_processing" }),

                // ── UPPER FLOOR ──────────────────────────────────────────────

                new RoomDef("stairwell_up",
                    "Stairwell — Upper", RoomType.Corridor, BuildingType.BuildingA,
                    new Vector3(0, Y_UPPER, 6),      new Vector3(4, WALL_H, 8),
                    new[] { "core_hub", "cctv_room", "admin_offices", "glass_walkway" }),

                new RoomDef("cctv_room",
                    "CCTV Security Room", RoomType.ControlRoom, BuildingType.BuildingA,
                    new Vector3(-8, Y_UPPER, 2),     new Vector3(8, WALL_H, 7),
                    new[] { "stairwell_up", "admin_offices" }),

                new RoomDef("admin_offices",
                    "Admin Offices", RoomType.AdminOffice, BuildingType.BuildingA,
                    new Vector3(8, Y_UPPER, 2),      new Vector3(10, WALL_H, 7),
                    new[] { "stairwell_up", "cctv_room", "glass_walkway" }),

                new RoomDef("glass_walkway",
                    "Glass Walkway / Overlook", RoomType.Skybridge, BuildingType.BuildingA,
                    new Vector3(0, Y_UPPER, -4),     new Vector3(14, WALL_H, 4),
                    new[] { "stairwell_up", "admin_offices" }),

                // ── BASEMENT ─────────────────────────────────────────────────

                new RoomDef("basement_stairs",
                    "Basement Stairwell", RoomType.Corridor, BuildingType.BuildingA,
                    new Vector3(0, Y_BASEMENT, 8),   new Vector3(4, WALL_H, 8),
                    new[] { "core_hub", "generator_room", "maintenance_corridor" }),

                new RoomDef("generator_room",
                    "Generator Room", RoomType.GeneratorRoom, BuildingType.BuildingA,
                    new Vector3(-8, Y_BASEMENT, 4),  new Vector3(10, WALL_H, 10),
                    new[] { "basement_stairs", "maintenance_corridor" }),

                new RoomDef("maintenance_corridor",
                    "Maintenance Corridor", RoomType.MaintenanceTunnel, BuildingType.BuildingA,
                    new Vector3(6, Y_BASEMENT, 0),   new Vector3(8, WALL_H, 18),
                    new[] { "basement_stairs", "generator_room", "drainage_utility" }),

                new RoomDef("drainage_utility",
                    "Drainage / Utility Space", RoomType.Corridor, BuildingType.BuildingA,
                    new Vector3(12, Y_BASEMENT, -6), new Vector3(7, WALL_H, 7),
                    new[] { "maintenance_corridor", "service_exit" }),

                new RoomDef("service_exit",
                    "Service Exit", RoomType.BreachPoint, BuildingType.BuildingA,
                    new Vector3(18, Y_BASEMENT, -8), new Vector3(5, WALL_H, 5),
                    new[] { "drainage_utility" })
            };

            // ── Build materials ──────────────────────────────────────────────
            var matFloor   = CreateGreyboxMaterial("MAT_Floor",    new Color(0.35f, 0.35f, 0.35f));
            var matWall    = CreateGreyboxMaterial("MAT_Wall",     new Color(0.28f, 0.28f, 0.28f));
            var matSafe    = CreateGreyboxMaterial("MAT_Safe",     new Color(0.25f, 0.45f, 0.25f));
            var matMed     = CreateGreyboxMaterial("MAT_Med",      new Color(0.25f, 0.35f, 0.50f));
            var matHazard  = CreateGreyboxMaterial("MAT_Hazard",   new Color(0.55f, 0.35f, 0.20f));
            var matElevator= CreateGreyboxMaterial("MAT_Elevator", new Color(0.55f, 0.55f, 0.20f));

            // ── Create room geometry ─────────────────────────────────────────
            var createdRooms = new Dictionary<string, GameObject>();

            foreach (var def in rooms)
            {
                Transform parent = def.Position.y < -1f ? basementRoot.transform
                                 : def.Position.y > 1f  ? upperRoot.transform
                                 : groundRoot.transform;

                Material mat = def.IsSafe           ? matSafe
                             : def.Type == RoomType.MedBay ? matMed
                             : def.Type == RoomType.MaintenanceTunnel ? matHazard
                             : matFloor;

                var roomGO = BuildRoom(def, parent, mat, matWall);
                createdRooms[def.Id] = roomGO;

                if (_addRoomNodes)
                    ConfigureRoomNode(roomGO, def);

                if (_addZoneTriggers)
                    ConfigureZoneTrigger(roomGO, def);

                if (_addNavMeshStatic)
                    GameObjectUtility.SetStaticEditorFlags(roomGO,
                        StaticEditorFlags.NavigationStatic | StaticEditorFlags.ContributeGI);

                if (_addFloorLights)
                    AddFloorLight(roomGO, def.Size);
            }

            // ── Elevator shaft ───────────────────────────────────────────────
            BuildElevatorShaft(elevatorRoot, matElevator);

            Debug.Log($"[BuildingAGenerator] Generated {rooms.Count} rooms + elevator shaft.");
            EditorUtility.DisplayDialog("Building A Greybox",
                $"Generated {rooms.Count} rooms.\n\n" +
                "Next steps:\n" +
                "1. Bake NavMesh (Window > AI > Navigation > Bake)\n" +
                "2. Set Ground floor on Elevator configs\n" +
                "3. Wire RoomNode references in WorldStateManager\n" +
                "4. Place enemies on NavMesh",
                "OK");
        }

        // ─── Room Geometry Builder ───────────────────────────────────────────

        private GameObject BuildRoom(RoomDef def, Transform parent, Material floorMat, Material wallMat)
        {
            var root = new GameObject($"Room_{def.Id}");
            root.transform.SetParent(parent, false);
            root.transform.position = def.Position;

            // Floor
            var floor = CreateBox(root.transform, "Floor",
                new Vector3(0, 0, 0),
                new Vector3(def.Size.x, 0.2f, def.Size.z),
                floorMat);

            // Ceiling
            CreateBox(root.transform, "Ceiling",
                new Vector3(0, def.Size.y, 0),
                new Vector3(def.Size.x, 0.2f, def.Size.z),
                wallMat);

            // Walls (4 sides)
            CreateBox(root.transform, "Wall_North",
                new Vector3(0, def.Size.y * 0.5f,  def.Size.z * 0.5f),
                new Vector3(def.Size.x, def.Size.y, 0.2f), wallMat);
            CreateBox(root.transform, "Wall_South",
                new Vector3(0, def.Size.y * 0.5f, -def.Size.z * 0.5f),
                new Vector3(def.Size.x, def.Size.y, 0.2f), wallMat);
            CreateBox(root.transform, "Wall_East",
                new Vector3( def.Size.x * 0.5f, def.Size.y * 0.5f, 0),
                new Vector3(0.2f, def.Size.y, def.Size.z), wallMat);
            CreateBox(root.transform, "Wall_West",
                new Vector3(-def.Size.x * 0.5f, def.Size.y * 0.5f, 0),
                new Vector3(0.2f, def.Size.y, def.Size.z), wallMat);

            // Trigger volume for RoomNode / zone detection
            var triggerGO = new GameObject("TriggerVolume");
            triggerGO.transform.SetParent(root.transform, false);
            triggerGO.transform.localPosition = new Vector3(0, def.Size.y * 0.5f, 0);
            var col = triggerGO.AddComponent<BoxCollider>();
            col.size      = new Vector3(def.Size.x - 0.4f, def.Size.y - 0.2f, def.Size.z - 0.4f);
            col.isTrigger = true;
            triggerGO.layer = LayerMask.NameToLayer("Default");

            return root;
        }

        private void BuildElevatorShaft(GameObject parent, Material mat)
        {
            // Shaft runs from basement to mezzanine at Core Hub position
            var shaft = CreateBox(parent.transform, "ElevatorShaft",
                new Vector3(5, (Y_UPPER + Y_BASEMENT) * 0.5f + WALL_H * 0.5f, 2),
                new Vector3(3.5f, Mathf.Abs(Y_UPPER - Y_BASEMENT) + WALL_H, 3.5f),
                mat);

            // Cabin placeholder
            var cabin = CreateBox(parent.transform, "ElevatorCabin",
                new Vector3(5, Y_GROUND + 0.1f, 2),
                new Vector3(3f, WALL_H - 0.2f, 3f),
                mat);

            // Tag cabin for ElevatorController reference
            cabin.name = "ElevatorCabin_ASSIGN_TO_CONTROLLER";

            // Floor stop markers
            string[] floorNames = { "FloorStop_Basement", "FloorStop_Ground", "FloorStop_Mezzanine" };
            float[]  floorYs    = { Y_BASEMENT, Y_GROUND, Y_UPPER };
            for (int i = 0; i < 3; i++)
            {
                var marker = new GameObject(floorNames[i]);
                marker.transform.SetParent(parent.transform, false);
                marker.transform.position = new Vector3(5, floorYs[i] + 0.1f, 2);

                // Add visible gizmo sphere for editor clarity
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.transform.SetParent(marker.transform, false);
                sphere.transform.localScale = Vector3.one * 0.4f;
                var r = sphere.GetComponent<Renderer>();
                if (r) r.sharedMaterial = mat;
                if (Application.isPlaying) DestroyImmediate(sphere.GetComponent<SphereCollider>());
            }
        }

        // ─── Component Configuration ─────────────────────────────────────────

        private void ConfigureRoomNode(GameObject roomGO, RoomDef def)
        {
            var triggerGO = roomGO.transform.Find("TriggerVolume");
            if (triggerGO == null) return;

            var node = triggerGO.gameObject.AddComponent<RoomNode>();

            // Use SerializedObject to set private serialized fields
            var so = new SerializedObject(node);
            so.FindProperty("_roomId").stringValue      = def.Id;
            so.FindProperty("_displayLabel").stringValue = def.DisplayLabel;
            so.FindProperty("_roomType").enumValueIndex  = (int)def.Type;
            so.FindProperty("_building").enumValueIndex  = (int)def.Building;
            so.FindProperty("_isSafeRoom").boolValue     = def.IsSafe;

            // Wire exits list
            var exitsProp = so.FindProperty("_exits");
            exitsProp.ClearArray();
            for (int i = 0; i < def.Exits.Length; i++)
            {
                exitsProp.InsertArrayElementAtIndex(i);
                exitsProp.GetArrayElementAtIndex(i).stringValue = def.Exits[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private void ConfigureZoneTrigger(GameObject roomGO, RoomDef def)
        {
            BuildingAZoneTrigger.ZoneType zone = def.Id switch
            {
                "core_hub"   => BuildingAZoneTrigger.ZoneType.CoreHub,
                "safe_room"  => BuildingAZoneTrigger.ZoneType.SafeRoom,
                "med_room"   => BuildingAZoneTrigger.ZoneType.MedRoom,
                "east_processing" => BuildingAZoneTrigger.ZoneType.EastWing,
                _ => BuildingAZoneTrigger.ZoneType.Other
            };

            if (zone == BuildingAZoneTrigger.ZoneType.Other) return;

            var triggerGO = roomGO.transform.Find("TriggerVolume");
            if (triggerGO == null) return;

            var trigger = triggerGO.gameObject.AddComponent<BuildingAZoneTrigger>();
            var so      = new SerializedObject(trigger);
            so.FindProperty("_zone").enumValueIndex = (int)zone;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private void AddFloorLight(GameObject roomGO, Vector3 roomSize)
        {
            var lightGO = new GameObject("FloorLight");
            lightGO.transform.SetParent(roomGO.transform, false);
            lightGO.transform.localPosition = new Vector3(0, roomSize.y - 0.5f, 0);

            var light = lightGO.AddComponent<Light>();
            light.type      = LightType.Point;
            light.range     = Mathf.Max(roomSize.x, roomSize.z) * 0.8f;
            light.intensity = 0.8f;
            light.color     = new Color(0.95f, 0.90f, 0.80f); // warm industrial
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private GameObject CreateParent(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        private GameObject CreateBox(Transform parent, string name, Vector3 localPos,
                                      Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale    = size;

            var r = go.GetComponent<Renderer>();
            if (r && mat) r.sharedMaterial = mat;

            return go;
        }

        private Material CreateGreyboxMaterial(string name, Color color)
        {
            var mat   = new Material(Shader.Find("Standard"));
            mat.name  = name;
            mat.color = color;
            mat.SetFloat("_Glossiness", 0.05f);
            mat.SetFloat("_Metallic",   0.0f);
            return mat;
        }

        private void ClearExisting()
        {
            var existing = FindExistingRoot();
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
                Debug.Log("[BuildingAGenerator] Cleared existing Building A root.");
            }
        }

        private GameObject FindExistingRoot()
            => GameObject.Find("BuildingA_Root");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ROOM DEFINITION — Internal data struct for generator
    // ─────────────────────────────────────────────────────────────────────────

    internal class RoomDef
    {
        public string     Id;
        public string     DisplayLabel;
        public RoomType   Type;
        public BuildingType Building;
        public Vector3    Position;
        public Vector3    Size;
        public string[]   Exits;
        public bool       IsSafe;

        public RoomDef(string id, string label, RoomType type, BuildingType building,
                       Vector3 pos, Vector3 size, string[] exits, bool isSafe = false)
        {
            Id           = id;
            DisplayLabel = label;
            Type         = type;
            Building     = building;
            Position     = pos;
            Size         = size;
            Exits        = exits;
            IsSafe       = isSafe;
        }
    }
}
#endif
