//
// Syncs the position & rotation of a Transform across clients.
// A decent substitute for Mirror's NetworkTransform.
//
// For more advanced implementations, please consult the Mirror community.
// 
// This can be used on players objects, npcs, items, or any other objects you want to sync.
// 


#if MIRROR_43_0_OR_NEWER
using System;
using UnityEngine;
using Mirror;

namespace twoloop
{
    public class FOUNetTransform : FOUNetTransformBase
    {
        /// <summary>
        /// Does this object allow client authority?
        /// </summary>
        [Space(20)] [Header("Settings")]
        [Tooltip("Set to true if moves come from owner client, set to false if moves always come from server")]
        public bool clientAuthority = true;

        /// <summary>
        /// Does this Transform automatically become the FloatingOrigin focus in Start() if this is the local
        /// player?
        /// </summary>
        [Tooltip("Does this Transform automatically become the FloatingOrigin focus in Start() if this is the local player?")]
        public bool isAutoFocus;

        public float lerpPositionAmount = .35f;

        public float lerpRotationAmount = .1f;

        // Is this a client with authority over this transform?
        // This component could be on the player object or any object that has been assigned authority to this client.
        private bool IsClientWithAuthority => clientAuthority && hasAuthority;
        
        private float _nextSyncTime;

        /// <summary>
        /// This is set true when both remoteoffset and remoteposition have been set by owner
        /// </summary>
        [SyncVar] private bool _hasOwnerInitializedOffset;
        
        [SyncVar] private FloatingOrigin.Offset _spawnPayloadOffset;
        
        [SyncVar(hook = nameof(OnTargetChanged))] private TransformTargetData _target;

        private FloatingOrigin.Offset _targetOffset;

        private void OnTargetChanged(TransformTargetData oldTarget, TransformTargetData newTarget)
        {
            if (newTarget.isOffsetChanged)
            {
                _targetOffset = newTarget.offset;
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
 
            if (!clientAuthority)
            {
                ServerInitializeOffset(new TransformTargetData(NetworkFOU.hostOffset, transform.position, transform.rotation, true));
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

            // Initialize offset and transform targets
            if (IsClientWithAuthority)
            {
                CmdInitializeOffset(new TransformTargetData(FloatingOrigin.LocalOffset, transform.position, transform.rotation, true));
                _hasOwnerInitializedOffset = true;
            }
        }
        
        private bool IsOffsetChanged()
        {
            return Vector3.Magnitude(FloatingOrigin.LocalOffset.ToVector3() - _targetOffset.ToVector3()) > float.Epsilon;
        }

        [Command(channel = Channels.Reliable)]
        private void CmdInitializeOffset(TransformTargetData target)
        {
            ServerInitializeOffset(target);
        }

        [Server]
        private void ServerInitializeOffset(TransformTargetData target)
        {
            _target = new TransformTargetData(target.offset, target.remotePosition, target.rotation,
                target.isOffsetChanged);
            
            if (_target.isOffsetChanged)
            {
                _spawnPayloadOffset = this._target.offset;
            }
            
            _hasOwnerInitializedOffset = true;
            
            RpcInitializeOffset(_target);
        }
        
        [ClientRpc(channel = Channels.Reliable)]
        private void RpcInitializeOffset(TransformTargetData target)
        {
            _target = new TransformTargetData(target.offset, target.remotePosition, target.rotation,
                target.isOffsetChanged);
            _hasOwnerInitializedOffset = true;
        }

        private void Update()
        {
            if(!IsClientWithAuthority && !(isServer && !clientAuthority))
            {
                if (_hasOwnerInitializedOffset)
                {
                    // Lerp all clients to the targets
                    // First convert to the local client's floating origin space.
                    var localTargetPosition = FloatingOrigin.RemoteToLocal(_targetOffset, _target.remotePosition);

                    // Lerp values towards targets
                    transform.position = Vector3.Lerp(transform.position, localTargetPosition, lerpPositionAmount * 30 * Time.deltaTime);
                    transform.rotation = Quaternion.Lerp(transform.rotation, _target.rotation,
                        lerpRotationAmount * 60 * Time.deltaTime);
                }
            }
            else
            {
                SendToServer();
            }
        }

        private bool IsControlledRemotley()
        {
            return !IsClientWithAuthority && !(isServer && !clientAuthority);
        }

        /// <summary>
        /// Sends the position, rotation, and local offset to the server
        /// using a tickrate. Call this in an update function.
        /// </summary>
        [Client]
        private void SendToServer()
        {
            float now = Time.time;
            if (now > _nextSyncTime)
            {
                _nextSyncTime = now + syncInterval;

                var data = new TransformTargetData(FloatingOrigin.LocalOffset, transform.position, transform.rotation,
                    IsOffsetChanged());
                
                if (clientAuthority)
                {
                    // Client->server
                    CmdSendState(data);
                }
                else
                {
                    // Server->client
                    ServerUpdateState(data);
                }
            }
        }
        
        [Command(channel = Channels.Unreliable)]
        private void CmdSendState(TransformTargetData target)
        {
            ServerUpdateState(target);
        }
        
        [Server]
        private void ServerUpdateState(TransformTargetData target)
        {
            _target = new TransformTargetData(target.offset, target.remotePosition, target.rotation,
                target.isOffsetChanged);

            if (_target.isOffsetChanged)
            {
                _spawnPayloadOffset = _target.offset;
            }
        }
    }
    
    /// <summary>
    /// Container for all the data that needs to be sent 
    /// </summary>
    public struct TransformTargetData
    {
        public FloatingOrigin.Offset offset;
        public Vector3 remotePosition;
        public Quaternion rotation;
        public bool isOffsetChanged;

        public TransformTargetData(FloatingOrigin.Offset offset, Vector3 remotePosition, Quaternion rotation,
            bool isOffsetChanged)
        {
            this.offset = offset;
            this.remotePosition = remotePosition;
            this.rotation = rotation;
            this.isOffsetChanged = isOffsetChanged;
        }
    }
    
    /// <summary>
    /// Network serializer for rigidbody movement data.
    /// Only sends offset when needed.
    /// </summary>
    public static class TransformTargetDataReaderWriter
    {
        public static void WriteTransformTargetData(this NetworkWriter writer, TransformTargetData value)
        {
            writer.WriteVector3(value.remotePosition);
            writer.WriteUInt(Compression.CompressQuaternion(value.rotation));
            writer.WriteBool(value.isOffsetChanged);
            if (value.isOffsetChanged)
            {
                writer.WriteOffset(value.offset);
            }
        }

        public static TransformTargetData ReadTransformTargetData(this NetworkReader reader)
        {
            var td = new TransformTargetData()
            {
                remotePosition = reader.ReadVector3(),
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