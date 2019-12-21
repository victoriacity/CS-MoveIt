﻿using UnityEngine;
using System;
using System.Collections.Generic;
using System.Xml.Serialization;

using ColossalFramework.Math;


namespace MoveIt
{
    public class NodeState : InstanceState
    {
        public NetNode.Flags flags;
        public BuildingState pillarState;

        [XmlElement("segmentsSave")]
        public SegmentSave[] segmentsSave = new SegmentSave[8];

        public struct SegmentSave
        {
            public Vector3 startDirection;
            public Vector3 endDirection;
        }

        public override void ReplaceInstance(Instance instance)
        {
            base.ReplaceInstance(instance);

            MoveableNode node = instance as MoveableNode;

            if (pillarState != null)
            {
                pillarState.instance = node.Pillar;
                if(pillarState.instance == null)
                {
                    pillarState = null;
                }
            }
        }
    }

    public class MoveableNode : Instance
    {
        public MoveableBuilding Pillar // Pillar, pylon, water junction
        {
            get
            {
                if(nodeBuffer[id.NetNode].m_building != 0)
                {
                    InstanceID pillarID = new InstanceID();
                    pillarID.Building = nodeBuffer[id.NetNode].m_building;


                    if ((BuildingManager.instance.m_buildings.m_buffer[pillarID.Building].m_flags & Building.Flags.Created) != Building.Flags.None)
                    {
                        MoveableBuilding pillarInstance = new MoveableBuilding(pillarID);

                        if (pillarInstance.isValid)
                        {
                            return pillarInstance;
                        }
                    }
                }
                return null;
            }
        }

        public override HashSet<ushort> segmentList
        {
            get
            {
                HashSet<ushort> segments = new HashSet<ushort>();

                for (int i = 0; i < 8; i++)
                {
                    ushort segment = nodeBuffer[id.NetNode].GetSegment(i);
                    if (segment != 0)
                    {
                        segments.Add(segment);
                    }
                }

                return segments;
            }
        }

        public MoveableNode(InstanceID instanceID) : base(instanceID)
        {
            //if ((NetManager.instance.m_nodes.m_buffer[instanceID.NetNode].m_flags & NetNode.Flags.Created) == NetNode.Flags.None)
            //{
            //    Debug.Log($"Node #{instanceID.NetNode} not found!");
            //    return;
            //    // TODO throw new Exception($"Node #{instanceID.NetNode} not found!");
            //}
            Info = new Info_Prefab(NetManager.instance.m_nodes.m_buffer[instanceID.NetNode].Info);
        }

        public override InstanceState GetState()
        {
            ushort node = id.NetNode;

            NodeState state = new NodeState();

            state.instance = this;
            state.Info = Info;

            state.position = nodeBuffer[node].m_position;
            state.terrainHeight = TerrainManager.instance.SampleOriginalRawHeightSmooth(state.position);

            state.flags = nodeBuffer[node].m_flags;

            MoveableBuilding pillarInstance = Pillar;

            if (Pillar != null)
            {
                state.pillarState = Pillar.GetState() as BuildingState;
            }

            for (int i = 0; i < 8; i++)
            {
                ushort segment = nodeBuffer[node].GetSegment(i);
                if (segment != 0)
                {
                    state.segmentsSave[i].startDirection = segmentBuffer[segment].m_startDirection;
                    state.segmentsSave[i].endDirection = segmentBuffer[segment].m_endDirection;
                }
            }

            return state;
        }

        public override void SetState(InstanceState state)
        {
            if (!(state is NodeState nodeState)) return;

            ushort node = id.NetNode;

            netManager.MoveNode(node, nodeState.position);

            for (int i = 0; i < 8; i++)
            {
                ushort segment = nodeBuffer[node].GetSegment(i);
                if (segment != 0)
                {
                    segmentBuffer[segment].m_startDirection = nodeState.segmentsSave[i].startDirection;
                    segmentBuffer[segment].m_endDirection = nodeState.segmentsSave[i].endDirection;

                    UpdateSegmentBlocks(segment, ref segmentBuffer[segment]);

                    netManager.UpdateNode(segmentBuffer[segment].m_startNode);
                    netManager.UpdateNode(segmentBuffer[segment].m_endNode);
                }
            }

            if (nodeState.pillarState != null)
            {
                nodeState.pillarState.instance.SetState(nodeState.pillarState);
            }
        }

        public override Vector3 position
        {
            get
            {
                if (id.IsEmpty) return Vector3.zero;
                return nodeBuffer[id.NetNode].m_position;
            }
            set
            {
                if (id.IsEmpty) nodeBuffer[id.NetNode].m_position = Vector3.zero;
                else nodeBuffer[id.NetNode].m_position = value;
            }
        }

        public override float angle
        {
            get { return 0f; }
            set { }
        }

        public override bool isValid
        {
            get
            {
                if (id.IsEmpty) return false;
                return (nodeBuffer[id.NetNode].m_flags & NetNode.Flags.Created) != NetNode.Flags.None;
            }
        }

        public override void Transform(InstanceState instanceState, ref Matrix4x4 matrix4x, float deltaHeight, float deltaAngle, Vector3 center, bool followTerrain)
        {
            NodeState state = instanceState as NodeState;

            Vector3 newPosition = matrix4x.MultiplyPoint(state.position - center);
            newPosition.y = state.position.y + deltaHeight;

            if (followTerrain)
            {
                newPosition.y = newPosition.y + TerrainManager.instance.SampleOriginalRawHeightSmooth(newPosition) - state.terrainHeight;
            }

            Move(newPosition, 0);

            if (state.pillarState != null)
            {
                Vector3 subPosition = state.pillarState.position - center;
                subPosition = matrix4x.MultiplyPoint(subPosition);
                subPosition.y = state.pillarState.position.y - state.position.y + newPosition.y;

                state.pillarState.instance.Move(subPosition, state.pillarState.angle + deltaAngle);
            }
        }

        public override void Move(Vector3 location, float angle)
        {
            if (!isValid) return;

            ushort node = id.NetNode;
            Vector3 oldPosition = nodeBuffer[node].m_position;

            netManager.MoveNode(node, location);

            for (int i = 0; i < 8; i++)
            {
                ushort segment = nodeBuffer[node].GetSegment(i);
                if (segment != 0 && !Action.IsSegmentSelected(segment)) // TODO: Is IsSegmentSelected sane?
                {
                    ushort startNode = segmentBuffer[segment].m_startNode;
                    ushort endNode = segmentBuffer[segment].m_endNode;

                    Vector3 oldVector;
                    if(node == endNode)
                    {
                        oldVector = oldPosition - nodeBuffer[startNode].m_position;
                    }
                    else
                    {
                        oldVector = nodeBuffer[endNode].m_position - oldPosition;
                    }
                    oldVector.Normalize();
                    
                    Vector3 startDirection = new Vector3(segmentBuffer[segment].m_startDirection.x, 0, segmentBuffer[segment].m_startDirection.z);
                    Vector3 endDirection = new Vector3(segmentBuffer[segment].m_endDirection.x, 0, segmentBuffer[segment].m_endDirection.z);

                    Quaternion startRotation = Quaternion.FromToRotation(oldVector, startDirection.normalized);
                    Quaternion endRotation = Quaternion.FromToRotation(-oldVector, endDirection.normalized);

                    Vector3 newVector = nodeBuffer[endNode].m_position - nodeBuffer[startNode].m_position;
                    newVector.Normalize();

                    segmentBuffer[segment].m_startDirection = startRotation * newVector;
                    segmentBuffer[segment].m_endDirection = endRotation * -newVector;

                    CalculateSegmentDirections(ref segmentBuffer[segment], segment);

                    netManager.UpdateSegmentRenderer(segment, true);
                    UpdateSegmentBlocks(segment, ref segmentBuffer[segment]);

                    if (node != startNode)
                    {
                        netManager.UpdateNode(startNode);
                    }
                    else
                    {
                        netManager.UpdateNode(endNode);
                    }
                }
            }

            netManager.UpdateNode(node);
        }

        public void AutoCurve(NetSegment segmentCurve)
        {
            ushort node = id.NetNode;

            if (segmentCurve.m_startNode != 0 && segmentCurve.m_endNode != 0)
            {
                segmentCurve.GetClosestPositionAndDirection(position, out Vector3 p, out Vector3 tangent);

                for (int i = 0; i < 8; i++)
                {
                    // Start node
                    ushort segment = nodeBuffer[segmentCurve.m_startNode].GetSegment(i);

                    if (segment != 0)
                    {
                        ushort startNode = segmentBuffer[segment].m_startNode;
                        ushort endNode = segmentBuffer[segment].m_endNode;

                        if (startNode == node)
                        {
                            segmentBuffer[segment].m_startDirection = -tangent;
                            segmentBuffer[segment].m_endDirection = segmentCurve.m_startDirection;

                            CalculateSegmentDirections(ref segmentBuffer[segment], segment);
                            netManager.UpdateSegmentRenderer(segment, true);
                            UpdateSegmentBlocks(segment, ref segmentBuffer[segment]);

                            netManager.UpdateNode(endNode);
                        }
                        else if (endNode == node)
                        {
                            segmentBuffer[segment].m_startDirection = segmentCurve.m_startDirection;
                            segmentBuffer[segment].m_endDirection = -tangent;

                            CalculateSegmentDirections(ref segmentBuffer[segment], segment);
                            netManager.UpdateSegmentRenderer(segment, true);
                            UpdateSegmentBlocks(segment, ref segmentBuffer[segment]);

                            netManager.UpdateNode(startNode);
                        }
                    }

                    // End node
                    segment = nodeBuffer[segmentCurve.m_endNode].GetSegment(i);

                    if (segment != 0)
                    {
                        ushort startNode = segmentBuffer[segment].m_startNode;
                        ushort endNode = segmentBuffer[segment].m_endNode;

                        if (startNode == node)
                        {
                            segmentBuffer[segment].m_startDirection = tangent;
                            segmentBuffer[segment].m_endDirection = segmentCurve.m_endDirection;

                            CalculateSegmentDirections(ref segmentBuffer[segment], segment);
                            netManager.UpdateSegmentRenderer(segment, true);
                            UpdateSegmentBlocks(segment, ref segmentBuffer[segment]);

                            netManager.UpdateNode(endNode);
                        }
                        else if (endNode == node)
                        {
                            segmentBuffer[segment].m_startDirection = segmentCurve.m_endDirection;
                            segmentBuffer[segment].m_endDirection = tangent;

                            CalculateSegmentDirections(ref segmentBuffer[segment], segment);
                            netManager.UpdateSegmentRenderer(segment, true);
                            UpdateSegmentBlocks(segment, ref segmentBuffer[segment]);

                            netManager.UpdateNode(startNode);
                        }
                    }
                }
            }

            netManager.UpdateNode(node);
        }

        public override void SetHeight(float height)
        {
            Vector3 newPosition = position;

            MoveableBuilding nodePillar = Pillar;
            if (nodePillar != null)
            {
                Vector3 subPosition = nodePillar.position;
                subPosition.y = subPosition.y - newPosition.y + height;

                nodePillar.Move(subPosition, nodePillar.angle);
            }

            newPosition.y = height;
            Move(newPosition, angle);
        }

        public override Instance Clone(InstanceState instanceState, ref Matrix4x4 matrix4x, float deltaHeight, float deltaAngle, Vector3 center, bool followTerrain, Dictionary<ushort, ushort> clonedNodes, Action action)
        {
            NodeState state = instanceState as NodeState;

            Vector3 newPosition = matrix4x.MultiplyPoint(state.position - center);
            newPosition.y = state.position.y + deltaHeight;

            if (followTerrain)
            {
                newPosition.y = newPosition.y + TerrainManager.instance.SampleOriginalRawHeightSmooth(newPosition) - state.terrainHeight;
            }

            Instance cloneInstance = null;

            if (NetManager.instance.CreateNode(out ushort clone, ref SimulationManager.instance.m_randomizer, state.Info.Prefab as NetInfo,
                newPosition, SimulationManager.instance.m_currentBuildIndex))
            {
                SimulationManager.instance.m_currentBuildIndex++;

                InstanceID cloneID = default;
                cloneID.NetNode = clone;
                cloneInstance = new MoveableNode(cloneID);

                nodeBuffer[clone].m_flags = state.flags;

                // TODO: Clone pillar instead?
                nodeBuffer[clone].Info.m_netAI.GetNodeBuilding(clone, ref nodeBuffer[clone], out BuildingInfo newBuilding, out float heightOffset);
                nodeBuffer[clone].UpdateBuilding(clone, newBuilding, heightOffset);
            }

            return cloneInstance;
        }

        public override Instance Clone(InstanceState instanceState, Dictionary<ushort, ushort> clonedNodes)
        {
            NodeState state = instanceState as NodeState;

            MoveableNode cloneInstance = null;

            if (NetManager.instance.CreateNode(out ushort clone, ref SimulationManager.instance.m_randomizer, state.Info.Prefab as NetInfo,
                state.position, SimulationManager.instance.m_currentBuildIndex))
            {
                SimulationManager.instance.m_currentBuildIndex++;

                InstanceID cloneID = default;
                cloneID.NetNode = clone;
                cloneInstance = new MoveableNode(cloneID);

                nodeBuffer[clone].m_flags = state.flags;

                // TODO: Clone pillar instead?
                nodeBuffer[clone].Info.m_netAI.GetNodeBuilding(clone, ref nodeBuffer[clone], out BuildingInfo newBuilding, out float heightOffset);
                nodeBuffer[clone].UpdateBuilding(clone, newBuilding, heightOffset);
            }

            return cloneInstance;
        }

        public override void Delete()
        {
            if (isValid) NetManager.instance.ReleaseNode(id.NetNode);
        }

        public override Bounds GetBounds(bool ignoreSegments = true)
        {
            ushort node = id.NetNode;

            Bounds bounds = SanitizeBounds(node);

            if (nodeBuffer[node].Info.m_netAI is WaterPipeAI)
            {
                ignoreSegments = true;
            }

            if (!ignoreSegments)
            {
                for (int i = 0; i < 8; i++)
                {
                    ushort segment = nodeBuffer[node].GetSegment(i);
                    if (segment != 0)
                    {
                        ushort startNode = segmentBuffer[segment].m_startNode;
                        ushort endNode = segmentBuffer[segment].m_endNode;

                        if (node != startNode)
                        {
                            bounds.Encapsulate(SanitizeBounds(startNode));
                        }
                        else
                        {
                            bounds.Encapsulate(SanitizeBounds(endNode));
                        }
                    }
                }
            }

            return bounds;
        }

        private static Bounds SanitizeBounds(ushort id)
        {
            NetNode node = nodeBuffer[id];
            Bounds bounds = node.m_bounds;
            Vector3 AbsCenter = new Vector3(Math.Abs(bounds.center.x), Math.Abs(bounds.center.y), Math.Abs(bounds.center.z));

            if (AbsCenter == bounds.extents || bounds.center == Vector3.zero)
            {
                node.m_bounds = new Bounds(node.m_position, new Vector3(16f, 0f, 16f));
            }

            return node.m_bounds;
        }

        public override void RenderOverlay(RenderManager.CameraInfo cameraInfo, Color toolColor, Color despawnColor)
        {
            if (!isValid) return;

            ushort node = id.NetNode;
            NetManager netManager = NetManager.instance;
            NetInfo netInfo = nodeBuffer[node].Info;
            Vector3 position = nodeBuffer[node].m_position;
            // NON-STOCK CODE STARTS
            if (MoveItTool.IsCSUROffset(netInfo))
            {
                var tmpDir = MoveItTool.GetNodeDir(node);
                tmpDir = new Vector3(tmpDir.z, tmpDir.y, -tmpDir.x);
                position = tmpDir * (netInfo.m_halfWidth + netInfo.m_pavementWidth) / 2f + position;
            }
            // NON-STOCK CODE ENDS
            Randomizer randomizer = new Randomizer(node);
            float alpha = 1f;
            NetTool.CheckOverlayAlpha(netInfo, ref alpha);
            toolColor.a *= alpha;
            // NON-STOCK CODE STARTS
            if (MoveItTool.IsCSUROffset(netInfo))
            {
                RenderManager.instance.OverlayEffect.DrawCircle(cameraInfo, toolColor, position, Mathf.Max(6f, netInfo.m_halfWidth - netInfo.m_pavementWidth), -1f, 1280f, false, true);
            }
            else
            {
                RenderManager.instance.OverlayEffect.DrawCircle(cameraInfo, toolColor, position, Mathf.Max(6f, netInfo.m_halfWidth * 2f), -1f, 1280f, false, true);
            }
            // NON-STOCK CODE ENDS
        }

        public override void RenderCloneOverlay(InstanceState state, ref Matrix4x4 matrix4x, Vector3 deltaPosition, float deltaAngle, Vector3 center, bool followTerrain, RenderManager.CameraInfo cameraInfo, Color toolColor) { }

        public override void RenderCloneGeometry(InstanceState state, ref Matrix4x4 matrix4x, Vector3 deltaPosition, float deltaAngle, Vector3 center, bool followTerrain, RenderManager.CameraInfo cameraInfo, Color toolColor) { }
    }
}
