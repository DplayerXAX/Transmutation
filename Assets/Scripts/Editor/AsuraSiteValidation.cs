using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class AsuraSiteValidation
{
    public static string Run()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Play mode required.");
        var root=new GameObject("Disposable site validation");
        var map=ScriptableObject.CreateInstance<AsuraSurfaceMap>();
        int checks=0;
        void Check(bool condition,string message){if(!condition)throw new Exception("Site validation: "+message);checks++;}
        void Polarity(Creature c,CreaturePolarity p)=>typeof(Creature).GetMethod("SetResolvedPolarity",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(c,new object[]{p});
        try
        {
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(root.transform);
            floor.layer=7;floor.transform.position=new Vector3(0,999.5f,0);floor.transform.localScale=new Vector3(150,1,40);
            map.bounds=new Bounds(new Vector3(0,1000,0),new Vector3(150,10,40));
            map.surfaces=Enumerable.Range(0,13).Select(i=>new AsuraSurfaceMap.Surface{
                position=new Vector3(-60+i*10,1000,0),area=100,vine=false,
                neighbours=new[]{i-1,i+1}.Where(n=>n>=0 && n<13).ToArray()}).ToArray();
            var t=root.AddComponent<AsuraTerritory>();t.surfaceMap=map;t.initialPopulation=0;t.fixedCaptureSites=true;
            t.linkagePrefab=AssetDatabase.LoadAssetAtPath<AsuraLinkage>(AsuraPrototypeSetup.Folder+"/AsuraLinkage.prefab");
            t.tendrilMaterial=Object.FindObjectsByType<AsuraTerritory>(FindObjectsSortMode.None).First(x=>x!=t).tendrilMaterial;
            t.fighterPrefabs=new Creature[]{AssetDatabase.LoadAssetAtPath<MeleeAsura>(AsuraPrototypeSetup.Folder+"/MeleeAsura.prefab")};
            AsuraHome Home(int x,CreaturePolarity polarity)
            {
                var h=Object.Instantiate(AssetDatabase.LoadAssetAtPath<AsuraHome>(AsuraPrototypeSetup.Folder+"/AsuraHome.prefab"),new Vector3(x,1000.1f,0),Quaternion.identity,root.transform);
                h.spawnsPerSecond=0;Polarity(h,polarity);return h;
            }
            var light=Home(-60,CreaturePolarity.Light);var dark=Home(60,CreaturePolarity.Dark);
            var node=Object.Instantiate(t.linkagePrefab,new Vector3(0,1000.1f,0),Quaternion.identity,root.transform);node.home=dark;Polarity(node,dark.Polarity);
            Creature Fighter(float x,AsuraHome home)
            {
                var c=Object.Instantiate(t.fighterPrefabs[0],new Vector3(x,1000.15f,3),Quaternion.identity,root.transform);
                Polarity(c,home.Polarity);c.gameObject.AddComponent<AsuraTerritoryMember>().home=home;return c;
            }
            var fighters=new[]{Fighter(-10,light),Fighter(10,light),Fighter(20,light)};
            typeof(AsuraTerritory).GetMethod("Initialize",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(t,null);
            Physics.SyncTransforms();t.Step(0.5f);
            Check(t.Sites.Count==1,"one authored linkage creates one stable site");
            Check(fighters[0].GetComponent<AsuraTerritoryMember>().Objective==t.Sites[0],"idle fighter receives enemy site objective");
            Check(AsuraTerritoryMember.TryMarch(fighters[0],fighters[0].GetComponent<AsuraMotor>(),3),"idle order drives existing motor");
            var marcher=fighters[0].GetComponent<AsuraTerritoryMember>();
            fighters[0].transform.position=new Vector3(-50,1000.15f,0);Physics.SyncTransforms();t.Step(0);
            Vector3 waypoint=marcher.MarchWaypoint;
            Check(waypoint.x>-50,"long route advances to the next surface sample");
            fighters[0].transform.position=new Vector3(-45.8f,1000.15f,1);Physics.SyncTransforms();t.Step(0);
            Check(marcher.MarchWaypoint==waypoint,"nearest-cell change does not reverse an unfinished waypoint");
            fighters[0].transform.position=new Vector3(-10,1000.15f,3);Physics.SyncTransforms();
            Check(!t.TryExpand(light),"procedural expansion disabled");
            node.ApplyDamage(10000,fighters[0]);
            Check(!node.gameObject.activeSelf && !t.Nodes.Contains(node),"destroyed body and collider disappear immediately");
            Check(t.Sites[0].occupant==null,"destroyed site becomes neutral rather than flipping");
            Check(AsuraBreachMomentum.Has(fighters[0]),"breach reward retained");
            t.Step(3);Check(t.Sites[0].occupant==null && t.Sites[0].progress==3,"capture requires full duration");
            var enemy=Fighter(-20,dark);t.Step(0.5f);
            Check(t.Sites[0].progress==0,"enemy presence resets uncontested capture");
            enemy.gameObject.SetActive(false);Object.Destroy(enemy.gameObject);
            t.Step(3);t.Step(3);
            Check(t.Sites[0].occupant!=null && t.Sites[0].occupant.home==light,"six uncontested seconds rebuild allied linkage");
            Check(t.Sites[0].occupant.Health==300 && t.Sites[0].occupant.Connected,"rebuilt linkage healthy and supplied");
            Check(t.Sites.Count==1 && t.Captured==1,"capture preserves site count");
            Check(fighters.All(f=>f.GetComponent<AsuraTerritoryMember>().Objective==null),"captured objective releases march orders");
            return checks+" fixed-site checks passed";
        }
        finally {Object.DestroyImmediate(root);Object.DestroyImmediate(map);}
    }
}
