/// Put this on objects that don't move and you need to spawn using NetworkServer.Spawn()
/// This will correct the start position to adjust from the host's offset.

#if MIRROR_43_0_OR_NEWER

using System;
using System.Diagnostics;
using Mirror;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace twoloop
{
    public class FOUStartAdjuster : FOUNetTransformBase
    {
        [SyncVar] private FloatingOrigin.AbsolutePosition absolutePosition;
        [SyncVar] private Quaternion rotation;
        [SyncVar] private Vector3 localScale;

        public void Awake()
        {
            if (NetworkServer.active)
            {
                this.absolutePosition =
                    new FloatingOrigin.AbsolutePosition(FloatingOrigin.LocalOffset, transform.position);
                this.rotation = transform.rotation;
                this.localScale = transform.localScale;
            }
        }

        public override void OnStartServer()
        {
            this.absolutePosition =
                new FloatingOrigin.AbsolutePosition(FloatingOrigin.LocalOffset, transform.position);
            this.rotation = transform.rotation;
            this.localScale = transform.localScale;
        }
        
        public override void OnStartClient()
        {
            if (!isServer)
            {
                transform.position = absolutePosition.ToLocal();
                transform.rotation = rotation;
                transform.localScale = localScale;
            }
        }
    }
}
#endif