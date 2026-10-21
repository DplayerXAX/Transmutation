using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class SquirrelPlayerReactionValidation
{
    [MenuItem("Tools/Creatures/Validate Squirrel Player Reactions (Play Mode)")]
    public static void Validate()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Enter Play Mode first."); return; }
        using (var f = new Fixture())
        {
            f.Pose(new Vector3(0, 0.5f, -4), Vector3.back);
            Check(!f.Squirrel.CanSeePlayer(f.Player), "back-facing squirrel cannot witness player");
            var food = f.Food(); f.Player.TryCarryFromInteraction(food);
            Check(f.Nest.TryPlayerDeposit(f.Player, food), "real deposit succeeds behind squirrel");
            Check(!f.Squirrel.IsSocialReaction, "unseen donation produces no response");
            f.Pose(new Vector3(0, 0.5f, -4), Vector3.forward);
            Check(f.Squirrel.CanSeePlayer(f.Player), "front-facing squirrel sees player");
            var wall = f.Primitive(PrimitiveType.Cube, new Vector3(0, 1, -2)); wall.transform.localScale = new Vector3(4, 3, 0.2f);
            Physics.SyncTransforms();
            Check(!f.Squirrel.CanSeePlayer(f.Player), "wall occludes witness"); Object.DestroyImmediate(wall);
            food = f.Food(); f.Player.TryCarryFromInteraction(food);
            Check(f.Nest.TryPlayerDeposit(f.Player, food) && f.Squirrel.CurrentActivity == Squirrel.Activity.Celebrating,
                "seen real deposit starts celebration");
            for (int i = 0; i < 1000 && f.Squirrel.IsSocialReaction; i++) f.Step();
            Check(Get<float>(f.Squirrel, "orbitTravel") >= 355f && !f.Squirrel.IsSocialReaction,
                "squirrel physically completes a full orbit");
            Check(f.Nest.StoredFoodCount == 2, "celebration preserves exact donated inventory");
        }
        using (var f = new Fixture())
        {
            var food = f.Food(); f.Player.TryCarryFromInteraction(food); f.Player.Release();
            Check(f.Nest.TryAcceptThrownFood(food) && food.StorageNest == f.Nest && !f.Nest.TryAcceptThrownFood(food),
                "released player food enters storage once, using the original instance");
            f.Pose(new Vector3(0, 0.5f, -3), Vector3.forward);
            Invoke(f.Squirrel, "CancelSocialReaction");
            f.Nest.ReceiveInteraction(new CreatureInteraction(f.Player.gameObject, f.Nest.transform.position, Vector3.forward));
            Check(f.Player.CarriedCreature == food && f.Squirrel.CurrentActivity == Squirrel.Activity.ReclaimingFood,
                "seen withdrawal targets the actual held food");
            food.GetComponent<Rigidbody>().isKinematic = true; // Model the hand constraint during isolated physics stepping.
            food.transform.position = new Vector3(0, 1.6f, -0.5f); food.GetComponent<Rigidbody>().position = food.transform.position;
            for (int i = 0; i < 250 && f.Player.CarriedCreature != null; i++) f.Step();
            Check(f.Player.CarriedCreature == null && food.Owner == f.Squirrel && f.Squirrel.CarriedCount == 1,
                 $"physical jump snatches the original food from player hand (activity={f.Squirrel.CurrentActivity}, squirrel={f.Body.position}, hand={food.transform.position}, held={food.IsCarried}, owner={food.Owner}, count={f.Squirrel.CarriedCount})");
        }
        using (var f = new Fixture())
        {
            f.Pose(new Vector3(0, 0.5f, -3), Vector3.forward);
            Check(f.Player.TryCarryFromInteraction(f.Nest) && f.Squirrel.CurrentActivity == Squirrel.Activity.FollowingNest,
                "witnessed nest pickup starts following");
            f.Player.transform.position = new Vector3(6, 0.5f, 6);
            f.Nest.transform.position = new Vector3(6, 1, 7); f.Nest.GetComponent<Rigidbody>().position = f.Nest.transform.position;
            for (int i = 0; i < 100; i++) f.Step();
            Check(f.Body.position.x > 2, "owner physically follows nest carrier");
            f.Player.Release(); f.Player.transform.position = new Vector3(30, 0.5f, 0);
            for (int i = 0; i < 600 && f.Squirrel.IsSocialReaction; i++) f.Step();
            Check(!f.Nest.IsMoving && !f.Squirrel.IsSocialReaction && Vector3.ProjectOnPlane(f.Nest.transform.position, Vector3.up).magnitude < 0.2f,
                "released nest returns to its original place");
        }
        using (var f = new Fixture())
        {
            f.Pose(new Vector3(0, 0.5f, -3), Vector3.forward);
            Check(f.Player.TryCarryFromInteraction(f.Nest), "pickup for warning scenario"); f.Player.Release();
            f.Player.transform.position = new Vector3(0, 0.5f, -4); f.Camera.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            // Move the owner near the remembered site; the state machine still performs pickup and the warning decision.
            f.Pose(new Vector3(0, 0.5f, -1), Vector3.back);
            for (int i = 0; i < 20 && f.Squirrel.CurrentActivity != Squirrel.Activity.WarningPlayer; i++) f.Step();
            Check(f.Squirrel.WarningCount == 1 && f.Squirrel.CurrentActivity == Squirrel.Activity.WarningPlayer && f.Squirrel.WarningVisible,
                "mutual facing triggers visible warning with no audio clip");
            for (int i = 0; i < 300 && !f.Squirrel.IsEscapingPlayer; i++) f.Step();
            Check(f.Squirrel.IsEscapingPlayer && f.Nest.IsMoving, "continuous stare escalates to escape with the real nest");
            f.Step();
            Check(Get<Vector3>(f.Squirrel.GetComponent<SquirrelMotor>(), "desiredVelocity").magnitude >= 80,
                "escape requests speed above player sprint and slide speeds");
            for (int i = 0; i < 500 && f.Squirrel.IsSocialReaction; i++) f.Step();
            Check(!f.Squirrel.IsSocialReaction && !f.Nest.IsMoving && f.Nest.Owner == f.Squirrel &&
                Vector3.ProjectOnPlane(f.Nest.transform.position - f.Player.transform.position, Vector3.up).magnitude >= 12,
                "escape places owned nest at a distant valid tree site");
        }
        using (var f = new Fixture())
        {
            f.Pose(new Vector3(0, 0.5f, -3), Vector3.forward);
            f.Player.TryCarryFromInteraction(f.Nest); f.Player.Release();
            f.Player.transform.position = new Vector3(0, 0.5f, -4);
            f.Pose(new Vector3(0, 0.5f, -1), Vector3.back);
            for (int i = 0; i < 20 && f.Squirrel.CurrentActivity != Squirrel.Activity.WarningPlayer; i++) f.Step();
            f.Camera.transform.rotation = Quaternion.LookRotation(Vector3.back); f.Step();
            Check(!f.Squirrel.IsSocialReaction && !f.Nest.IsMoving && !f.Squirrel.WarningVisible,
                "looking away breaks stare and allows original placement");
        }
        Debug.Log("Squirrel Player Reaction validation passed: front/back, occlusion, real gifts, full orbit, thrown inventory, jumping theft recovery, following, original return, silent warning, stare escalation, fast escape and safe tree placement.");
    }
    private sealed class Fixture : IDisposable
    {
        public readonly Scene Scene;
        public readonly Squirrel Squirrel;
        public readonly SquirrelNest Nest;
        public readonly PlayerCreatureCarrier Player;
        public readonly Camera Camera;
        public readonly Rigidbody Body;
        private readonly SquirrelSystemManager system;
        public Fixture()
        {
            Scene = SceneManager.CreateScene("Squirrel Reactions " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var floor = Primitive(PrimitiveType.Cube, new Vector3(0,-0.5f,0)); floor.layer = 7; floor.transform.localScale = new Vector3(150,1,150);
            var root = new GameObject("Fixture system"); SceneManager.MoveGameObjectToScene(root, Scene);
            system = root.AddComponent<SquirrelSystemManager>(); Set(system,"initialFoodCount",0);
            var tree = new GameObject("Tree"); SceneManager.MoveGameObjectToScene(tree, Scene); tree.transform.position = new Vector3(22,0,0);
            system.GenerateAroundTrees(new[] {tree.AddComponent<SquirrelTree>()});
            Squirrel = Primitive(PrimitiveType.Cube,new Vector3(0,0.5f,-4)).AddComponent<Squirrel>();
            Body = Squirrel.GetComponent<Rigidbody>(); Body.interpolation = RigidbodyInterpolation.None;
            Nest = Primitive(PrimitiveType.Sphere,new Vector3(0,0.5f,0)).AddComponent<SquirrelNest>();
            Nest.GetComponent<Rigidbody>().isKinematic = true; Nest.AssignOwner(Squirrel);
            Squirrel.Initialize(system,Nest,1,3);
            var player = new GameObject("Player"); SceneManager.MoveGameObjectToScene(player,Scene); player.SetActive(false);
            player.transform.position = new Vector3(0,0.5f,-1);
            var cameraObject = new GameObject("Camera"); cameraObject.transform.SetParent(player.transform,false); cameraObject.transform.localPosition = Vector3.up;
            Camera = cameraObject.AddComponent<Camera>(); Camera.enabled = false;
            Player = player.AddComponent<PlayerCreatureCarrier>(); Set(Player,"carryCamera",Camera); player.SetActive(true);
            Physics.SyncTransforms();
        }
        public GameObject Primitive(PrimitiveType type,Vector3 position)
        { var go = GameObject.CreatePrimitive(type); SceneManager.MoveGameObjectToScene(go,Scene); go.transform.position=position; return go; }
        public SquirrelFood Food() { var food=Primitive(PrimitiveType.Capsule,new Vector3(2,1,0)).AddComponent<SquirrelFood>(); system.Register(food); return food; }
        public void Pose(Vector3 position,Vector3 forward)
        { Body.position=Squirrel.transform.position=position; Body.rotation=Squirrel.transform.rotation=Quaternion.LookRotation(forward); Body.linearVelocity=Vector3.zero; Physics.SyncTransforms(); }
        public void Step()
        { Invoke(Squirrel,"TickCreature",0.02f); Set(Squirrel.GetComponent<SquirrelMotor>(),"nextHop",0f); Invoke(Squirrel.GetComponent<SquirrelMotor>(),"FixedUpdate"); Scene.GetPhysicsScene().Simulate(0.02f); }
        public void Dispose()
        { Player.Release(); foreach(var root in Scene.GetRootGameObjects()) Object.DestroyImmediate(root); SceneManager.UnloadSceneAsync(Scene); }
    }
    private static void Set(object obj,string field,object value)=>obj.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(obj,value);
    private static T Get<T>(object obj,string field)=>(T)obj.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);
    private static void Invoke(object obj,string method,params object[] args)=>obj.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,args);
    private static void Check(bool ok,string message) { if(!ok) throw new InvalidOperationException("Squirrel reaction failed: "+message); }
}
