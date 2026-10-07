using UnityEngine;

/// <summary>A single body's attachment to stationary silk. Only owns the joint it creates.</summary>
[DisallowMultipleComponent]
public sealed class WebCapture : MonoBehaviour
{
    public Rigidbody Body { get; private set; }
    public SkyWeb OwningWeb { get; private set; }
    public Vector3 ContactPoint { get; private set; }
    public bool IsCaptured => OwningWeb != null;

    private FixedJoint silkJoint;
    private Creature creature;
    private bool previousSimulation;
    private float eligibleAfter;

    public bool CanCapture => !IsCaptured && Time.time >= eligibleAfter;

    public bool Attach(SkyWeb web, Rigidbody body, Vector3 contactPoint)
    {
        if (!CanCapture || web == null || body == null || body.gameObject != gameObject || body.isKinematic) return false;
        creature = body.GetComponentInParent<Creature>();
        if (creature != null && creature.IsCarried) return false;
        Body = body;
        ContactPoint = contactPoint;
        OwningWeb = web;
        if (creature != null)
        {
            previousSimulation = creature.SimulationEnabled;
            creature.SetSimulationEnabled(false);
        }
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        silkJoint = gameObject.AddComponent<FixedJoint>();
        silkJoint.autoConfigureConnectedAnchor = false;
        // Rigidbody pose is authoritative; interpolation can leave Transform a frame behind.
        Vector3 anchor = Quaternion.Inverse(body.rotation) * (contactPoint - body.position);
        Vector3 scale = transform.lossyScale;
        silkJoint.anchor = new Vector3(anchor.x / scale.x, anchor.y / scale.y, anchor.z / scale.z);
        silkJoint.connectedBody = null;
        silkJoint.connectedAnchor = contactPoint;
        silkJoint.breakForce = float.PositiveInfinity;
        silkJoint.breakTorque = float.PositiveInfinity;
        return true;
    }

    public void Release()
    {
        SkyWeb previousWeb = OwningWeb;
        OwningWeb = null;
        if (silkJoint != null)
        {
            // Destroy is deferred. Neutralize the attachment immediately before a carry handoff.
            silkJoint.breakForce = 0f;
            silkJoint.breakTorque = 0f;
            Destroy(silkJoint);
            silkJoint = null;
        }
        if (creature != null) creature.SetSimulationEnabled(previousSimulation);
        creature = null;
        if (Body != null && !Body.isKinematic) Body.WakeUp();
        eligibleAfter = Time.time + 0.5f;
        if (previousWeb != null) previousWeb.ForgetCapture(this);
    }

    private void OnDisable() => Release();
    private void OnDestroy() => Release();
}
