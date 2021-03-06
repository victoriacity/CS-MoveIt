using System;
using ColossalFramework;
using System.Collections.Generic;
using UnityEngine;

namespace MoveIt
{
    class AlignSlopeAction : Action
    {
        protected static Building[] buildingBuffer = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
        protected static PropInstance[] propBuffer = Singleton<PropManager>.instance.m_props.m_buffer;
        protected static TreeInstance[] treeBuffer = Singleton<TreeManager>.instance.m_trees.m_buffer;
        protected static NetSegment[] segmentBuffer = Singleton<NetManager>.instance.m_segments.m_buffer;
        protected static NetNode[] nodeBuffer = Singleton<NetManager>.instance.m_nodes.m_buffer;

        public bool IsQuick = false;

        public HashSet<InstanceState> m_states = new HashSet<InstanceState>();

        private Instance[] keyInstance = new Instance[2];

        public Instance PointA
        {
            get
            {
                return keyInstance[0];
            }
            set
            {
                keyInstance[0] = value;
            }
        }
        public Instance PointB
        {
            get
            {
                return keyInstance[1];
            }
            set
            {
                keyInstance[1] = value;
            }
        }

        public bool followTerrain;

        public AlignSlopeAction()
        {
            foreach (Instance instance in selection)
            {
                if (instance.isValid)
                {
                    m_states.Add(instance.GetState());
                }
            }
        }

        public override void Do()
        {
            float angleDelta;
            float heightDelta;
            float distance;
            Matrix4x4 matrix = default;

            if (IsQuick)
            {
                if (selection.Count != 1) return;
                foreach (Instance instance in selection) // Is this really the best way to get the value of selection[0]?
                {
                    if (!instance.isValid || !(instance is MoveableNode nodeInstance)) return;

                    NetNode node = nodeBuffer[nodeInstance.id.NetNode];

                    int c = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        ushort segId = 0;
                        if ((segId = node.GetSegment(i)) > 0)
                        {
                            if (c > 1) return; // More than 2 segments found

                            NetSegment segment = segmentBuffer[segId];
                            InstanceID instanceID = default;
                            if (segment.m_startNode == nodeInstance.id.NetNode)
                            {
                                instanceID.NetNode = segment.m_endNode;
                            }
                            else
                            {
                                instanceID.NetNode = segment.m_startNode;
                            }
                            keyInstance[c] = new MoveableNode(instanceID);
                            c++;
                        }
                    }
                    if (c != 2) return;
                }
            }

            angleDelta = 0 - (float)Math.Atan2(PointB.position.z - PointA.position.z, PointB.position.x - PointA.position.x);
            heightDelta = PointB.position.y - PointA.position.y;
            distance = (float)Math.Sqrt(Math.Pow(PointB.position.z - PointA.position.z, 2) + Math.Pow(PointB.position.x - PointA.position.x, 2));

            foreach (InstanceState state in m_states)
            {
                float distanceOffset, heightOffset;
                matrix.SetTRS(PointA.position, Quaternion.AngleAxis(angleDelta * Mathf.Rad2Deg, Vector3.down), Vector3.one);
                distanceOffset = (matrix.MultiplyPoint(state.position - PointA.position) - PointA.position).x;
                heightOffset = distanceOffset / distance * heightDelta;

                state.instance.SetHeight(Mathf.Clamp(PointA.position.y + heightOffset, 0f, 1000f));
            }
        }

        public override void Undo()
        {
            foreach (InstanceState state in m_states)
            {
                state.instance.SetState(state);
            }

            UpdateArea(GetTotalBounds(false));
        }

        public override void ReplaceInstances(Dictionary<Instance, Instance> toReplace)
        {
            foreach (InstanceState state in m_states)
            {
                if (toReplace.ContainsKey(state.instance))
                {
                    DebugUtils.Log("AlignSlopeAction Replacing: " + state.instance.id.RawData + " -> " + toReplace[state.instance].id.RawData);
                    state.ReplaceInstance(toReplace[state.instance]);
                }
            }
        }
    }
}
