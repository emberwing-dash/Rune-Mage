using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace RuneMage
{
    /// <summary>
    /// Room understanding through the Meta XR MR Utility Kit. Everything degrades gracefully: with no scene data (editor, or the
    /// player skipped Space Setup) enemies spawn on an arc in front of the player at a safe distance, and the floor is assumed
    /// to be at tracking-origin zero (the rig uses floor-level tracking).
    /// </summary>
    public sealed class MrSpatial : MonoBehaviour
    {
        Game game;
        MRUKRoom room;

        public bool HasRoom => room != null;
        public float FloorY { get; private set; }
        public string Status { get; private set; } = "no room data (using fallback layout)";

        public void Init(Game g)
        {
            game = g;
            TryBind();
        }

        void TryBind()
        {
            var mruk = MRUK.Instance;
            if (mruk == null) return;
            mruk.SceneLoadedEvent.AddListener(OnSceneLoaded);
            if (mruk.IsInitialized) OnSceneLoaded();
        }

        void OnSceneLoaded()
        {
            var mruk = MRUK.Instance;
            if (mruk == null) return;
            room = mruk.GetCurrentRoom();
            if (room == null) return;
            if (room.FloorAnchor != null) FloorY = room.FloorAnchor.transform.position.y;
            Status = "room loaded: " + room.Anchors.Count + " anchors";
            Debug.Log("[RuneMage] MRUK " + Status + ", floor y=" + FloorY);
        }

        void Update()
        {
            if (room == null && Time.frameCount % 90 == 0) TryBind();
        }

        public bool IsInsideRoom(Vector3 p)
        {
            if (room == null) return true;
            return room.IsPositionInRoom(p, false);
        }

        /// <summary>A floor point 2.4 to 4.8 m from the player, preferably within their field of view.</summary>
        public bool FindSpawnPoint(out Vector3 pos, out Vector3 normal)
        {
            var head = game.Hands.Head;
            Vector3 hp = head != null ? head.position : Vector3.zero;
            Vector3 fwd = head != null ? Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized : Vector3.forward;
            normal = Vector3.up;

            if (room != null)
            {
                for (int i = 0; i < 30; i++)
                {
                    if (room.GenerateRandomPositionOnSurface(MRUK.SurfaceType.FACING_UP, 0.35f,
                            LabelFilter.Included(MRUKAnchor.SceneLabels.FLOOR), out Vector3 p, out Vector3 n))
                    {
                        Vector3 flat = Vector3.ProjectOnPlane(p - hp, Vector3.up);
                        float d = flat.magnitude;
                        if (d < 2.4f || d > 4.8f) continue;
                        if (i < 22 && Vector3.Angle(fwd, flat) > 75f) continue;   // prefer in front of the player
                        pos = new Vector3(p.x, FloorY, p.z);
                        normal = n;
                        return true;
                    }
                }
            }

            // fallback arc
            float ang = Random.Range(-60f, 60f);
            Vector3 dir = Quaternion.AngleAxis(ang, Vector3.up) * fwd;
            pos = hp + dir * Random.Range(3.0f, 3.8f);
            pos.y = FloorY;
            return false;
        }

        /// <summary>Largest free spot for a placed object (shard / NPC) at a given height on a surface, falling back to the player's front.</summary>
        public Vector3 FindSurfacePoint(Vector3 fallback)
        {
            if (room != null &&
                room.GenerateRandomPositionOnSurface(MRUK.SurfaceType.FACING_UP, 0.2f,
                    LabelFilter.Included(MRUKAnchor.SceneLabels.TABLE | MRUKAnchor.SceneLabels.COUCH | MRUKAnchor.SceneLabels.BED), out Vector3 p, out _))
                return p + Vector3.up * 0.05f;
            return fallback;
        }
    }
}
