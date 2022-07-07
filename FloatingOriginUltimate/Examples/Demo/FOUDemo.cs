using twoloop;
using UnityEngine;

public class FOUDemo : MonoBehaviour
{
    public Vector3 velocity = new Vector3(0, 0, 2000);

    public GameObject testSpawnObject;

    private float _lastSpawnTime = float.NegativeInfinity;

    public void Update()
    {
        transform.position += velocity * Time.deltaTime;

        // Spawn objects in front of us to show we are moving
        if (Time.time - _lastSpawnTime > .2f)
        {
            GameObject g = Instantiate(testSpawnObject, transform.position + transform.forward * 500 + Random.insideUnitSphere * 100f, Quaternion.identity);
            g.transform.localScale *= Random.Range(1, 2.5f);
            Destroy(g, 2);
            _lastSpawnTime = Time.time;
        }

        if (FloatingOrigin.singleton)
        {
            Debug.Log(FloatingOrigin.LocalOffset.ToString());
        }
    }

    private void OnGUI()
    {
        var g = new GUIStyle();
        g.normal.textColor = Color.black;
        
        GUI.Label(new Rect(10, 70, 300, 20), "Player is automatically moving forward." ,g);
        
        GUI.Label(new Rect(10, 100, 300, 20), "Notice how the position magnitude remains low," ,g);
        
        GUI.Label(new Rect(10, 130, 300, 20), "while the total distance increases forever!" ,g);
    }
}
