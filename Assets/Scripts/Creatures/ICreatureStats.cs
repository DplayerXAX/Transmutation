using UnityEngine;

/// <summary>Shared readable state, independent of creature lifecycle and player controls.</summary>
public interface ICreatureStats
{
    float Luminosity { get; }
    Vector3 EmissionPosition { get; }
    Transform StatRoot { get; }
    bool IsAvailableForPerception { get; }
    void SetLuminosity(float value);
}
