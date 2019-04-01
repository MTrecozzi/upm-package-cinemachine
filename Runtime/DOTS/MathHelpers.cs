using Unity.Mathematics;
using System.Runtime.CompilerServices;

namespace Cinemachine.ECS
{
    public static class MathHelpers
    {
        /// <summary>A useful Epsilon</summary>
        public const float Epsilon = 0.0001f;

        /// <summary>Is the vector within Epsilon of zero length?</summary>
        /// <param name="v"></param>
        /// <returns>True if the square magnitude of the vector is within Epsilon of zero</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AlmostZero(this float3 v)
        {
            return math.lengthsq(v) < 0.000001f;
        }

        /// <summary>
        /// Returns a non-normalized projection of the supplied vector onto a plane
        /// as described by its normal
        /// </summary>
        /// <param name="vector"></param>
        /// <param name="planeNormal">The normal that defines the plane.  Must have a length of 1.</param>
        /// <returns>The component of the vector that lies in the plane</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 ProjectOntoPlane(this float3 vector, float3 planeNormal)
        {
            return (vector - math.dot(vector, planeNormal) * planeNormal);
        }

        /// <summary>Much more stable for small angles than Unity's native implementation.
        /// Directions must be unit length.  Returns radians</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float AngleUnit(float3 fromUnit, float3 toUnit)
        {
            return math.atan2(math.length(fromUnit - toUnit), math.length(fromUnit + toUnit)) * 2;
        }

        /// <summary>Get a unit axis perpendicular to both vectors.  This is a normalized cross
        /// product, with a default axis for handling colinear vectors</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 Axis(float3 from, float3 to, float3 defaultAxisUnit)
        {
            float3 cross = math.cross(from, to);
            float len = math.length(cross);
            return math.select(defaultAxisUnit, cross / len, len > Epsilon);
        }

        /// <summary>Much more stable for small angles than Unity's native implementation.
        /// Directions must be unit length.  Returns radians</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SignedAngleUnit(float3 fromUnit, float3 toUnit, float3 upUnit)
        {
            float angle = AngleUnit(fromUnit, toUnit);
            return math.select(
                angle, -angle,
                math.sign(math.dot(upUnit, math.cross(fromUnit, toUnit))) < 0);
        }

        /// <summary>Returns the quaternion that will rotate from one direction to another.
        /// Directions must be unit length</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static quaternion FromToRotationUnit(
            float3 fromUnit, float3 toUnit, float3 defaultAxisUnit)
        {
            return quaternion.AxisAngle(
                Axis(fromUnit, toUnit, defaultAxisUnit),
                AngleUnit(fromUnit, toUnit));
        }

        /// <summary>LookRotation with conservative handling for looking at up/down poles</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static quaternion LookRotationUnit(this quaternion q, float3 fwdUnit, float3 upUnit)
        {
            float crossLen = math.length(math.cross(upUnit, fwdUnit));
            return math.select(
                quaternion.LookRotation(fwdUnit, upUnit).value,
                FromToRotationUnit(math.forward(q), fwdUnit, math.mul(q, new float3(1, 0, 0))).value,
                crossLen < Epsilon);
        }

        /// <summary>Returns the quaternion that will rotate from one orientation to another</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static quaternion FromToRotation(quaternion from, quaternion to)
        {
            return math.mul(math.inverse(from), to);
        }

        /// <summary>Rotate a quaternion so that its up matches the desired direction.
        /// Up must be unit length</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static quaternion Uppify(quaternion q, float3 upUnit)
        {
            return math.mul(FromToRotationUnit(math.mul(q, math.up()), upUnit, math.forward(q)), q);
        }

        /// <summary>The amount left after dampTime.  Exposed for testing purposes only </summary>
        public const float kNegligibleResidual = 0.01f;

        /// <summary>Get a damped version of a quantity.  This is the portion of the
        /// quantity that will take effect over the given time.</summary>
        /// <param name="initial">The amount that will be damped</param>
        /// <param name="dampTime">The rate of damping.  This is the time it would
        /// take to apply the entire amount</param>
        /// <param name="deltaTime">The time over which to damp</param>
        /// <param name="fixedDeltaTime">If nonzero, this indicates how to break down
        /// deltaTime to give more consistent results in situations of variable framerate</param>
        /// <returns>The damped amount.  This will be the original amount scaled by
        /// a value between 0 and 1.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Damp(
            float initial, float dampTime,
            float deltaTime, float fixedDeltaTime = 0)
        {
            /// GML todo: optimize! get rid of those ifs!
            if (math.abs(initial) < Epsilon || deltaTime < 0)
                return initial;
            if (deltaTime < Epsilon)
                return 0;

            // Try to reduce damage caused by deltaTime variability
            float step = math.select(
                fixedDeltaTime / 5, deltaTime,
                fixedDeltaTime == 0 || fixedDeltaTime == deltaTime);

            const float kLogNegligibleResidual = -4.605170186f; // == math.Log(kNegligibleResidual=0.01f);
            float decayConstant = math.select(
                0, math.exp(kLogNegligibleResidual * step / dampTime), dampTime > Epsilon);

            float vel = initial * step / deltaTime;
            int numSteps = (int)math.floor(deltaTime / step);
            float r = 0;
            for (int i = 0; i < numSteps; ++i)
                r = (r + vel) * decayConstant;

            float d = deltaTime - (step * numSteps);
            r = math.lerp(r, (r + vel) * decayConstant, d / step);

            return initial - r;
        }

        /// <summary>Get a damped version of a quantity.  This is the portion of the
        /// quantity that will take effect over the given time.</summary>
        /// <param name="initial">The amount that will be damped</param>
        /// <param name="dampTime">The rate of damping.  This is the time it would
        /// take to apply the entire amount</param>
        /// <param name="deltaTime">The time over which to damp</param>
        /// <param name="fixedDeltaTime">If nonzero, this indicates how to break down
        /// deltaTime to give more consistent results in situations of variable framerate</param>
        /// <returns>The damped amount.  This will be the original amount scaled by
        /// a value between 0 and 1.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 Damp(
            float2 initial, float2 dampTime,
            float deltaTime, float fixedDeltaTime = 0)
        {
            /// GML todo: optimize! get rid of those ifs!
            if (math.cmax(math.abs(initial)) < Epsilon || deltaTime < 0)
                return initial;
            if (deltaTime < Epsilon)
                return 0;

            // Try to reduce damage caused by deltaTime variability
            float step = math.select(
                fixedDeltaTime / 5, deltaTime,
                fixedDeltaTime == 0 || fixedDeltaTime == deltaTime);

            const float kLogNegligibleResidual = -4.605170186f; // == math.Log(kNegligibleResidual=0.01f);
            float2 decayConstant = math.select(
                0, math.exp(kLogNegligibleResidual * step / dampTime), dampTime > Epsilon);

            float2 vel = initial * step / deltaTime;
            int numSteps = (int)math.floor(deltaTime / step);
            float2 r = 0;
            for (int i = 0; i < numSteps; ++i)
                r = (r + vel) * decayConstant;

            float d = deltaTime - (step * numSteps);
            r = math.lerp(r, (r + vel) * decayConstant, d / step);

            return initial - r;
        }

        /// <summary>Get a damped version of a quantity.  This is the portion of the
        /// quantity that will take effect over the given time.</summary>
        /// <param name="initial">The amount that will be damped</param>
        /// <param name="dampTime">The rate of damping.  This is the time it would
        /// take to apply the entire amount</param>
        /// <param name="deltaTime">The time over which to damp</param>
        /// <param name="fixedDeltaTime">If nonzero, this indicates how to break down
        /// deltaTime to give more consistent results in situations of variable framerate</param>
        /// <returns>The damped amount.  This will be the original amount scaled by
        /// a value between 0 and 1.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 Damp(
            float3 initial, float3 dampTime,
            float deltaTime, float fixedDeltaTime = 0)
        {
            /// GML todo: optimize! get rid of those ifs!
            if (math.cmax(math.abs(initial)) < Epsilon || deltaTime < 0)
                return initial;
            if (deltaTime < Epsilon)
                return 0;

            // Try to reduce damage caused by deltaTime variability
            float step = math.select(
                fixedDeltaTime / 5, deltaTime,
                fixedDeltaTime == 0 || fixedDeltaTime == deltaTime);

            const float kLogNegligibleResidual = -4.605170186f; // == math.Log(kNegligibleResidual=0.01f);
            float3 decayConstant = math.select(
                0, math.exp(kLogNegligibleResidual * step / dampTime), dampTime > Epsilon);

            float3 vel = initial * step / deltaTime;
            int numSteps = (int)math.floor(deltaTime / step);
            float3 r = 0;
            for (int i = 0; i < numSteps; ++i)
                r = (r + vel) * decayConstant;

            float d = deltaTime - (step * numSteps);
            r = math.lerp(r, (r + vel) * decayConstant, d / step);

            return initial - r;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Bezier(float t, float p0, float p1, float p2, float p3)
        {
            t = math.clamp(t, 0, 1);
            float d = 1f - t;
            return d * d * d * p0 + 3f * d * d * t * p1
                + 3f * d * t * t * p2 + t * t * t * p3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 Bezier(float t, float2 p0, float2 p1, float2 p2, float2 p3)
        {
            t = math.clamp(t, 0, 1);
            float d = 1f - t;
            return d * d * d * p0 + 3f * d * d * t * p1
                + 3f * d * t * t * p2 + t * t * t * p3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 Bezier(float t, float3 p0, float3 p1, float3 p2, float3 p3)
        {
            t = math.clamp(t, 0, 1);
            float d = 1f - t;
            return d * d * d * p0 + 3f * d * d * t * p1
                + 3f * d * t * t * p2 + t * t * t * p3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 Bezier(float t, float4 p0, float4 p1, float4 p2, float4 p3)
        {
            t = math.clamp(t, 0, 1);
            float d = 1f - t;
            return d * d * d * p0 + 3f * d * d * t * p1
                + 3f * d * t * t * p2 + t * t * t * p3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float BezierTangent(
            float t, float p0, float p1, float p2, float p3)
        {
            t = math.clamp(t, 0, 1);
            return (-3f * p0 + 9f * p1 - 9f * p2 + 3f * p3) * t * t
                +  (6f * p0 - 12f * p1 + 6f * p2) * t
                -  3f * p0 + 3f * p1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 BezierTangent(
            float t, float2 p0, float2 p1, float2 p2, float2 p3)
        {
            t = math.clamp(t, 0, 1);
            return (-3f * p0 + 9f * p1 - 9f * p2 + 3f * p3) * t * t
                +  (6f * p0 - 12f * p1 + 6f * p2) * t
                -  3f * p0 + 3f * p1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 BezierTangent(
            float t, float3 p0, float3 p1, float3 p2, float3 p3)
        {
            t = math.clamp(t, 0, 1);
            return (-3f * p0 + 9f * p1 - 9f * p2 + 3f * p3) * t * t
                +  (6f * p0 - 12f * p1 + 6f * p2) * t
                -  3f * p0 + 3f * p1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 BezierTangent(
            float t, float4 p0, float4 p1, float4 p2, float4 p3)
        {
            t = math.clamp(t, 0, 1);
            return (-3f * p0 + 9f * p1 - 9f * p2 + 3f * p3) * t * t
                +  (6f * p0 - 12f * p1 + 6f * p2) * t
                -  3f * p0 + 3f * p1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Bias(float t, float b)
        {
            return (math.clamp(t, 0, 1) / ((((1f/math.clamp(b, 0, 1)) - 2f) * (1f - t)) + 1f));
        }

        public struct rect2d { public float2 pos; public float2 size; }
        public struct rect3d { public float3 pos; public float3 size; }

        /// <summary>
        /// Get the rotations, first about world up, then about (travelling) local right,
        /// necessary to align the quaternion's forward with the target direction.
        /// This represents the tripod head movement needed to look at the target.
        /// This formulation makes it easy to interpolate without introducing spurious roll.
        /// </summary>
        /// <param name="orient"></param>
        /// <param name="lookAtDirUnit">The worldspace target direction (must be unit vector)
        /// in which we want to look</param>
        /// <param name="worldUpUnit">Which way is up.  Must have a length of 1.</param>
        /// <returns>Vector2.x is rotation about worldUp, and Vector2.y is second rotation,
        /// about local right.  All in radians.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 GetCameraRotationToTarget(
            this quaternion orient, float3 lookAtDirUnit, float3 worldUpUnit)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (math.abs(math.length(lookAtDirUnit) - 1) > Epsilon)
                throw new System.IndexOutOfRangeException("lookAtDirUnit must be unit length");
            if (math.abs(math.length(worldUpUnit) - 1) > Epsilon)
                throw new System.IndexOutOfRangeException("worldUpUnit must be unit length");
#endif
            // Work in local space
            quaternion toLocal = math.inverse(orient);
            float3 up = math.mul(toLocal, worldUpUnit);
            lookAtDirUnit = math.mul(toLocal, lookAtDirUnit);

            // Align yaw based on world up
            var d0 = lookAtDirUnit.ProjectOntoPlane(up);
            var d0Len = math.length(d0);
            var f = new float3(0, 0, 1).ProjectOntoPlane(up);
            var fLen = math.length(f);
            var pole = new float3(0, math.select(1, -1, math.dot(f, up) < 0), 0).ProjectOntoPlane(up);
            float angleH = math.select(
                SignedAngleUnit(math.select(f/fLen, pole, fLen < Epsilon), d0/d0Len, up),
                0,
                d0Len < Epsilon);
            var q = quaternion.AxisAngle(up, angleH);

            // Get local vertical angle
            float angleV = SignedAngleUnit(
                math.mul(q, new float3(0, 0, 1)),
                lookAtDirUnit,
                math.mul(q, new float3(1, 0, 0)));

            return new float2(angleH, angleV);
        }

        /// <summary>
        /// Apply rotations, first about world up, then about (travelling) local right.
        /// rot.y is rotation about worldUp, and rot.x is second rotation, about local right.
        /// </summary>
        /// <param name="orient"></param>
        /// <param name="rot">Vector2.x is rotation about worldUp, and Vector2.y is second rotation,
        /// about local right.  Angles in radians.</param>
        /// <param name="worldUp">Which way is up</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static quaternion ApplyCameraRotation(
            this quaternion orient, float2 rot, float3 up)
        {
            quaternion q = quaternion.AxisAngle(new float3(1, 0, 0), rot.y);
            return math.mul(math.mul(quaternion.AxisAngle(up, rot.x), orient), q);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 GetTranslationFromTRS(this float4x4 m)
        {
            return m.c3.xyz;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 GetScaleFromTRS(this float4x4 m)
        {
            return new float3(math.length(m.c0), math.length(m.c1), math.length(m.c2));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static quaternion GetRotationFromTRS(this float4x4 m)
        {
            var s = m.GetScaleFromTRS();
            float3x3 r = new float3x3(m.c0.xyz / s, m.c1.xyz / s, m.c2.xyz / s);
            return math.normalizesafe(new quaternion(r));
        }
    }
}