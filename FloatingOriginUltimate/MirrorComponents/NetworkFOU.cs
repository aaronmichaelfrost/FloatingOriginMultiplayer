#if MIRROR_43_0_OR_NEWER

using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.Networking.Types;

namespace twoloop
{
    /// <summary>
    /// Singleton network component that stores the host's offset.
    /// You should have this in your gameplay scene if you are doing multiplayer.
    /// </summary>
    public class NetworkFOU : NetworkBehaviour
    {
        public static NetworkFOU singleton;
        
        // Synced variable used to update hostOffset
        [SyncVar(hook = nameof(OnHostOffsetChanged))] private FloatingOrigin.Offset _hostOffset;

        // The host of the server's world offset
        public static FloatingOrigin.Offset hostOffset;

        private void Awake()
        {
            if (!singleton)
            {
                singleton = this;
            }
            else
            {
                Debug.LogError("There cannot be two NetworkFOU's in the scene.");
                Destroy(this);
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            if (!isServer)
            {
                RecenterNetworkIdentities();
            }
        }

        /// <summary>
        /// Fixes the position of scene objects that do not have FOUNet transform / rigidbody components on them.
        /// </summary>
        private void RecenterNetworkIdentities()
        {
            var all = FindObjectsOfType<NetworkIdentity>();
            foreach (var networkComponent in all)
            {
                if (!networkComponent.GetComponent<FOUNetTransformBase>())
                {
                    networkComponent.transform.position =
                        FloatingOrigin.RemoteToLocal(hostOffset, networkComponent.transform.position);
                }
            }
        }

        [Server]
        public void SetHostOffset(FloatingOrigin.Offset value)
        {
            _hostOffset = value;
            hostOffset = value;
        }

        private void OnHostOffsetChanged(FloatingOrigin.Offset oldValue, FloatingOrigin.Offset newValue)
        {
            hostOffset = newValue;
        }
    }
}
#endif