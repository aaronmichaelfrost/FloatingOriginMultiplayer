using UnityEngine;
using twoloop;

public class FOUDebugger : MonoBehaviour
{
    public void OnGUI()
    {
        if (!FloatingOrigin.singleton.focus)
        {
            return;
        }
        var g = new GUIStyle();
        g.normal.textColor = Color.black;
        GUI.Label(new Rect(10, 370, 300, 20), "Player position: " + FloatingOrigin.singleton.focus.transform.position, g);

        if (FloatingOrigin.singleton)
        {
            GUI.Label(new Rect(10, 400, 1000, 20), "Local Offset (m):  " + FloatingOrigin.LocalOffset.ToString(), g);
        }
    }
}