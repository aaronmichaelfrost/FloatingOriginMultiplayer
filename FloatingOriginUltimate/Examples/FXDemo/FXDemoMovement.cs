using twoloop;
using UnityEngine;

public class FXDemoMovement : MonoBehaviour
{
    private Vector3 firstPos;
    private Vector3 secondPos;
    private void Start()
    {
        firstPos = transform.position + transform.right * 10 + transform.up * 10;
        secondPos = -firstPos;

        FloatingOrigin.OnOriginShifted.AddListener(OnOriginShifted);
    }
    
    private void OnOriginShifted(Vector3 newOffset, Vector3 translation)
    {
        // Always update stored vector3 positions
        firstPos += translation;
        secondPos += translation;
    }
    
    void Update()
    {
        transform.position = Vector3.Lerp(firstPos, secondPos, (Mathf.Sin(Time.time) + 1) / 2);
    }
}
