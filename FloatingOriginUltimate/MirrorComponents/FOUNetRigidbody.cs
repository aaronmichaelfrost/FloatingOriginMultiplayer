///
// Sync the position, rotation, and velocity of a Transform with a rigidbody across clients.
// This is a good substitute for a NetworkTransform if your object has a rigidbody.
//
// For more advanced implementations, please consult the Mirror community.
// 
// This can be used on players objects, npcs, items, or any other objects you want to sync.
// 

#if MIRROR_43_0_OR_NEWER
using UnityEngine;
using Mirror;

namespace twoloop
{
    public class FOUNetRigidbody : FOUNetTransformBase
    {
        /// <summary>
        /// The main rigidbody to sync. Usually this should be on a root GameObject.
        /// </summary>
        [Tooltip("The main rigidbody to sync. Usually this should be on a root GameObject.")]
        [SerializeField] public Rigidbody targetRb;
        
        [Space(20)] [Header("Debug")]
        
        /// <summary>
        /// If set true, the rigidbody will set isKinematic true in edit mode automatically, and in Start(), isKinematic will
        /// set false on ONLY the server version of this object, therefore allowing physics simulation
        /// to occur ONLY on the server. You likely will want this for networked objects like items, but not players.
        /// </summary>
        [Space(20)] [Header("Settings")] 
        public bool serverOnlyPhysics;

        /// <summary>
        /// Does this object allow client authority?
        /// </summary>
        [Tooltip("Set to true if moves come from owner client, set to false if moves always come from server")]
        public bool clientAuthority = true;
        
        /// <summary>
        /// Does this Transform automatically become the FloatingOrigin focus in Start() if this is the local
        /// player?
        /// </summary>
        [Tooltip("Does this Transform automatically become the FloatingOrigin focus in Start() if this is the local player?")]
        public bool isAutoFocus;

        public float lerpPositionAmount = .2f;

        public float lerpRotationAmount = .1f;

        /// <summary>
        /// Should the rigidbody turn kinematic on at the end of Start()?
        /// </summary>
        [HideInInspector] public bool forceKinematic;

        public Vector3 velocity
        {
            get
            {
                return _target.velocity;
            }
        }

        // Is this a client with authority over this transform?
        // This component could be on the player object or any object that has been assigned authority to this client.
        private bool IsClientWithAuthority => clientAuthority && hasAuthority;
        
        private float _nextSyncTime;

        [SyncVar()] private bool _hasOwnerInitializedOffset;

        [SyncVar(hook = nameof(OnTargetChanged))] private NetRbTargetData _target;

        private FloatingOrigin.Offset _targetOffset;

        [SyncVar] private FloatingOrigin.Offset _spawnPayloadOffset;

        private void OnTargetChanged(NetRbTargetData oldTarget, NetRbTargetData newTarget)
        {
            if (newTarget.isOffsetChanged)
            {
                _targetOffset = newTarget.offset;
            }
        }

        public void OnValidate()
        {
            if (!targetRb)
            {
                targetRb = GetComponent<Rigidbody>();
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            
            // Set the focus 
            if (isLocalPlayer && isAutoFocus)
            {
                FloatingOrigin.singleton.focus = transform;
            }
            
            if (isLocalPlayer || !isServer)
            {
                // Correct the start position since the spawn message sets the position with the host's offset
                // This can be done automatically with spawn handler. Read the Getting Started PDF
                
                transform.position = FloatingOrigin.RemoteToLocal(NetworkFOU.hostOffset, transform.position);
            }

            _targetOffset = _spawnPayloadOffset;

            // Initialize offset and lerp targets
            if (IsClientWithAuthority)
            {
                CmdInitializeOffset(new NetRbTargetData(FloatingOrigin.LocalOffset, transform.position, transform.rotation,
                    true, targetRb.velocity));
                _hasOwnerInitializedOffset = true;
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            
            if (!clientAuthority)
            {
                ServerInitializeOffset(new NetRbTargetData(FloatingOrigin.LocalOffset, transform.position, transform.rotation,
                    true, targetRb.velocity));
            }
        }
        
        [Command(channel = Channels.Reliable)]
        private void CmdInitializeOffset(NetRbTargetData target)
        {
            ServerInitializeOffset(target);
        }

        [Server]
        private void ServerInitializeOffset(NetRbTargetData target)
        {
            _target = new NetRbTargetData(target.offset, target.remotePosition, target.rotation,
                target.isOffsetChanged, target.velocity);
            
            if (_target.isOffsetChanged)
            {
                _spawnPayloadOffset = _target.offset;
            }
            
            _hasOwnerInitializedOffset = true;
            
            RpcInitializeOffset(_target);
        }

        [ClientRpc(channel = Channels.Reliable)]
        private void RpcInitializeOffset(NetRbTargetData _target)
        {
            this._target = new NetRbTargetData(_target.offset, _target.remotePosition, _target.rotation,
                _target.isOffsetChanged, _target.velocity);
            
            _hasOwnerInitializedOffset = true;
        }

        private void Start()
        {
            if (targetRb == null)
            {
                return;
            }

            if (isServer)
            {
                if (serverOnlyPhysics)
                {
                    targetRb.isKinematic = false;
                }
            }
            else
            {
                if (serverOnlyPhysics)
                {
                    targetRb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                    targetRb.isKinematic = true;
                }
            }

            if (forceKinematic)
            {
                targetRb.isKinematic = true;
            }
        }
        
        private void Update()
        {
            if (targetRb == null)
            {
                return;
            }
            
            if(!IsClientWithAuthority && !(isServer && !clientAuthority))
            {
                if (_hasOwnerInitializedOffset)
                {
                    // Lerp all clients to the targets
                    
                    // First convert to the local client's floating origin space.
                    var localTargetPosition = FloatingOrigin.RemoteToLocal(_targetOffset, _target.remotePosition);

                    // Lerp values towards targets
                    transform.position = Vector3.Lerp(transform.position, localTargetPosition, lerpPositionAmount * 30 * Time.deltaTime);
                    transform.position += _target.velocity * Time.deltaTime;
                    transform.rotation = Quaternion.Lerp(transform.rotation, _target.rotation,
                        lerpRotationAmount * 60 * Time.deltaTime);
                }
            }
            else
            {
                SendToServer();
            }
        }

        private bool IsOffsetChanged()
        {
            return Vector3.Magnitude(FloatingOrigin.LocalOffset.ToVector3() - _targetOffset.ToVector3()) > float.Epsilon;
        }
        
        private void SendToServer()
        {
            float now = Time.time;
            if (now > _nextSyncTime)
            {
                _nextSyncTime = now + syncInterval;

                var newData = new NetRbTargetData(FloatingOrigin.LocalOffset, transform.position, transform.rotation,
                    IsOffsetChanged(),
                    targetRb.velocity);

                if (IsClientWithAuthority)
                {
                    // Client -> server
                    CmdSendState(newData);
                }
                else
                {
                    // Server -> server
                    ServerUpdateState(newData);
                }
            }
        }
        
        /// <summary>
        /// Sends the position, rotation, velocity, and local offset to the server.
        /// </summary>
        [Command (channel = Channels.Unreliable)]
        void CmdSendState(NetRbTargetData target)
        {
            ServerUpdateState(target);
        }

        void ServerUpdateState(NetRbTargetData target)
        {
            _target = new NetRbTargetData(target.offset, target.remotePosition, target.rotation,
                target.isOffsetChanged, target.velocity);

            if (_target.isOffsetChanged)
            {
                _spawnPayloadOffset = _target.offset;
            }
        }
    }
    
    /// <summary>
    /// Container for all the data that needs to be sent 
    /// </summary>
    public struct NetRbTargetData
    {
        public FloatingOrigin.Offset offset;
        public Vector3 remotePosition;
        public Quaternion rotation;
        public bool isOffsetChanged;
        public Vector3 velocity;

        public NetRbTargetData(FloatingOrigin.Offset offset, Vector3 remotePosition, Quaternion rotation,
            bool isOffsetChanged, Vector3 velocity)
        {
            this.offset = offset;
            this.remotePosition = remotePosition;
            this.rotation = rotation;
            this.isOffsetChanged = isOffsetChanged;
            this.velocity = velocity;
        }
    }
    
    /// <summary>
    /// Network serializer for rigidbody movement data.
    /// Only sends offset when needed.
    /// </summary>
    public static class NetRbTargetDataReaderWriter
    {
        public static void WriteTargetData(this NetworkWriter writer, NetRbTargetData value)
        {
            writer.WriteVector3(value.remotePosition);
            writer.WriteVector3(value.velocity);
            writer.WriteUInt(Compression.CompressQuaternion(value.rotation));
            writer.WriteBool(value.isOffsetChanged);
            if (value.isOffsetChanged)
            {
                writer.WriteOffset(value.offset);
            }
        }

        public static NetRbTargetData ReadTargetData(this NetworkReader reader)
        {
            var td = new NetRbTargetData()
            {
                remotePosition = reader.ReadVector3(),
                velocity = reader.ReadVector3(),
                rotation = Compression.DecompressQuaternion(reader.ReadUInt()),
                isOffsetChanged = reader.ReadBool(),
            };

            if (td.isOffsetChanged)
            {
                td.offset = reader.ReadOffset();
            }

            return td;
        }
    }
}
#endif