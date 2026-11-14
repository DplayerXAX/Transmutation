using UnityEngine;

/// <summary>Small authored showcase creature, walking a lane until external motion lifts it.</summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class LeafGardenWalker : Creature
{
    public float Speed = 7;
    public float Acceleration = 80;
    public float LaneHalfWidth = 25;
    public Transform Visual;
    private Rigidbody body;
    private Vector3 home;
    private float direction = 1;
    private int fixedSteps;
    private float assignedX;
    public string Diagnostic => body==null ? "Not running" : $"steps={fixedSteps} enabled={isActiveAndEnabled} kinematic={body.isKinematic} carried={IsCarried} captured={IsBubbleCaptured} home={home} assignedX={assignedX}";
    public override bool Invulnerable => true;
    protected override void InitializeCreature() { body = GetComponent<Rigidbody>(); home = transform.position; }
    protected override void TickCreature(float dt) { }
    public void ResetWalk() => direction=1;
    private void FixedUpdate()
    {
        fixedSteps++;
        if (!SimulationEnabled || IsCarried || IsBubbleCaptured || body.isKinematic) return;
        if (Mathf.Abs(body.position.x-home.x)>LaneHalfWidth) direction = body.position.x>home.x ? -1 : 1;
        // This demonstration lane is flat. A height gate lets external launches take over
        // without relying on the project's configurable physics query layer matrix.
        bool grounded = body.position.y < home.y + 0.2f;
        if (grounded && body.linearVelocity.y<2)
        {
            Vector3 velocity=body.linearVelocity;
            velocity.x=Mathf.MoveTowards(velocity.x,direction*Speed,Time.fixedDeltaTime*Acceleration);
            velocity.z=Mathf.MoveTowards(velocity.z,0,Time.fixedDeltaTime*Acceleration);
            body.linearVelocity=velocity;
            assignedX=velocity.x;
        }
        if (Visual!=null) Visual.localScale=new Vector3(1,1+Mathf.Sin(Time.time*12)*0.035f,1);
    }
}
