using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Shows effective lifecycle state without bypassing creature death behavior.</summary>
[CustomEditor(typeof(Creature), true), CanEditMultipleObjects]
public sealed class CreatureEditor : Editor
{
    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();
        var state = new Foldout { text = "Creature Lifecycle (Play Mode)", value = true };
        var dead = new Toggle("Is Dead") { name = "is-dead" };
        var running = new Toggle("Simulation Running") { name = "simulation-running" };
        dead.SetEnabled(false);
        running.SetEnabled(false);
        state.Add(dead);
        state.Add(running);
        var health = new FloatField("Health") { name = "creature-health" };
        var polarity = new TextField("Resolved Polarity");
        var cause = new TextField("Death Cause");
        var combat = new TextField("Combat");
        foreach (VisualElement field in new VisualElement[] { health, polarity, cause, combat })
        { field.SetEnabled(false); state.Add(field); }
        var kill = new Button(() => Kill(targets.OfType<Creature>().ToArray())) { text = "Kill", name = "kill-creatures" };
        state.Add(kill);
        state.Add(new HelpBox("Death keeps the body and stops its abilities. Simulation pause and pickup eligibility are separate. Kill is available for active scene creatures in Play Mode.", HelpBoxMessageType.Info));
        root.Add(state);
        InspectorElement.FillDefaultInspector(root, serializedObject, this);

        void Refresh()
        {
            var creatures = targets.OfType<Creature>().Where(c => c != null).ToArray();
            if (creatures.Length == 0) { kill.SetEnabled(false); return; }
            dead.SetValueWithoutNotify(creatures[0].IsDead);
            running.SetValueWithoutNotify(creatures[0].isActiveAndEnabled && creatures[0].SimulationEnabled);
            health.SetValueWithoutNotify(creatures[0].Health);
            health.showMixedValue = creatures.Any(c => c.Health != creatures[0].Health);
            polarity.SetValueWithoutNotify(creatures[0].Polarity.ToString());
            polarity.showMixedValue = creatures.Any(c => c.Polarity != creatures[0].Polarity);
            cause.SetValueWithoutNotify(creatures[0].DeathCause.ToString());
            cause.showMixedValue = creatures.Any(c => c.DeathCause != creatures[0].DeathCause);
            var fighting = creatures[0].GetComponent<CreatureCombat>();
            combat.SetValueWithoutNotify(fighting == null ? "—" : fighting.State + " / " +
                (fighting.Target != null ? fighting.Target.name : "no target") + " / releases " + fighting.Releases);
            dead.showMixedValue = creatures.Any(c => c.IsDead != creatures[0].IsDead);
            running.showMixedValue = creatures.Any(c => (c.isActiveAndEnabled && c.SimulationEnabled) != running.value);
            state.SetEnabled(EditorApplication.isPlaying);
            kill.SetEnabled(creatures.Any(CanKill));
        }
        Refresh();
        root.schedule.Execute(Refresh).Every(100);
        return root;
    }

    private static bool CanKill(Creature creature) => creature != null && Application.IsPlaying(creature) &&
        creature.gameObject.activeInHierarchy && !creature.IsDead;

    internal static void Kill(Creature[] creatures)
    {
        if (!EditorApplication.isPlaying) return;
        foreach (Creature creature in creatures)
            if (CanKill(creature)) creature.Die();
    }
}
