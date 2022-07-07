#if MIRROR_43_0_OR_NEWER
using Mirror;
using UnityEngine;

public class TestController : NetworkBehaviour
{
    public float mouse_sensitivity = 50.0f;
    public float player_speed = 5.0f;
    public Animator animator;
    
    private CharacterController cc;

    [SyncVar(hook = nameof(OnMovingChanged))]
    private bool isMoving;
    
    [SyncVar(hook = nameof(OnCurrentSpeedChanged))]
    private float currentSpeed;
    
    private static readonly int AnimatorMoving = Animator.StringToHash("Moving");
    private static readonly int AnimatorSpeed = Animator.StringToHash("speed");

    private Rigidbody rb;

    void Start()
    {
        cc = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        rb = GetComponent<Rigidbody>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!isLocalPlayer)
        {
            Destroy(GetComponentInChildren<Camera>().gameObject);
        }
    }

    private void Update()
    {
        if (!isLocalPlayer)
        {
            return;
        }

        float scrollVel = Input.GetAxis("Mouse ScrollWheel");
        if (scrollVel > 0)
        {
            player_speed += 20000 * Time.deltaTime;
        }else if (scrollVel < 0)
        {
            player_speed -= 20000 * Time.deltaTime;
        }
        float forwardsInput = Input.GetAxisRaw("Vertical");

        CmdUpdateAnimator(player_speed * forwardsInput != 0, player_speed);

        // Move around with WASD
        var move = new Vector3(Input.GetAxisRaw("Horizontal"), 0, forwardsInput);
        move = transform.TransformDirection(move);
        move = move * player_speed * Time.deltaTime;
        transform.position += move;
        
        

        // Turn player
        var turn_player = new Vector3(0, Input.GetAxisRaw("Mouse X"), 0);
        turn_player = turn_player * mouse_sensitivity * Time.deltaTime;
        transform.localEulerAngles += turn_player;
    }

    [Command]
    private void CmdUpdateAnimator(bool _isMoving, float _currentMoveSpeed)
    {
        isMoving = _isMoving;
        currentSpeed = _currentMoveSpeed;
    }

    private void OnGUI()
    {
        if (!isLocalPlayer)
        {
            return;
        }
        var style = new GUIStyle();
        style.normal.textColor = Color.black;
        GUI.Label(new Rect(10, 330, 1000, 20), "Player speed (m/s):  " + player_speed, style);
    }

    private void OnCurrentSpeedChanged(float oldValue, float newValue)
    {
        animator.SetFloat(AnimatorSpeed, Mathf.Clamp(newValue / 1000, 1, 10));
    }
    private void OnMovingChanged(bool oldValue, bool newValue)
    {
        animator.SetBool(AnimatorMoving, newValue);
    }
}
#endif