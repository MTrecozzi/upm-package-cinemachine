using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Transforms;
using Unity.Mathematics;

namespace Cinemachine.ECS
{
    [UpdateAfter(typeof(CM_VcamFinalizeSystem))]
    public class CM_VcamPushToTransformSystem : JobComponentSystem
    {
        ComponentGroup m_mainGroup;

        protected override void OnCreateManager()
        {
            m_mainGroup = GetComponentGroup(
                ComponentType.Create<LocalToWorld>(), 
                ComponentType.ReadOnly<CM_VcamPosition>(), 
                ComponentType.ReadOnly<CM_VcamRotation>());
        }

        [BurstCompile]
        struct PushToTransformJob : IJobParallelFor
        {
            public ComponentDataArray<LocalToWorld> positions;
            [ReadOnly] public ComponentDataArray<CM_VcamPosition> vcamPositions;
            [ReadOnly] public ComponentDataArray<CM_VcamRotation> vcamRotations;

            public void Execute(int index)
            {
                var m = positions[index].Value; m.c0.w = m.c1.w = m.c2.w = 0; // GML todo: just get float3x3 instead
                float4 v = new float4(0.5773503f, 0.5773503f, 0.5773503f, 0); // unit vector
                var scale = float4x4.Scale(math.length(math.mul(m, v))); // approximate uniform scale
                positions[index] = new LocalToWorld 
                { 
                    Value = math.mul(new float4x4(vcamRotations[index].raw, vcamPositions[index].raw), scale)
                };
            }
        }
        
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var job = new PushToTransformJob
            {
                positions = m_mainGroup.GetComponentDataArray<LocalToWorld>(),
                vcamPositions = m_mainGroup.GetComponentDataArray<CM_VcamPosition>(),
                vcamRotations = m_mainGroup.GetComponentDataArray<CM_VcamRotation>()
            };
            return job.Schedule(m_mainGroup.CalculateLength(), 32, inputDeps);
        }
    }
}