using System;
using System.Collections.Generic;
using UnityEngine;

public enum FurnaceIngredient { None, BirdBones, Bloodstone }

[CreateAssetMenu(menuName = "Creatures/Furnace Recipe")]
public sealed class FurnaceRecipe : ScriptableObject
{
    [Serializable]
    public struct Input
    {
        public FurnaceIngredient kind;
        [Min(1)] public int quantity;
    }

    public Input[] inputs = {
        new Input { kind = FurnaceIngredient.BirdBones, quantity = 1 },
        new Input { kind = FurnaceIngredient.Bloodstone, quantity = 1 }
    };
    public Creature output;
    [Min(1)] public int outputPerSet = 1;
    [Range(0f, 1f)] public float requiredPeak = 0.5f;
    public bool consumeSurplus = true;

    public bool Accepts(FurnaceIngredient kind) =>
        kind != FurnaceIngredient.None && Array.Exists(inputs, input => input.kind == kind);

    public int CompleteSets(Dictionary<FurnaceIngredient, int> counts)
    {
        int sets = int.MaxValue;
        foreach (Input input in inputs)
        {
            if (input.quantity < 1 || !counts.TryGetValue(input.kind, out int count)) return 0;
            sets = Mathf.Min(sets, count / input.quantity);
        }
        return inputs.Length == 0 ? 0 : sets;
    }

    public int ConsumptionLimit(FurnaceIngredient kind, int sets) =>
        consumeSurplus ? int.MaxValue : sets * Array.Find(inputs, input => input.kind == kind).quantity;
}
