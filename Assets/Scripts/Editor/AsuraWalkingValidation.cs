using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>Simulates real prefab bodies on frictionless ramps in a disposable physics scene.</summary>
public static class AsuraWalkingValidation
{
    public static string Run()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Play mode required");
        var scene=SceneManager.CreateScene("Asura slope checks "+Guid.NewGuid(),new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var physics=scene.GetPhysicsScene();
        var tick=typeof(AsuraMotor).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
        var material=new PhysicsMaterial("Frictionless validation"){dynamicFriction=0,staticFriction=0,frictionCombine=PhysicsMaterialCombine.Minimum};
        int checks=0;var results=new System.Collections.Generic.List<string>();
        void Check(bool condition,string message){if(!condition)throw new Exception("Asura walking: "+message);checks++;}
        try
        {
            var ramp=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(ramp,scene);
            ramp.layer=7;ramp.transform.localScale=new Vector3(60,1,30);ramp.transform.rotation=Quaternion.Euler(0,0,30);
            ramp.GetComponent<Collider>().sharedMaterial=material;
            foreach(string name in new[]{"MeleeAsura","GeneralAsura","RangedAsura","RollingAsura"})
            {
                ramp.transform.rotation=Quaternion.Euler(0,0,30);Physics.SyncTransforms();
                physics.Raycast(new Vector3(0,20,0),Vector3.down,out var support,40,1<<7,QueryTriggerInteraction.Ignore);
                var c=Object.Instantiate(AssetDatabase.LoadAssetAtPath<Creature>(AsuraPrototypeSetup.Folder+"/"+name+".prefab"),support.point+Vector3.up*0.6f,Quaternion.identity);
                SceneManager.MoveGameObjectToScene(c.gameObject,scene);
                var motor=c.GetComponent<AsuraMotor>();var body=c.GetComponent<Rigidbody>();
                foreach(var shape in c.GetComponentsInChildren<Collider>())shape.sharedMaterial=material;
                void Sim(int frames){for(int n=0;n<frames;n++){Physics.SyncTransforms();tick.Invoke(motor,null);physics.Simulate(Time.fixedDeltaTime);}}
                motor.Stop();Sim(100);Check(motor.Grounded,name+" establishes walkable support");
                Vector3 start=body.position;Sim(150);
                float drift=Vector3.ProjectOnPlane(body.position-start,support.normal).magnitude;
                results.Add(name+" idle drift="+drift.ToString("F3")+"m");
                Check(drift<0.12f,name+" holds still on frictionless 30-degree slope");
                start=body.position;motor.SetIntent(Vector3.right*3,Vector3.right);Sim(100);
                Check(body.position.x-start.x>2 && body.position.y-start.y>0.8f,name+" walks uphill in support plane");
                motor.Stop();Sim(100);start=body.position;Sim(100);
                Check(Vector3.Distance(body.position,start)<0.12f,name+" stops without persistent sliding");
                body.linearVelocity=Vector3.right*5+Vector3.up*4;motor.AcceptPush(100);Sim(1);
                Check(body.linearVelocity.y>3,name+" preserves external launch during knockback");
                Object.DestroyImmediate(c.gameObject);
            }
            ramp.transform.rotation=Quaternion.Euler(0,0,65);Physics.SyncTransforms();
            physics.Raycast(new Vector3(0,30,0),Vector3.down,out var steepSupport,60,1<<7,QueryTriggerInteraction.Ignore);
            var steep=Object.Instantiate(AssetDatabase.LoadAssetAtPath<MeleeAsura>(AsuraPrototypeSetup.Folder+"/MeleeAsura.prefab"),steepSupport.point+Vector3.up*0.6f,Quaternion.identity);
            SceneManager.MoveGameObjectToScene(steep.gameObject,scene);var steepMotor=steep.GetComponent<AsuraMotor>();
            steepMotor.SetIntent(Vector3.right*3,Vector3.right);
            for(int n=0;n<40;n++){Physics.SyncTransforms();tick.Invoke(steepMotor,null);physics.Simulate(Time.fixedDeltaTime);}
            Check(!steepMotor.Grounded,"65-degree slope is not walkable");
            steep.Die();Check(!steepMotor.OwnsMovement,"dead bodies relinquish walking control");
            return checks+" slope checks passed; "+string.Join("; ",results);
        }
        finally
        {
            foreach(var root in scene.GetRootGameObjects())Object.DestroyImmediate(root);
            SceneManager.UnloadSceneAsync(scene);Object.DestroyImmediate(material);
        }
    }
}
