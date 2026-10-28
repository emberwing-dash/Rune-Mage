using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace RuneMage
{
    /// <summary>One tracked hand, reduced to what the game needs.</summary>
    public sealed class HandState
    {
        public readonly bool IsRight;
        public bool Tracked;
        public Vector3 IndexTip, ThumbTip, Palm, PalmNormal;
        public bool Pinching;               // thumb + index
        public float PinchStrength;
        public bool OpenPalm;               // fingers extended, not pinching
        public Vector3 PalmVelocity;
        public float PalmSpeedAwayFromHead; // m/s, positive = pushing away from the head

        internal OVRHand Ovr;
        internal OVRSkeleton Skeleton;
        internal Transform tipT, thumbT, wristT, indexKnuckleT, pinkyKnuckleT, middleKnuckleT;
        internal Vector3 lastPalm;
        internal bool havePalm;

        public HandState(bool right) { IsRight = right; }
    }

    /// <summary>
    /// Hand tracking front end (Meta XR Core SDK OVRHand / OVRSkeleton). Produces pinch ink, push-to-cast, palm-up
    /// grimoire summon and swipe gestures. In the Editor, with no headset, the mouse stands in for the right hand so the
    /// drawing and casting can be tested on a monitor (Editor-only; the shipped game uses hands and voice only).
    /// </summary>
    public sealed class HandInput : MonoBehaviour
    {
        public Transform Head;
        public HandState Left = new HandState(false);
        public HandState Right = new HandState(true);

        public event Action PushGesture;          // open palm thrust away from the player
        public event Action<int> SwipeGesture;    // -1 left / +1 right (in the head's frame) with open hand
        public event Action GrimoireSummon;       // left palm up held
        public event Action GrimoireDismiss;      // left palm down held

        public bool AnyTracked => Left.Tracked || Right.Tracked;
        public bool UsingSimulation { get; private set; }

        float palmUpHeld, palmDownHeld, pushCooldown, swipeCooldown;
        readonly Queue<(float t, float d)> pushSamples = new Queue<(float, float)>();
        readonly Queue<(float t, float x)> swipeSamples = new Queue<(float, float)>();

        /// <summary>The hand currently inking (pinching). The right hand wins when both pinch.</summary>
        public HandState Inker => Right.Tracked && Right.Pinching ? Right : (Left.Tracked && Left.Pinching ? Left : null);

        public void Bind(OVRHand left, OVRSkeleton leftSkel, OVRHand right, OVRSkeleton rightSkel)
        {
            Left.Ovr = left; Left.Skeleton = leftSkel;
            Right.Ovr = right; Right.Skeleton = rightSkel;
        }

        void Update()
        {
            if (Head == null && Camera.main != null) Head = Camera.main.transform;
            if (Head == null) return;

            ReadOvr(Left);
            ReadOvr(Right);
            if (Scripted) ApplyScripted();
#if UNITY_EDITOR
            else if (!AnyTracked) SimulateMouseHand();
            else UsingSimulation = false;
#endif
            Derive(Left);
            Derive(Right);
            DetectGestures();
        }

        // --- OVR -------------------------------------------------------------------------------------------------

        void ReadOvr(HandState h)
        {
            h.Tracked = false;
            if (h.Ovr == null || h.Skeleton == null) return;
            if (!h.Ovr.IsTracked || !h.Ovr.IsDataHighConfidence) return;
            if (!h.Skeleton.IsInitialized || !h.Skeleton.IsDataValid || h.Skeleton.Bones == null) return;

            if (h.tipT == null) CacheBones(h);
            if (h.tipT == null || h.wristT == null) return;

            h.Tracked = true;
            h.IndexTip = h.tipT.position;
            h.ThumbTip = h.thumbT != null ? h.thumbT.position : h.IndexTip;
            Vector3 w = h.wristT.position;
            Vector3 i = h.indexKnuckleT != null ? h.indexKnuckleT.position : w + Vector3.forward * 0.08f;
            Vector3 p = h.pinkyKnuckleT != null ? h.pinkyKnuckleT.position : w + Vector3.forward * 0.08f;
            Vector3 m = h.middleKnuckleT != null ? h.middleKnuckleT.position : (i + p) * 0.5f;
            h.Palm = Vector3.Lerp(w, m, 0.6f);
            // Mirror-image hands: the cross product order flips with handedness so the normal always leaves the palm.
            h.PalmNormal = (h.IsRight ? Vector3.Cross(p - w, i - w) : Vector3.Cross(i - w, p - w)).normalized;
            h.Pinching = h.Ovr.GetFingerIsPinching(OVRHand.HandFinger.Index);
            h.PinchStrength = h.Ovr.GetFingerPinchStrength(OVRHand.HandFinger.Index);
            bool others = h.Ovr.GetFingerPinchStrength(OVRHand.HandFinger.Middle) < 0.4f &&
                          h.Ovr.GetFingerPinchStrength(OVRHand.HandFinger.Ring) < 0.4f;
            h.OpenPalm = !h.Pinching && h.PinchStrength < 0.35f && others;
        }

        void CacheBones(HandState h)
        {
            foreach (var b in h.Skeleton.Bones)
            {
                switch (b.Id)
                {
                    case OVRSkeleton.BoneId.Hand_IndexTip: h.tipT = b.Transform; break;
                    case OVRSkeleton.BoneId.Hand_ThumbTip: h.thumbT = b.Transform; break;
                    case OVRSkeleton.BoneId.Hand_WristRoot: h.wristT = b.Transform; break;
                    case OVRSkeleton.BoneId.Hand_Index1: h.indexKnuckleT = b.Transform; break;
                    case OVRSkeleton.BoneId.Hand_Middle1: h.middleKnuckleT = b.Transform; break;
                    case OVRSkeleton.BoneId.Hand_Pinky1: h.pinkyKnuckleT = b.Transform; break;
                }
            }
        }

        void Derive(HandState h)
        {
            if (!h.Tracked) { h.havePalm = false; return; }
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            if (h.havePalm) h.PalmVelocity = (h.Palm - h.lastPalm) / dt;
            h.lastPalm = h.Palm;
            h.havePalm = true;
            Vector3 away = Vector3.ProjectOnPlane(h.Palm - Head.position, Vector3.up).normalized;
            h.PalmSpeedAwayFromHead = Vector3.Dot(h.PalmVelocity, away);
        }

        // --- gestures --------------------------------------------------------------------------------------------

        void DetectGestures()
        {
            float now = Time.unscaledTime;
            pushCooldown -= Time.unscaledDeltaTime;
            swipeCooldown -= Time.unscaledDeltaTime;

            // Push: open palm facing away from the head, moving away quickly (distance gain over a short window).
            foreach (var h in new[] { Right, Left })
            {
                if (!h.Tracked || !h.OpenPalm) continue;
                Vector3 toHand = (h.Palm - Head.position);
                bool facingAway = Vector3.Dot(h.PalmNormal, toHand.normalized) > 0.35f;
                float d = Vector3.ProjectOnPlane(toHand, Vector3.up).magnitude;
                if (h.IsRight) { pushSamples.Enqueue((now, d)); while (pushSamples.Count > 0 && now - pushSamples.Peek().t > 0.35f) pushSamples.Dequeue(); }
                if (facingAway && pushCooldown <= 0f && h.PalmSpeedAwayFromHead > 0.9f)
                {
                    float gain = 0f;
                    if (h.IsRight && pushSamples.Count > 1) gain = d - pushSamples.Peek().d;
                    else gain = 0.2f;
                    if (gain > 0.12f)
                    {
                        pushCooldown = 0.7f;
                        PushGesture?.Invoke();
                    }
                }
            }

            // Left palm up near chest = summon, palm down = dismiss.
            if (Left.Tracked)
            {
                bool inFront = Vector3.Dot((Left.Palm - Head.position), Head.forward) > 0.18f;
                palmUpHeld = Left.PalmNormal.y > 0.72f && inFront ? palmUpHeld + Time.unscaledDeltaTime : 0f;
                palmDownHeld = Left.PalmNormal.y < -0.72f && Left.OpenPalm ? palmDownHeld + Time.unscaledDeltaTime : 0f;
                if (palmUpHeld > 0.55f) { palmUpHeld = -1.5f; GrimoireSummon?.Invoke(); }
                if (palmDownHeld > 0.8f) { palmDownHeld = -1.5f; GrimoireDismiss?.Invoke(); }
            }
            else { palmUpHeld = Mathf.Min(0, palmUpHeld); palmDownHeld = Mathf.Min(0, palmDownHeld); }

            // Swipe: open right hand sweeping sideways fast (turns grimoire pages).
            if (Right.Tracked && Right.OpenPalm && swipeCooldown <= 0f)
            {
                float side = Vector3.Dot(Right.PalmVelocity, Head.right);
                if (Mathf.Abs(side) > 1.3f && Mathf.Abs(Right.PalmVelocity.y) < Mathf.Abs(side))
                {
                    swipeCooldown = 0.8f;
                    SwipeGesture?.Invoke(side > 0 ? 1 : -1);
                }
            }
        }

        // --- automation hook (automated tests, scripted demos) -----------------------------------------------------

        public bool Scripted;
        public Vector3 ScriptedTip;
        public bool ScriptedPinch;

        public void TriggerPush() => PushGesture?.Invoke();
        public void TriggerGrimoire(bool open) { if (open) GrimoireSummon?.Invoke(); else GrimoireDismiss?.Invoke(); }

        void ApplyScripted()
        {
            var h = Right;
            h.Tracked = true;
            h.IndexTip = ScriptedTip;
            h.ThumbTip = ScriptedTip;
            h.Palm = ScriptedTip - Head.forward * 0.05f;
            h.PalmNormal = Head.forward;
            h.Pinching = ScriptedPinch;
            h.PinchStrength = ScriptedPinch ? 1f : 0f;
            h.OpenPalm = !ScriptedPinch;
            UsingSimulation = true;
        }

        // --- Editor-only mouse stand-in -------------------------------------------------------------------------

#if UNITY_EDITOR
        Vector3 simTip;
        void SimulateMouseHand()
        {
            var mouse = Mouse.current; var kb = Keyboard.current;
            var cam = Camera.main;
            if (mouse == null || cam == null) return;
            UsingSimulation = true;

            float depth = 0.55f + (kb != null && kb.eKey.isPressed ? 0.2f : 0f) - (kb != null && kb.qKey.isPressed ? 0.2f : 0f);
            Vector2 mp = mouse.position.ReadValue();
            var ray = cam.ScreenPointToRay(mp);
            simTip = ray.origin + ray.direction * depth;

            var h = Right;
            h.Tracked = true;
            h.IndexTip = simTip;
            h.ThumbTip = simTip;
            h.Palm = simTip + Head.forward * -0.05f;
            h.PalmNormal = Head.forward;
            h.Pinching = mouse.leftButton.isPressed;
            h.PinchStrength = h.Pinching ? 1f : 0f;
            h.OpenPalm = !h.Pinching;
            if (kb != null && kb.spaceKey.wasPressedThisFrame) { pushCooldown = 0.7f; PushGesture?.Invoke(); }
            if (kb != null && kb.gKey.wasPressedThisFrame) GrimoireSummon?.Invoke();
            if (kb != null && kb.hKey.wasPressedThisFrame) GrimoireDismiss?.Invoke();
            if (kb != null && kb.rightArrowKey.wasPressedThisFrame) SwipeGesture?.Invoke(1);
            if (kb != null && kb.leftArrowKey.wasPressedThisFrame) SwipeGesture?.Invoke(-1);
        }
#endif
    }
}
