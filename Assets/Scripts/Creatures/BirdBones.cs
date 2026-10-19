using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Passive, carryable Matter remains. No bird flight or hunting logic.</summary>
[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public sealed class BirdBones : Creature
{
    private static Material fallbackMaterial;

    protected override void InitializeCreature()
    {
        SetCreatureName("Bird Bones");
        SetFoodAndDiet(FoodTag.Matter, FoodTag.None);
        InitializeDeadRemains();
        SetMovementPhysics(false, true);
    }

    protected override void TickCreature(float deltaTime) { }

    internal static BirdBones Spawn(BirdBones prefab, Scene scene, Vector3 position, Quaternion rotation,
        Vector3 velocity, Vector3 angularVelocity)
    {
        BirdBones bones;
        if (prefab != null) bones = Instantiate(prefab, position, rotation);
        else
        {
            // Independently created birds still leave useful remains without an authored prefab reference.
            GameObject root = new GameObject("Bird Bones (Automatic)");
            root.transform.SetPositionAndRotation(position, rotation);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<Rigidbody>().mass = 0.15f;
            root.AddComponent<BoxCollider>().size = new Vector3(0.45f, 0.2f, 0.7f);
            bones = root.AddComponent<BirdBones>();
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Bone Placeholder";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = new Vector3(0.45f, 0.2f, 0.7f);
            visual.GetComponent<Collider>().enabled = false;
            Destroy(visual.GetComponent<Collider>());
            if (fallbackMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader != null)
                {
                    fallbackMaterial = new Material(shader) { name = "Bird Bones (Shared Automatic)", enableInstancing = true };
                    fallbackMaterial.SetColor("_BaseColor", new Color(0.88f, 0.84f, 0.7f));
                }
            }
            if (fallbackMaterial != null) visual.GetComponent<Renderer>().sharedMaterial = fallbackMaterial;
        }
        if (bones.gameObject.scene != scene) SceneManager.MoveGameObjectToScene(bones.gameObject, scene);
        Rigidbody body = bones.GetComponent<Rigidbody>();
        body.isKinematic = false;
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = velocity;
        body.angularVelocity = angularVelocity;
        return bones;
    }
}
