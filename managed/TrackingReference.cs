using UnityEngine;

namespace AmanatsuLocationVR
{

    internal sealed class TrackingReference
    {
        public bool Ready { get; private set; }
        public Vector3 Position { get; private set; }
        public Quaternion Rotation { get; private set; } = Quaternion.identity;
        public Vector3 OriginPosition { get; private set; }
        public ulong OriginGeneration { get; private set; }
        bool anchorPending = true;

        public void Reset() { Ready = false; anchorPending = true; OriginGeneration = 0; }
        public void RequestPositionAnchor() { anchorPending = true; }
        public void AnchorCurrentPosition() { if (Ready) OriginPosition = Position; }

        public bool Accept(Vector3 position, Quaternion rotation, bool connected, bool valid,
            int activity, ulong generation)
        {

            if (!connected || !valid || (activity != -1 && activity != 1 && activity != 2)) return false;
            bool reanchored = anchorPending || !Ready || generation != OriginGeneration;
            if (reanchored) OriginPosition = position;
            OriginGeneration = generation;
            Position = position;
            Rotation = rotation;
            Ready = true;
            anchorPending = false;
            return reanchored;
        }

        public void Applied(bool rotationEnabled, bool positionEnabled, bool pin,
            out Vector3 translation, out Quaternion rotation)
        {
            translation = positionEnabled && !pin ? Position - OriginPosition : Vector3.zero;
            rotation = rotationEnabled ? Rotation : Quaternion.identity;
        }

        public static void HudAnchor(Vector3 rawPosition, Quaternion rawRotation,
            Vector3 appliedTranslation, Quaternion appliedRotation, float[] openVrPose)
        {

            Quaternion rotation = rawRotation * Quaternion.Inverse(appliedRotation);
            Vector3 position = rawPosition - rotation * appliedTranslation;
            ToOpenVr(position, rotation, openVrPose);
        }

        public static void ToOpenVr(Vector3 p, Quaternion q, float[] m)
        {
            var right = q * Vector3.right;
            var up = q * Vector3.up;
            var forward = q * Vector3.forward;
            m[0] = right.x; m[1] = up.x; m[2] = -forward.x; m[3] = p.x;
            m[4] = right.y; m[5] = up.y; m[6] = -forward.y; m[7] = p.y;
            m[8] = -right.z; m[9] = -up.z; m[10] = forward.z; m[11] = -p.z;
        }
    }
}
