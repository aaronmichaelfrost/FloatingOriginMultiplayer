// Adaption of vis2k's NetworkTransform V2 (2021-07) by twoloopassets for Floating Origin Ultimate (2021-12)

// Features snapshot interpolation

#if MIRROR_43_0_OR_NEWER

using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace twoloop
{
    public class FOUNetTransformV2 : FOUNetTransformBase
    {
        /// <summary>
        /// Does this Transform automatically become the FloatingOrigin focus in Start() if this is the local
        /// player?
        /// </summary>
        [Tooltip(
            "Does this Transform automatically become the FloatingOrigin focus in Start() if this is the local player?")]
        public bool isAutoFocus;

        /// <summary>
        /// This is set true when both remoteoffset and remoteposition have been set by owner
        /// </summary>
        [SyncVar] private bool _hasOwnerInitializedOffset;

        [SyncVar] private FloatingOrigin.Offset _spawnPayloadOffset;

        [SyncVar(hook = nameof(OnTargetChanged))]
        private TransformTargetData _target;

        private FloatingOrigin.Offset _targetOffset;
        
        [Header("Authority")]
        [Tooltip("Set to true if moves come from owner client, set to false if moves always come from server")]
        public bool clientAuthority;

        // Is this a client with authority over this transform?
        // This component could be on the player object or any object that has been assigned authority to this client.
        protected bool IsClientWithAuthority => hasAuthority && clientAuthority;

        // target transform to sync. can be on a child.
        private Transform targetComponent;

        [Header("Synchronization")] [Range(0, 1)]
        public float sendInterval = 0.050f;
        
        double lastClientSendTime;
        double lastServerSendTime;
        
        [Header("Interpolation")] public bool interpolatePosition = true;
        public bool interpolateRotation = true;
        
        [Header("Buffering")]
        [Tooltip(
            "Snapshots are buffered for sendInterval * multiplier seconds. If your expected client base is to run at non-ideal connection quality (2-5% packet loss), 3x supposedly works best.")]
        public int bufferTimeMultiplier = 1;

        public float bufferTime => sendInterval * bufferTimeMultiplier;

        [Tooltip("Buffer size limit to avoid ever growing list memory consumption attacks.")]
        public int bufferSizeLimit = 64;

        [Tooltip(
            "Start to accelerate interpolation if buffer size is >= threshold. Needs to be larger than bufferTimeMultiplier.")]
        public int catchupThreshold = 4;

        [Tooltip("Once buffer is larger catchupThreshold, accelerate by multiplier % per excess entry.")]
        [Range(0, 1)]
        public float catchupMultiplier = 0.10f;

        // snapshots sorted by timestamp
        // in the original article, glenn fiedler drops any snapshots older than
        // the last received snapshot.
        // -> instead, we insert into a sorted buffer
        // -> the higher the buffer information density, the better
        // -> we still drop anything older than the first element in the buffer
        // => internal for testing
        //
        // IMPORTANT: of explicit 'FOUNTSnapshot' type instead of 'Snapshot'
        //            interface because List<interface> allocates through boxing
        internal SortedList<double, FOUNTSnapshot> serverBuffer = new SortedList<double, FOUNTSnapshot>();
        internal SortedList<double, FOUNTSnapshot> clientBuffer = new SortedList<double, FOUNTSnapshot>();

        // absolute interpolation time, moved along with deltaTime
        // (roughly between [0, delta] where delta is snapshot B - A timestamp)
        // (can be bigger than delta when overshooting)
        double serverInterpolationTime;
        double clientInterpolationTime;

        // only convert the static Interpolation function to Func<T> once to
        // avoid allocations
        Func<FOUNTSnapshot, FOUNTSnapshot, double, FOUNTSnapshot> Interpolate = FOUNTSnapshot.Interpolate;

        public void Awake()
        {
            targetComponent = GetComponent<Transform>();
        }

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
                ServerInitializeOffset(new TransformTargetData(NetworkFOU.hostOffset, transform.position,
                    transform.rotation, true));
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
                CmdInitializeOffset(new TransformTargetData(FloatingOrigin.LocalOffset, transform.position,
                    transform.rotation, true));
                _hasOwnerInitializedOffset = true;
            }
        }

        private bool IsOffsetChanged()
        {
            return Vector3.Magnitude(FloatingOrigin.LocalOffset.ToVector3() - _targetOffset.ToVector3()) >
                   float.Epsilon;
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

        // snapshot functions //////////////////////////////////////////////////
        // construct a snapshot of the current state
        // => internal for testing
        protected virtual FOUNTSnapshot ConstructSnapshot()
        {
            // NetworkTime.localTime for double precision until Unity has it too
            return new FOUNTSnapshot(
                // our local time is what the other end uses as remote time
                NetworkTime.localTime,
                // the other end fills out local time itself
                0,
                targetComponent.localPosition,
                targetComponent.localRotation,
                FloatingOrigin.LocalOffset
            );
        }

        // apply a snapshot to the Transform.
        // -> start, end, interpolated are all passed in caes they are needed
        // -> a regular game would apply the 'interpolated' snapshot
        // -> a board game might want to jump to 'goal' directly
        // (it's easier to always interpolate and then apply selectively,
        //  instead of manually interpolating x, y, z, ... depending on flags)
        // => internal for testing
        //
        // NOTE: stuck detection is unnecessary here.
        //       we always set transform.position anyway, we can't get stuck.
        protected virtual void ApplySnapshot(FOUNTSnapshot start, FOUNTSnapshot goal, FOUNTSnapshot interpolated)
        {
            targetComponent.localPosition = interpolatePosition ? interpolated.position : FloatingOrigin.RemoteToLocal(goal.remoteOffset, goal.position);
            targetComponent.localRotation = interpolateRotation ? interpolated.rotation : goal.rotation;
        }

        // cmd /////////////////////////////////////////////////////////////////
        // only unreliable. see comment above of this file.
        [Command(channel = Channels.Unreliable)]
        void CmdClientToServerSync(TransformTargetData targetData)
        {
            if (targetData.isOffsetChanged)
            {
                _spawnPayloadOffset = targetData.offset;
            }
            
            OnClientToServerSync(targetData);
            //For client authority, immediately pass on the client snapshot to all other
            //clients instead of waiting for server to send its snapshots.
            if (clientAuthority)
            {
                RpcServerToClientSync(targetData);
            }
        }

        // local authority client sends sync message to server for broadcasting
        protected virtual void OnClientToServerSync(TransformTargetData targetData)
        {
            // only apply if in client authority mode
            if (!clientAuthority) return;

            // protect against ever growing buffer size attacks
            if (serverBuffer.Count >= bufferSizeLimit) return;

            // only player owned objects (with a connection) can send to
            // server. we can get the timestamp from the connection.
            double timestamp = connectionToClient.remoteTimeStamp;
            
            if (targetData.isOffsetChanged)
            {
                _targetOffset = targetData.offset;
            }

            _target = targetData;

            // construct snapshot with batch timestamp to save bandwidth
            FOUNTSnapshot snapshot = new FOUNTSnapshot(
                timestamp,
                NetworkTime.localTime,
                FloatingOrigin.RemoteToLocal(_targetOffset, _target.remotePosition), _target.rotation, FloatingOrigin.LocalOffset
            );

            // add to buffer (or drop if older than first element)
            SnapshotInterpolation.InsertIfNewEnough(snapshot, serverBuffer);
        }

        // rpc /////////////////////////////////////////////////////////////////
        // only unreliable. see comment above of this file.
        [ClientRpc(channel = Channels.Unreliable)]
        void RpcServerToClientSync(TransformTargetData targetData) =>
            OnServerToClientSync(targetData);

        // server broadcasts sync message to all clients
        protected virtual void OnServerToClientSync(TransformTargetData targetData)
        {
            // in host mode, the server sends rpcs to all clients.
            // the host client itself will receive them too.
            // -> host server is always the source of truth
            // -> we can ignore any rpc on the host client
            // => otherwise host objects would have ever growing clientBuffers
            // (rpc goes to clients. if isServer is true too then we are host)
            if (isServer) return;

            // don't apply for local player with authority
            if (IsClientWithAuthority) return;

            // protect against ever growing buffer size attacks
            if (clientBuffer.Count >= bufferSizeLimit) return;
            
            if (targetData.isOffsetChanged)
            {
                _targetOffset = targetData.offset;
            }
            
            _target = targetData;

            // on the client, we receive rpcs for all entities.
            // not all of them have a connectionToServer.
            // but all of them go through NetworkClient.connection.
            // we can get the timestamp from there.
            double timestamp = NetworkClient.connection.remoteTimeStamp;

            // construct snapshot with batch timestamp to save bandwidth
            FOUNTSnapshot snapshot = new FOUNTSnapshot(
                timestamp,
                NetworkTime.localTime,
                FloatingOrigin.RemoteToLocal(_targetOffset, _target.remotePosition), _target.rotation, FloatingOrigin.LocalOffset
            );

            // add to buffer (or drop if older than first element)
            SnapshotInterpolation.InsertIfNewEnough(snapshot, clientBuffer);
        }

        // update //////////////////////////////////////////////////////////////
        void UpdateServer()
        {
            // broadcast to all clients each 'sendInterval'
            // (client with authority will drop the rpc)
            // NetworkTime.localTime for double precision until Unity has it too
            //
            // IMPORTANT:
            // snapshot interpolation requires constant sending.
            // DO NOT only send if position changed. for example:
            // ---
            // * client sends first position at t=0
            // * ... 10s later ...
            // * client moves again, sends second position at t=10
            // ---
            // * server gets first position at t=0
            // * server gets second position at t=10
            // * server moves from first to second within a time of 10s
            //   => would be a super slow move, instead of a wait & move.
            //
            // IMPORTANT:
            // DO NOT send nulls if not changed 'since last send' either. we
            // send unreliable and don't know which 'last send' the other end
            // received successfully.
            //
            // Checks to ensure server only sends snapshots if object is
            // on server authority(!clientAuthority) mode because on client
            // authority mode snapshots are broadcasted right after the authoritative
            // client updates server in the command function(see above), OR,
            // since host does not send anything to update the server, any client
            // authoritative movement done by the host will have to be broadcasted
            // here by checking IsClientWithAuthority.
            if (NetworkTime.localTime >= lastServerSendTime + sendInterval &&
                (!clientAuthority || IsClientWithAuthority))
            {
                // send snapshot without timestamp.
                // receiver gets it from batch timestamp to save bandwidth.
                FOUNTSnapshot snapshot = ConstructSnapshot();
                RpcServerToClientSync(new TransformTargetData(FloatingOrigin.LocalOffset, snapshot.position, snapshot.rotation, IsOffsetChanged()));
                lastServerSendTime = NetworkTime.localTime;
            }

            // apply buffered snapshots IF client authority
            // -> in server authority, server moves the object
            //    so no need to apply any snapshots there.
            // -> don't apply for host mode player objects either, even if in
            //    client authority mode. if it doesn't go over the network,
            //    then we don't need to do anything.
            if (clientAuthority && !hasAuthority)
            {
                if (!_hasOwnerInitializedOffset) return;
                // compute snapshot interpolation & apply if any was spit out
                // TODO we don't have Time.deltaTime double yet. float is fine.
                if (SnapshotInterpolation.Compute(
                    NetworkTime.localTime, Time.deltaTime,
                    ref serverInterpolationTime,
                    bufferTime, serverBuffer,
                    catchupThreshold, catchupMultiplier,
                    Interpolate,
                    out FOUNTSnapshot computed))
                {
                    FOUNTSnapshot start = serverBuffer.Values[0];
                    FOUNTSnapshot goal = serverBuffer.Values[1];
                    ApplySnapshot(start, goal, computed);
                }
            }
        }

        void UpdateClient()
        {
            // client authority, and local player (= allowed to move myself)?
            if (IsClientWithAuthority)
            {
                // send to server each 'sendInterval'
                // NetworkTime.localTime for double precision until Unity has it too
                //
                // IMPORTANT:
                // snapshot interpolation requires constant sending.
                // DO NOT only send if position changed. for example:
                // ---
                // * client sends first position at t=0
                // * ... 10s later ...
                // * client moves again, sends second position at t=10
                // ---
                // * server gets first position at t=0
                // * server gets second position at t=10
                // * server moves from first to second within a time of 10s
                //   => would be a super slow move, instead of a wait & move.
                //
                // IMPORTANT:
                // DO NOT send nulls if not changed 'since last send' either. we
                // send unreliable and don't know which 'last send' the other end
                // received successfully.
                if (NetworkTime.localTime >= lastClientSendTime + sendInterval)
                {
                    // send snapshot without timestamp.
                    // receiver gets it from batch timestamp to save bandwidth.
                    FOUNTSnapshot snapshot = ConstructSnapshot();
                    CmdClientToServerSync(new TransformTargetData(FloatingOrigin.LocalOffset, snapshot.position,
                        snapshot.rotation, IsOffsetChanged()));

                    lastClientSendTime = NetworkTime.localTime;
                }
            }
            // for all other clients (and for local player if !authority),
            // we need to apply snapshots from the buffer
            else
            {
                if (!_hasOwnerInitializedOffset) return;
                // compute snapshot interpolation & apply if any was spit out
                // TODO we don't have Time.deltaTime double yet. float is fine.
                if (SnapshotInterpolation.Compute(
                    NetworkTime.localTime, Time.deltaTime,
                    ref clientInterpolationTime,
                    bufferTime, clientBuffer,
                    catchupThreshold, catchupMultiplier,
                    Interpolate,
                    out FOUNTSnapshot computed))
                {
                    FOUNTSnapshot start = clientBuffer.Values[0];
                    FOUNTSnapshot goal = clientBuffer.Values[1];
                    ApplySnapshot(start, goal, computed);
                }
            }
        }

        void Update()
        {
            // if server then always sync to others.
            if (isServer) UpdateServer();
            // 'else if' because host mode shouldn't send anything to server.
            // it is the server. don't overwrite anything there.
            else if (isClient) UpdateClient();
        }

        // common Teleport code for client->server and server->client
        protected virtual void OnTeleport(Vector3 destination)
        {
            // reset any in-progress interpolation & buffers
            Reset();

            // set the new position.
            // interpolation will automatically continue.
            targetComponent.position = destination;

            // TODO
            // what if we still receive a snapshot from before the interpolation?
            // it could easily happen over unreliable.
            // -> maybe add destionation as first entry?
        }

        // server->client teleport to force position without interpolation.
        // otherwise it would interpolate to a (far away) new position.
        // => manually calling Teleport is the only 100% reliable solution.
        [ClientRpc]
        public void RpcTeleport(Vector3 destination)
        {
            // NOTE: even in client authority mode, the server is always allowed
            //       to teleport the player. for example:
            //       * CmdEnterPortal() might teleport the player
            //       * Some people use client authority with server sided checks
            //         so the server should be able to reset position if needed.

            // TODO what about host mode?
            OnTeleport(destination);
        }

        // client->server teleport to force position without interpolation.
        // otherwise it would interpolate to a (far away) new position.
        // => manually calling Teleport is the only 100% reliable solution.
        [Command]
        public void CmdTeleport(Vector3 destination)
        {
            // client can only teleport objects that it has authority over.
            if (!clientAuthority) return;

            // TODO what about host mode?
            OnTeleport(destination);

            // if a client teleports, we need to broadcast to everyone else too
            // TODO the teleported client should ignore the rpc though.
            //      otherwise if it already moved again after teleporting,
            //      the rpc would come a little bit later and reset it once.
            // TODO or not? if client ONLY calls Teleport(pos), the position
            //      would only be set after the rpc. unless the client calls
            //      BOTH Teleport(pos) and targetComponent.position=pos
            RpcTeleport(destination);
        }

        protected virtual void Reset()
        {
            // disabled objects aren't updated anymore.
            // so let's clear the buffers.
            serverBuffer.Clear();
            clientBuffer.Clear();

            // reset interpolation time too so we start at t=0 next time
            serverInterpolationTime = 0;
            clientInterpolationTime = 0;
        }

        protected virtual void OnDisable() => Reset();
        protected virtual void OnEnable() => Reset();

        protected virtual void OnValidate()
        {
            // make sure that catchup threshold is > buffer multiplier.
            // for a buffer multiplier of '3', we usually have at _least_ 3
            // buffered snapshots. often 4-5 even.
            //
            // catchUpThreshold should be a minimum of bufferTimeMultiplier + 3,
            // to prevent clashes with SnapshotInterpolation looking for at least
            // 3 old enough buffers, else catch up will be implemented while there
            // is not enough old buffers, and will result in jitter.
            // (validated with several real world tests by ninja & imer)
            catchupThreshold = Mathf.Max(bufferTimeMultiplier + 3, catchupThreshold);

            // buffer limit should be at least multiplier to have enough in there
            bufferSizeLimit = Mathf.Max(bufferTimeMultiplier, bufferSizeLimit);
        }
    }
    
    
    // NetworkTransform Snapshot
    public struct FOUNTSnapshot : Snapshot
    {
        // time or sequence are needed to throw away older snapshots.
        //
        // glenn fiedler starts with a 16 bit sequence number.
        // supposedly this is meant as a simplified example.
        // in the end we need the remote timestamp for accurate interpolation
        // and buffering over time.
        //
        // note: in theory, IF server sends exactly(!) at the same interval then
        //       the 16 bit ushort timestamp would be enough to calculate the
        //       remote time (sequence * sendInterval). but Unity's update is
        //       not guaranteed to run on the exact intervals / do catchup.
        //       => remote timestamp is better for now
        //
        // [REMOTE TIME, NOT LOCAL TIME]
        // => DOUBLE for long term accuracy & batching gives us double anyway
        public double remoteTimestamp { get; set; }
        public double localTimestamp { get; set; }

        public FloatingOrigin.Offset remoteOffset;

        public Vector3 position;
        public Quaternion rotation;

        public FOUNTSnapshot(double remoteTimestamp, double localTimestamp, Vector3 position, Quaternion rotation, FloatingOrigin.Offset remoteOffset)
        {
            this.remoteOffset = remoteOffset;
            this.remoteTimestamp = remoteTimestamp;
            this.localTimestamp = localTimestamp;
            this.position = position;
            this.rotation = rotation;
        }

        public static FOUNTSnapshot Interpolate(FOUNTSnapshot from, FOUNTSnapshot to, double t)
        {
            // NOTE:
            // Vector3 & Quaternion components are float anyway, so we can
            // keep using the functions with 't' as float instead of double.
            return new FOUNTSnapshot(
                // interpolated snapshot is applied directly. don't need timestamps.
                0, 0,
                // lerp position/rotation unclamped in case we ever need
                // to extrapolate. atm SnapshotInterpolation never does.
                Vector3.LerpUnclamped(FloatingOrigin.RemoteToLocal(from.remoteOffset, from.position), FloatingOrigin.RemoteToLocal(to.remoteOffset, to.position), (float) t),
                // IMPORTANT: LerpUnclamped(0, 60, 1.5) extrapolates to ~86.
                //            SlerpUnclamped(0, 60, 1.5) extrapolates to 90!
                //            (0, 90, 1.5) is even worse. for Lerp.
                //            => Slerp works way better for our euler angles.
                Quaternion.SlerpUnclamped(from.rotation, to.rotation, (float) t),
                FloatingOrigin.LocalOffset
            );
        }
    }
}

#endif