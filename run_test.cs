using UnityEngine;
public class TestLoad {
    public static void Check() {
        var clips = Resources.LoadAll<AnimationClip>("CombatModels");
        foreach(var c in clips) Debug.Log(c.name + " len:" + c.length);
    }
}
