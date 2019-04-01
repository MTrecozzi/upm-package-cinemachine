using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace Cinemachine.ECS
{
    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamFinalizeSystem))]
    [UpdateBefore(typeof(CM_ChannelSystem))]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public class CM_VcamRaycastShotQualitySystem : JobComponentSystem
    {
        ComponentGroup m_vcamGroup;

        protected override void OnCreateManager()
        {
            m_vcamGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_VcamChannel>(),
                ComponentType.ReadWrite<CM_VcamShotQuality>(),
                ComponentType.ReadOnly<CM_VcamPositionState>(),
                ComponentType.ReadOnly<CM_VcamRotationState>(),
                ComponentType.ReadOnly<CM_VcamLensState>());
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var channelSystem = World.GetOrCreateSystem<CM_ChannelSystem>();
            JobHandle jobDeps = channelSystem.InvokePerVcamChannel(
                m_vcamGroup, inputDeps, new QualityJobLaunch() );
            return jobDeps;
        }

        struct QualityJobLaunch : CM_ChannelSystem.VcamGroupCallback
        {
            public JobHandle Invoke(
                ComponentGroup filteredGroup, Entity channelEntity,
                CM_Channel c, CM_ChannelState state, JobHandle inputDeps)
            {
                var objectCount = filteredGroup.CalculateLength();

                // These will be deallocated by the final job
                var raycastCommands = new NativeArray<RaycastCommand>(objectCount, Allocator.TempJob);
                var raycastHits = new NativeArray<RaycastHit>(objectCount, Allocator.TempJob);

                var setupRaycastsJob = new SetupRaycastsJob()
                {
                    layerMask = -5, // GML todo: how to set this?
                    minDstanceFromTarget = 0, // GML todo: how to set this?
                    raycasts = raycastCommands
                };
                var setupDeps = setupRaycastsJob.ScheduleGroup(filteredGroup, inputDeps);
                var raycastDeps = RaycastCommand.ScheduleBatch(raycastCommands, raycastHits, 32, setupDeps);

                if (c.settings.IsOrthographic)
                {
                    var qualityJobOrtho = new CalculateQualityJobOrtho()
                    {
                        aspect = c.settings.aspect,
                        hits = raycastHits,         // deallocates on completion
                        raycasts = raycastCommands  // deallocates on completion
                    };
                    return qualityJobOrtho.ScheduleGroup(filteredGroup, raycastDeps);
                }

                var qualityJob = new CalculateQualityJob()
                {
                    aspect = c.settings.aspect,
                    hits = raycastHits,         // deallocates on completion
                    raycasts = raycastCommands  // deallocates on completion
                };
                return qualityJob.ScheduleGroup(filteredGroup, raycastDeps);
            }
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
                [ReadOnly] ref CM_VcamPositionState posState,
                [ReadOnly] ref CM_VcamRotationState rotState)
            {
                // GML hack
                if (index >= raycasts.Length)
                    return; // WTF!!!!

                // GML todo: check for no lookAt condition

                // cast back towards the camera to filter out target's collider
                float3 dir = posState.GetCorrected() - rotState.lookAtPoint;
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
            public float aspect;
            [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<RaycastHit> hits;
            [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<RaycastCommand> raycasts;

            public void Execute(
                Entity entity, int index,
                ref CM_VcamShotQuality shotQuality, [ReadOnly] ref CM_VcamPositionState posState,
                [ReadOnly] ref CM_VcamRotationState rotState, [ReadOnly] ref CM_VcamLensState lens)
            {
                // GML hack
                if (index >= raycasts.Length)
                    return; // WTF!!!!

                float3 offset = rotState.lookAtPoint - posState.GetCorrected();
                offset = math.mul(math.inverse(rotState.GetCorrected()), offset); // camera-space
                bool isOnscreen = IsTargetOnscreen(offset, lens.fov, aspect);
                bool noObstruction = hits[index].normal == Vector3.zero;
                bool isVisible = noObstruction && isOnscreen;
                shotQuality.value = math.select(0f, 1f, isVisible);
            }
        }

        [BurstCompile]
        struct CalculateQualityJobOrtho : IJobProcessComponentDataWithEntity<
            CM_VcamShotQuality, CM_VcamPositionState, CM_VcamRotationState, CM_VcamLensState>
        {
            public float aspect;
            [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<RaycastHit> hits;
            [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<RaycastCommand> raycasts;

            public void Execute(
                Entity entity, int index,
                ref CM_VcamShotQuality shotQuality, [ReadOnly] ref CM_VcamPositionState posState,
                [ReadOnly] ref CM_VcamRotationState rotState, [ReadOnly] ref CM_VcamLensState lens)
            {
                // GML hack
                if (index >= raycasts.Length)
                    return; // WTF!!!!

                float3 offset = rotState.lookAtPoint - posState.GetCorrected();
                offset = math.mul(math.inverse(rotState.GetCorrected()), offset); // camera-space
                bool isOnscreen = IsTargetOnscreenOrtho(offset, lens.fov, aspect);
                bool noObstruction = hits[index].normal == Vector3.zero;
                bool isVisible = noObstruction && isOnscreen;
                shotQuality.value = math.select(0f, 1f, isVisible);
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