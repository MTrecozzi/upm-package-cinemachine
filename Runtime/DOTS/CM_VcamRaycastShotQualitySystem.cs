using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using System.Runtime.CompilerServices;
using Unity.Transforms;

namespace Cinemachine.ECS
{
    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamFinalizeSystem))]
    [UpdateBefore(typeof(CM_VcamPrioritySystem))]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public class CM_VcamRaycastShotQualitySystem : JobComponentSystem
    {
        ComponentGroup m_mainGroup;

        protected override void OnCreateManager()
        {
            m_mainGroup = GetComponentGroup(
                ComponentType.ReadWrite<CM_VcamShotQuality>(),
                ComponentType.ReadOnly<CM_VcamPositionState>(),
                ComponentType.ReadOnly<CM_VcamRotationState>(),
                ComponentType.ReadOnly<CM_VcamLensState>());
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // GML todo: should use corrected position/orientation

            var objectCount = m_mainGroup.CalculateLength();

            // These will be deallocated by the final job
            var raycastCommands = new NativeArray<RaycastCommand>(objectCount, Allocator.TempJob);
            var raycastHits = new NativeArray<RaycastHit>(objectCount, Allocator.TempJob);

            var setupRaycastsJob = new SetupRaycastsJob()
            {
                layerMask = -5, // GML todo: how to set this?
                minDstanceFromTarget = 0, // GML todo: how to set this?
                raycasts = raycastCommands
            };
            var setupDependency = setupRaycastsJob.ScheduleGroup(m_mainGroup, inputDeps);
            var raycastDependency = RaycastCommand.ScheduleBatch(
                raycastCommands, raycastHits, 32, setupDependency);

            var qualityJob = new CalculateQualityJob()
            {
                isOrthographic = false, // GML fixme
                aspect = (float)Screen.width / (float)Screen.height, // GML fixme
                hits = raycastHits,         // deallocates on completion
                raycasts = raycastCommands  // deallocates on completion
            };
            return qualityJob.ScheduleGroup(m_mainGroup, raycastDependency);
        }

        [BurstCompile]
        struct SetupRaycastsJob : IJobProcessComponentDataWithEntity<CM_VcamPositionState, CM_VcamRotationState>
        {
            public int layerMask;
            public float minDstanceFromTarget;
            public NativeArray<RaycastCommand> raycasts;

            // GML todo: handle IgnoreTag or something like that ?

            public void Execute(
                Entity entity, int index,
                [ReadOnly] ref CM_VcamPositionState posState, [ReadOnly] ref CM_VcamRotationState rotState)
            {
                // GML todo: check for no lookAt condition

                // cast back towards the camera to filter out target's collider
                float3 dir = posState.raw - rotState.lookAtPoint;
                float distance = math.length(dir);
                dir /= distance;
                raycasts[index] = new RaycastCommand(
                    rotState.lookAtPoint + minDstanceFromTarget * dir, dir,
                    math.max(0, distance - minDstanceFromTarget), layerMask);
            }
        }

        [BurstCompile]
        struct CalculateQualityJob : IJobProcessComponentDataWithEntity<
            CM_VcamShotQuality, CM_VcamPositionState, CM_VcamRotationState, CM_VcamLensState>
        {
            public bool isOrthographic;
            public float aspect;
            [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<RaycastHit> hits;
            [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<RaycastCommand> raycasts;

            public void Execute(
                Entity entity, int index,
                ref CM_VcamShotQuality shotQuality, [ReadOnly] ref CM_VcamPositionState posState,
                [ReadOnly] ref CM_VcamRotationState rotState, [ReadOnly] ref CM_VcamLensState lens)
            {
                bool noObstruction = hits[index].normal == Vector3.zero;

                float3 offset = rotState.lookAtPoint - (posState.raw + posState.correction);
                offset = math.mul(math.inverse(rotState.raw), offset); // camera-space
                var fov = lens.fov;
                bool isOnscreen =
                    (!isOrthographic & IsTargetOnscreen(offset, fov, aspect))
                    | (isOrthographic & IsTargetOnscreenOrtho(offset, fov, aspect));

                bool isVisible = noObstruction && isOnscreen;
                shotQuality = new CM_VcamShotQuality { value = math.select(0f, 1f, isVisible) };
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsTargetOnscreen(float3 dir, float size, float aspect)
        {
            float fovY = 0.5f * math.radians(size);    // size is fovH in deg.  need half-fov in rad
            float2 fov = new float2(math.atan(math.tan(fovY) * aspect), fovY);
            float2 angle = new float2(
                MathHelpers.AngleUnit(
                    math.normalize(dir.ProjectOntoPlane(math.up())), new float3(0, 0, 1)),
                MathHelpers.AngleUnit(
                    math.normalize(dir.ProjectOntoPlane(new float3(1, 0, 0))), new float3(0, 0, 1)));
            return math.all(angle <= fov);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsTargetOnscreenOrtho(float3 dir, float size, float aspect)
        {
            float2 s = new float2(size * aspect, size);
            return math.all(math.abs(new float2(dir.x, dir.y)) < s);
        }
    }
}