using System;
using System.Collections.Generic;

/// <summary>Equal allocation with caps and redistribution. Reuses the caller's recipient buffer.</summary>
internal static class BirdNutritionSharing
{
    internal static void Distribute(List<BirdCreature> recipients, float nutrition)
    {
        float remaining = nutrition;
        while (remaining > 0f && recipients.Count > 0)
        {
            float share = remaining / recipients.Count;
            bool cappedAny = false;
            for (int i = recipients.Count - 1; i >= 0; i--)
            {
                BirdCreature bird = recipients[i];
                if (bird.Hunger > share) continue;
                remaining = Math.Max(0f, remaining - bird.ReceiveNutrition(bird.Hunger));
                int last = recipients.Count - 1;
                recipients[i] = recipients[last];
                recipients.RemoveAt(last);
                cappedAny = true;
            }
            if (cappedAny) continue;
            for (int i = 0; i < recipients.Count; i++) recipients[i].ReceiveNutrition(share);
            break;
        }
        // The caller discards unused nutrition once every recipient is full.
    }
}
