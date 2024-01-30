using HarmonyLib;
using SRML;
using SRML.Console;
using SRML.Utils.Enum;
using SRML.SR;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
using System.Json;
using UnityEngine;
using MonomiPark.SlimeRancher.Regions;
using AssetsLib;
using TMPro;
using Console = SRML.Console.Console;
using Object = UnityEngine.Object;

namespace DroneNetworkDevTool
{
    public class Main : ModEntryPoint
    {
        internal static Assembly modAssembly = Assembly.GetExecutingAssembly();
        internal static string modName = $"{modAssembly.GetName().Name}";
        internal static string modDir = $"{System.Environment.CurrentDirectory}\\SRML\\Mods\\{modName}";
        public override void PreLoad()
        {
            HarmonyInstance.PatchAll();
            foreach (var t in modAssembly.GetTypes())
                if (t.IsSubclassOf(typeof(ConsoleCommand)))
                    Console.RegisterCommand((ConsoleCommand)t.GetConstructor(new Type[0]).Invoke(new object[0]));
            SRCallbacks.OnActorSpawn += (x, y, z) => {
                if (x == Identifiable.Id.PLAYER && !EditHandler.Instance)
                {
                    y.AddComponent<EditHandler>();
                    var ui = new GameObject("crosshairUI", typeof(RectTransform), typeof(EditUI)).GetComponent<EditUI>();
                    ui.transform.SetParent(HudUI.Instance.uiContainer.transform.Find("crossHair"), false);
                }
            };
        }
        public static void Log(object message) => Debug.Log($"[{modName}]: " + message);
        public static void LogError(object message) => Debug.LogError($"[{modName}]: " + message);
        public static void LogWarning(object message) => Debug.LogWarning($"[{modName}]: " + message);
        public static void LogSuccess(object message) => Debug.LogAssertion($"[{modName}]: " + message);
        public static Mesh arrowMesh;
        public static Mesh nodeMesh;
        public static Material readonlyMaterial;
        public static Material material;
        public static Material selectedMaterial;
        static Main()
        {
            nodeMesh = new Mesh();
            nodeMesh.vertices = new Vector3[]
            {
                new Vector3(0.5f,0.5f,0.5f),
                new Vector3(0.5f,0.5f,-0.5f),
                new Vector3(0.5f,-0.5f,0.5f),
                new Vector3(-0.5f,0.5f,0.5f),
                new Vector3(0.5f,-0.5f,-0.5f),
                new Vector3(-0.5f,0.5f,-0.5f),
                new Vector3(-0.5f,-0.5f,0.5f),
                new Vector3(-0.5f,-0.5f,-0.5f)
            };
            nodeMesh.triangles = new int[]
            {
                0,1,3,1,3,5,
                6,4,2,7,6,4,
                0,1,2,1,2,4,
                6,5,3,7,6,5,
                0,2,3,2,3,6,
                5,4,1,7,5,4
            };
            nodeMesh.uv = new Vector2[]
            {
                new Vector2(1,1),
                new Vector2(0.5f,0.75f),
                new Vector2(0,0),
                new Vector2(0.5f,0.75f),
                new Vector2(0.5f,0.25f),
                new Vector2(1,1),
                new Vector2(0.5f,0.25f),
                new Vector2(0,0)
            };
            nodeMesh.RecalculateBounds();
            nodeMesh.RecalculateNormals();
            nodeMesh.RecalculateTangents();

            var ver = new List<Vector3>() { Vector3.forward * 0.5f };
            var tri = new List<int>();
            var uvs = new List<Vector2>() { Vector2.one * 0.5f };
            for (int i = 0; i < 16; i++)
            {
                ver.Add(Vector3.back * 0.5f + (Vector3.up * 0.5f).Rotate(0, 0, 360 / 16f * i));
                tri.AddRange(new[] { 0, i + 1, (i + 1).Mod(16) + 1 });
                uvs.Add(Vector2.one * 0.5f + (Vector2.up * 0.5f).Rotate(360 / 16f * -i));
                if (i > 0)
                    tri.AddRange(new[] { 1, (i + 1).Mod(16) + 1, i + 1 });
            }
            arrowMesh = new Mesh();
            arrowMesh.vertices = ver.ToArray();
            arrowMesh.triangles = tri.ToArray();
            arrowMesh.uv = uvs.ToArray();

            material = new Material(Shader.Find("SR/Particles/Additive (Soft)"));
            var t = new Texture2D(1, 1);
            material.mainTexture = t;
            t.SetPixel(0, 0, new Color(1, 1, 1, 0.2f));
            t.Apply();

            selectedMaterial = new Material(Shader.Find("SR/Particles/Additive (Soft)"));
            t = new Texture2D(1, 1);
            selectedMaterial.mainTexture = t;
            t.SetPixel(0, 0, new Color(0, 1, 0, 0.2f));
            t.Apply();

            readonlyMaterial = new Material(Shader.Find("SR/Particles/Additive (Soft)"));
            t = new Texture2D(1, 1);
            readonlyMaterial.mainTexture = t;
            t.SetPixel(0, 0, new Color(1, 0, 1, 0.2f));
            t.Apply();
        }
    }

    [EnumHolder]
    public static class Ids
    {
        public static readonly WeaponVacuum.VacMode DRONEDEV;
    }

    class CustomCommand : ConsoleCommand
    {
        public override string Usage => "togglenetworkingmode [editExistingNodes]";
        public override string ID => "togglenetworkingmode";
        public override string Description => "Toggles the Drone Network Development Tool";
        public override bool Execute(string[] args)
        {
            var handler = EditHandler.Instance;
            if (!handler?.vacuum)
            {
                Main.LogError("This command can only be used in-world");
                return true;
            }
            if (handler.vacuum.vacMode == Ids.DRONEDEV)
            {
                handler.Deactivate();
                Main.Log("Edit mode disabled");
                return true;

            }
            if (!Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out RaycastHit hit, 100, -1))
            {
                Main.LogError("Must be looking at an object");
                return true;
            }
            var r = hit.collider.GetComponentInParent<Region>();
            if (!r)
            {
                var m = hit.collider.GetComponentInParent<RegionMember>();
                r = m?.regions?.FirstOrDefault();
            }
            if (!r)
            {
                Main.LogError("Object must belong to a region");
                return true;
            }
            if (args != null && args.Length > 0 && bool.TryParse(args[0],out var flag))
                handler.Activate(r,flag);
            handler.Activate(r);
            Main.Log("Edit mode enabled on " + r.name);
            return true;
        }
    }

    class CustomCommand2 : ConsoleCommand
    {
        public override string Usage => "createnode <name>";
        public override string ID => "createnode";
        public override string Description => "Creates a Drone Network Development Tool node";
        public override bool Execute(string[] args)
        {
            var handler = EditHandler.Instance;
            if (!handler?.vacuum)
            {
                Main.LogError("This command can only be used in-world");
                return true;
            }
            if (args == null)
                args = new string[0];
            if (args.Length < 1)
            {
                Main.LogError("Not enough arguments");
                return false;
            }
            if (handler.vacuum.vacMode != Ids.DRONEDEV)
            {
                Main.LogError("Edit mode is disabled");
                return true;
            }
            if (handler.IsHolding)
            {
                Main.LogError("Cannot create a new node while editing something else");
                return true;
            }
            if (handler.TryCreateNode(args[0]))
            {
                Main.Log("Created node " + args[0]);
                return true;
            }
            Main.LogError("Node already exists with the name " + args[0]);
            return true;
        }
    }

    class CustomCommand3 : ConsoleCommand
    {
        public override string Usage => "savenodes <file> [includeReadOnly]";
        public override string ID => "savenodes";
        public override string Description => "Saves the nodes created with the Drone Network Development Tools to a file";
        public override bool Execute(string[] args)
        {
            var handler = EditHandler.Instance;
            if (!handler?.vacuum)
            {
                Main.LogError("This command can only be used in-world");
                return true;
            }
            if (args == null)
                args = new string[0];
            if (args.Length < 1)
            {
                Main.LogError("Not enough arguments");
                return false;
            }
            if (handler.IsHolding)
            {
                Main.LogError("Cannot save while editing");
                return true;
            }
            if (args.Length > 1 && !bool.TryParse(args[1], out var flag))
                handler.Save(args[0], flag);
            handler.Save(args[0]);
            return true;
        }
    }

    public class EditHandler : SRSingleton<EditHandler>
    {
        WeaponVacuum _v;
        public WeaponVacuum vacuum
        {
            get
            {
                if (!_v)
                    _v = GetComponent<TeleportablePlayer>().weaponVacuum;
                return _v;
            }
        }
        List<DummyDroneNode> currentNodes = new List<DummyDroneNode>();
        Region active;
        Material material;
        DummyDroneNode heldNode;
        DummyDroneConnection heldConnection;
        public bool IsHolding => heldConnection || heldNode;
        float holdingDist;
        public override void Awake()
        {
            base.Awake();
            material = new Material(Shader.Find("SR/Particles/Additive (Soft)"));
            var t = new Texture2D(1, 1);
            material.mainTexture = t;
            t.SetPixel(0, 0, Color.red);
            t.Apply();
        }
        DummyDroneObject lastLooking;
        bool wasActive = false;
        void Update()
        {
            if (vacuum.vacMode != Ids.DRONEDEV)
            {
                if (wasActive)
                    Deactivate();
                if (heldConnection)
                    Destroy(heldConnection.gameObject);
                if (heldNode)
                    Destroy(heldNode.gameObject);
            }
            else
            {
                wasActive = true;
                if (heldConnection)
                {
                    var hits = Physics.RaycastAll(Camera.main.transform.position, Camera.main.transform.forward, 100, 1 << vp_Layer.RaycastOnly);
                    RaycastHit? hit = null;
                    foreach ( var h in hits)
                        if ((hit == null || hit.Value.distance > h.distance) && h.collider.GetComponent<DummyDroneNode>())
                            hit = h;
                    if (hit != null)
                        heldConnection.Target = hit.Value.collider.GetComponent<DummyDroneNode>();
                    else
                        heldConnection.TargetPosition = Camera.main.transform.position + Camera.main.transform.forward * holdingDist;
                    if (SRInput.Actions.attack.WasPressed)
                    {
                        if (heldConnection.Target)
                        {
                            heldConnection.Material = Main.material;
                            heldConnection = null;
                        }
                        else
                            Destroy(heldConnection.gameObject);
                    }
                    lastLooking = null;
                }
                else if (heldNode)
                {
                    heldNode.Position = Camera.main.transform.position + Camera.main.transform.forward * holdingDist;
                    if (SRInput.Actions.vac.WasPressed)
                    {
                        heldNode.Material = Main.material;
                        heldNode = null;
                    }
                    else if (SRInput.Actions.attack.WasPressed)
                        Destroy(heldNode.gameObject);
                    lastLooking = null;
                }
                else if (!IsHolding)
                {
                    DummyDroneObject looking = null;
                    if (Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out var hit, 100, 1 << vp_Layer.RaycastOnly))
                        looking = hit.collider.GetComponent<DummyDroneObject>();
                    if (looking != lastLooking)
                    {
                        if (lastLooking)
                            lastLooking.Material = lastLooking.ReadOnly ? Main.readonlyMaterial : Main.material;
                        if (looking)
                            looking.Material = Main.selectedMaterial;
                        lastLooking = looking;
                    }
                    if (lastLooking && !lastLooking.ReadOnly)
                    {
                        if (SRInput.Actions.vac.WasPressed && lastLooking is DummyDroneNode)
                        {
                            heldNode = lastLooking as DummyDroneNode;
                            holdingDist = (heldNode.Position - Camera.main.transform.position).magnitude;
                        }
                        else if (SRInput.Actions.attack.WasPressed)
                        {
                            if (lastLooking is DummyDroneConnection)
                            {
                                heldConnection = lastLooking as DummyDroneConnection;
                                holdingDist = hit.distance;
                            }
                            else if (lastLooking is DummyDroneNode)
                            {
                                lastLooking.Material = Main.material;
                                heldConnection = (lastLooking as DummyDroneNode).CreateConnection();
                                heldConnection.Material = Main.selectedMaterial;
                                holdingDist = ((lastLooking as DummyDroneNode).Position - Camera.main.transform.position).magnitude;
                            }
                        }
                    }
                }
                if (heldConnection || lastLooking is DummyDroneConnection)
                {
                    EditUI.Instance.text.text = (heldConnection ? heldConnection : lastLooking as DummyDroneConnection).Length.ToString();
                    if (heldConnection)
                        EditUI.Instance.text2.text = "Shoot to " + (heldConnection.Target ? "place" : "destroy") + " the connection";
                    else if (!lastLooking.ReadOnly)
                        EditUI.Instance.text2.text = "Shoot to pickup the connection";
                    else
                        EditUI.Instance.text2.text = "";
                }
                else if (heldNode || lastLooking is DummyDroneNode)
                {
                    EditUI.Instance.text.text = (heldNode ? heldNode : lastLooking).name;
                    if (heldNode)
                        EditUI.Instance.text2.text = "Shoot to destroy the node\nVac to place the node";
                    else if (lastLooking && !lastLooking.ReadOnly && lastLooking is DummyDroneNode)
                        EditUI.Instance.text2.text = "Shoot to create a new connection\nVac to pickup the node";
                    else
                        EditUI.Instance.text2.text = "";
                }
                else
                {
                    EditUI.Instance.text.text = "";
                    EditUI.Instance.text2.text = "";
                }
            }
        }
        public void Activate(Region cell,bool allowEditingExisting = false)
        {
            if (active == cell)
                return;
            if (active)
                Deactivate();
            active = cell;
            vacuum.vacMode = Ids.DRONEDEV;
            if (cell.GetComponent<DroneNetwork>())
            {
                var dtn = new Dictionary<PathingNetworkNode, DummyDroneNode>();
                foreach (var n in cell.GetComponent<DroneNetwork>().pather.nodes)
                {
                    var dn = Instantiate(DummyDroneNode.nodePrefab, null, false);
                    dn.name = n.name;
                    currentNodes.Add(dn);
                    dtn.Add(n, dn);
                    dn.Position = n.position;
                }
                foreach (var p in dtn)
                {
                    foreach (var c in p.Key.connections)
                        p.Value.CreateConnection(dtn[c]);
                    if (!allowEditingExisting)
                        p.Value.ReadOnly = true;
                }

            }
        }
        public void Deactivate()
        {
            foreach (var n in currentNodes)
                Destroy(n.gameObject);
            currentNodes.Clear();
            active = null;
            wasActive = false;
            if (vacuum.vacMode == Ids.DRONEDEV)
                vacuum.vacMode = WeaponVacuum.VacMode.NONE;
        }
        public bool TryCreateNode(string name)
        {
            if (currentNodes.Exists((x) => x.name == name))
                return false;
            heldNode = Instantiate(DummyDroneNode.nodePrefab);
            heldNode.Material = Main.selectedMaterial;
            holdingDist = 3;
            heldNode.name = name;
            currentNodes.Add(heldNode);
            return true;
        }

        public void Save(string filename, bool includeReadonly = false)
        {
            var main = new JsonObject();
            foreach (var n in currentNodes)
            {
                if (!n)
                    continue;
                if (!includeReadonly && n.ReadOnly)
                    continue;
                var node = new JsonObject();
                var pos = new JsonArray();
                pos.AddRange(n.Position.x,n.Position.y,n.Position.z);
                node.Add("position", pos);
                var connects = new JsonArray();
                foreach (var c in n.connections)
                    if (c?.Target)
                        connects.Add(c.Target.name);
                node.Add("connections", connects);
                main.Add(n.name, node);
            }
            System.IO.File.WriteAllText(filename, main.ToString());
            //main.Save(new System.IO.StreamWriter(filename,));
        }
    }
    class EditUI : SRSingleton<EditUI>
    {
        ScaleAnimator uiScaler;
        public TMP_Text text;
        public TMP_Text text2;
        RectTransform textRect;
        RectTransform textRect2;
        public override void Awake()
        {
            base.Awake();
            uiScaler = gameObject.AddComponent<ScaleAnimator>();
            uiScaler.AnimationTime = 0.4f;
            var rect = GetComponent<RectTransform>();
            rect.sizeDelta = Vector2.zero;
            rect.anchorMin = -Vector2.one;
            rect.anchorMax = Vector2.one;
            text = Instantiate(SRSingleton<HudUI>.Instance.currencyText, rect, false).GetComponent<TMP_Text>();
            text.fontSize /= 2;
            textRect = text.GetComponent<RectTransform>();
            textRect.sizeDelta = Vector2.zero;
            textRect.anchorMin = new Vector2(0, 0.75f);
            textRect.anchorMax = new Vector2(0, 0.75f);
            text.lineSpacing = 0;
            text.autoSizeTextContainer = false;
            text2 = Instantiate(SRSingleton<HudUI>.Instance.currencyText, rect, false).GetComponent<TMP_Text>();
            text2.fontSize /= 2;
            textRect2 = text2.GetComponent<RectTransform>();
            textRect2.sizeDelta = Vector2.zero;
            textRect2.anchorMin = new Vector2(1, 0.75f);
            textRect2.anchorMax = new Vector2(1, 0.75f);
            text2.lineSpacing = 0;
            text2.autoSizeTextContainer = false;
        }
        void Update()
        {
            uiScaler.SetTarget(EditHandler.Instance?.vacuum?.vacMode == Ids.DRONEDEV ? Vector3.one : Vector3.zero);
            textRect.offsetMin = new Vector2(-text.preferredWidth, -text.preferredHeight / 2);
            textRect.offsetMax = new Vector2(0, text.preferredHeight / 2);
            textRect2.offsetMax = new Vector2(text2.preferredWidth, text2.preferredHeight / 2);
            textRect2.offsetMin = new Vector2(0, -text2.preferredHeight / 2);
        }
    }

    abstract class DummyDroneObject : MonoBehaviour { public abstract Material Material { get; set; } public abstract bool ReadOnly { get; set; } }

    class DummyDroneNode : DummyDroneObject
    {
        static DummyDroneNode _np;
        public static DummyDroneNode nodePrefab
        {
            get
            {
                if (!_np)
                {
                    var g = new GameObject("dummyNode").CreatePrefab();
                    g.AddComponent<MeshRenderer>().sharedMaterial = Main.material;
                    g.AddComponent<MeshFilter>().sharedMesh = Main.nodeMesh;
                    var c = g.AddComponent<BoxCollider>();
                    c.size = Vector3.one;
                    c.isTrigger = false;
                    c.enabled = true;
                    g.layer = vp_Layer.RaycastOnly;

                    _np = g.AddComponent<DummyDroneNode>();
                }
                return _np;
            }
        }
        public List<DummyDroneConnection> connections = new List<DummyDroneConnection>();
        public List<DummyDroneConnection> inheritConnections = new List<DummyDroneConnection>();
        Vector3 position;
        bool readOnly;
        public Vector3 Position
        {
            get => position;
            set
            {
                if (readOnly)
                    throw new Exception("readonly");
                transform.position = value;
                position = value;
            }
        }
        public override bool ReadOnly
        {
            get => readOnly;
            set
            {
                if (readOnly)
                    throw new Exception("readonly");
                readOnly = value;
                foreach (var c in connections)
                    if (c && !c.ReadOnly)
                        c.ReadOnly = value;
                if (readOnly && Material == Main.material)
                    Material = Main.readonlyMaterial;
            }
        }
        Material _m = Main.material;
        public override Material Material
        {
            get => _m;
            set
            {
                if (_m == value)
                    return;
                _m = value;
                GetComponent<MeshRenderer>().sharedMaterial = _m;
                foreach (var c in connections)
                    if (c && !c.ReadOnly)
                        c.Material = _m;
            }
        }
        public void CreateConnection(DummyDroneNode Target) => Instantiate(DummyDroneConnection.connectPrefab, transform, false).Target = Target;
        public DummyDroneConnection CreateConnection() {
            var c = Instantiate(DummyDroneConnection.connectPrefab, transform, false);
            c.TargetPosition = transform.position;
            return c;
        }
        void OnDestroy()
        {
            foreach (var n in inheritConnections)
                Destroy(n?.gameObject);
        }
    }
    class DummyDroneConnection : DummyDroneObject
    {
        static DummyDroneConnection _cp;
        public static DummyDroneConnection connectPrefab
        {
            get
            {
                if (!_cp)
                {
                    var g = new GameObject("dummyConnection").CreatePrefab();
                    var c = g.AddComponent<CapsuleCollider>();
                    c.height = 0;
                    c.radius = 0.2f;
                    c.isTrigger = false;
                    c.enabled = true;
                    c.direction = 2;
                    g.layer = vp_Layer.RaycastOnly;
                    _cp = g.AddComponent<DummyDroneConnection>();
                }
                return _cp;
            }
        }
        static Transform _ap;
        public static Transform arrowPrefab
        {
            get
            {
                if (!_ap)
                {
                    var g = new GameObject("dummyConnection").CreatePrefab();
                    g.AddComponent<MeshFilter>().sharedMesh = Main.arrowMesh;
                    g.AddComponent<MeshRenderer>().sharedMaterial = Main.material;
                    g.transform.localScale = Vector3.one * 0.2f;
                    _ap = g.transform;
                }
                return _ap;
            }
        }
        Vector3 targetPos;
        DummyDroneNode targetNode;
        public DummyDroneNode Target
        {
            get => targetNode;
            set
            {
                if (readOnly)
                    throw new Exception("readonly");
                targetNode?.inheritConnections.Remove(this);
                targetNode = value;
                targetNode?.inheritConnections.Add(this);
            }
        }
        public Vector3 TargetPosition
        {
            get => targetNode?.Position ?? targetPos;
            set
            {
                if (readOnly)
                    throw new Exception("readonly");
                targetNode = null;
                targetPos = value;
            }
        }
        bool readOnly;
        public override bool ReadOnly
        {
            get => readOnly;
            set
            {
                if (readOnly)
                    throw new Exception("readonly");
                readOnly = value;
                if (readOnly && Material == Main.material)
                    Material = Main.readonlyMaterial;
            }
        }
        DummyDroneNode parent;
            void Awake()
            {
                parent = GetComponentInParent<DummyDroneNode>();
                parent?.connections.Add(this);
            }

        Vector3 lastPos;
        Vector3 lastTar;
        float dist;
        public float Length => dist;
        List<Transform> arrows = new List<Transform>();
        float time;
        Material _m = Main.material;
            public override Material Material
        {
            get => _m;
            set
            {
                if (_m == value)
                    return;
                _m = value;
                foreach (var a in arrows)
                    a.GetComponent<MeshRenderer>().sharedMaterial = _m;
            }
        }
        void OnChange()
        {
            var diff = TargetPosition - parent.Position;
            dist = diff.magnitude;
            transform.rotation = diff == Vector3.zero ? new Quaternion(0,0,0,1) : Quaternion.LookRotation(diff);
            var c = GetComponent<CapsuleCollider>();
            c.center = Vector3.forward * dist / 2;
            c.height = dist;
        }
        void Update()
        {
            if (lastPos != parent.Position || lastTar != TargetPosition)
            {
                lastPos = parent.Position;
                lastTar = TargetPosition;
                OnChange();
            }
            time += Time.deltaTime;
            if (time >= 1)
                time %= 1;
            var count = Mathf.CeilToInt(dist - time);
            if (count < 0)
                count = 0;
            while (count < arrows.Count)
            {
                Destroy(arrows[0].gameObject);
                arrows.RemoveAt(0);
            }
            while (count > arrows.Count)
            {
                var a = Instantiate(arrowPrefab,transform,false);
                a.GetComponent<MeshRenderer>().sharedMaterial = Material;
                arrows.Add(a);
            }
            var i = time;
            foreach (var a in arrows)
                a.localPosition = Vector3.forward * i++;
        }
            void OnDestroy()
            {
                parent?.connections.Remove(this);
            }
    }


    [HarmonyPatch(typeof(WeaponVacuum), "Update")]
    class Patch_VacuumUpdate
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var ind = code.FindIndex((x) => x.operand is MethodInfo && (x.operand as MethodInfo).Name == "set_InGadgetMode");
            code.Insert(ind++, new CodeInstruction(OpCodes.Ldarg_0));
            code.Insert(ind++, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_VacuumUpdate), "OverridePlayerGadgetMode")));
            return code;
        }
        static bool OverridePlayerGadgetMode(bool value, WeaponVacuum instance)
        {
            if (instance.vacMode == Ids.DRONEDEV)
                return true;
            return value;
        }
    }

    [HarmonyPatch(typeof(DeactivateBasedOnGadgetMode), "Update")]
    class Patch_GadgetActivateUpdate
    {
        static WeaponVacuum vac;
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var ind = code.FindIndex((x) => x.operand is MethodInfo && (x.operand as MethodInfo).Name == "get_InGadgetMode") + 1;
            code.Insert(ind++, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_GadgetActivateUpdate), "OverridePlayerGadgetMode")));
            return code;
        }
        static bool OverridePlayerGadgetMode(bool value)
        {
            if (!vac)
                vac = SceneContext.Instance?.Player?.GetComponent<TeleportablePlayer>()?.weaponVacuum;
            if (value && vac && vac.vacMode == Ids.DRONEDEV)
                return false;
            return value;
        }
    }
}