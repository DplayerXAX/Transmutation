using UnityEngine;

/// <summary>
/// Moving ground the player stands on and is carried by (SmoothFirstPersonController adds its velocity),
/// such as a compost creature's back or the Ore Colossus's walking body.
/// </summary>
public interface IRideSurface
{
    /// <summary>Whether standing on this collider rides the surface.</summary>
    bool Supports(Collider collider);
    /// <summary>World velocity of the surface at a point.</summary>
    Vector3 VelocityAt(Vector3 pointWS);
}
