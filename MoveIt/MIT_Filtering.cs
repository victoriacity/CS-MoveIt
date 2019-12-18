using ColossalFramework;
using ColossalFramework.Math;
using System;
using System.Collections.Generic;
using UnityEngine;
using System.Text.RegularExpressions;

namespace MoveIt
{
    public partial class MoveItTool : ToolBase
    {
        static bool _stepProcessed = false;

        private void RaycastHoverInstance(Ray mouseRay)
        {
            Vector3 origin = mouseRay.origin;
            Vector3 normalized = mouseRay.direction.normalized;
            Vector3 vector = mouseRay.origin + normalized * Camera.main.farClipPlane;
            Segment3 ray = new Segment3(origin, vector);

            Building[] buildingBuffer = BuildingManager.instance.m_buildings.m_buffer;
            PropInstance[] propBuffer = PropManager.instance.m_props.m_buffer;
            NetNode[] nodeBuffer = NetManager.instance.m_nodes.m_buffer;
            NetSegment[] segmentBuffer = NetManager.instance.m_segments.m_buffer;
            TreeInstance[] treeBuffer = TreeManager.instance.m_trees.m_buffer;

            Vector3 location = RaycastMouseLocation(mouseRay);

            InstanceID id = InstanceID.Empty;

            ItemClass.Layer itemLayers = GetItemLayers();

            bool selectPicker = false;
            bool selectBuilding = true;
            bool selectProps = true;
            bool selectDecals = true;
            bool selectSurfaces = true;
            bool selectNodes = true;
            bool selectSegments = true;
            bool selectTrees = true;
            bool selectProc = PO.Active;

            bool repeatSearch = false;

            if (marqueeSelection)
            {
                selectPicker = filterPicker;
                selectBuilding = filterBuildings;
                selectProps = filterProps;
                selectDecals = filterDecals;
                selectSurfaces = filterSurfaces;
                selectNodes = filterNodes;
                selectSegments = filterSegments;
                selectTrees = filterTrees;
                selectProc = PO.Active ? filterProcs : false;
            }

            if (AlignMode == AlignModes.Group || AlignMode == AlignModes.Inplace)
            {
                selectNodes = false;
                selectTrees = false;
            }
            else if (AlignMode == AlignModes.Mirror)
            {
                selectBuilding = false;
                selectProps = false;
                selectDecals = false;
                selectSurfaces = false;
                selectProc = false;
                selectTrees = false;
                selectNodes = false;
            }

            float smallestDist = 640000f;

            do
            {
                if (PO.Active && selectProc)
                {
                    foreach (IPO_Object obj in PO.Objects)
                    {
                        if (stepOver.isValidPO(obj.Id))
                        {
                            bool inXBounds = obj.Position.x > (location.x - 4f) && obj.Position.x < (location.x + 4f);
                            bool inZBounds = obj.Position.z > (location.z - 4f) && obj.Position.z < (location.z + 4f);
                            if (inXBounds && inZBounds)
                            {
                                float t = obj.GetDistance(location);
                                if (t < smallestDist)
                                {
                                    id.NetLane = obj.Id;
                                    smallestDist = t;
                                }
                            }
                        }
                    }
                }

                int gridMinX = Mathf.Max((int)((location.x - 16f) / 64f + 135f) - 1, 0);
                int gridMinZ = Mathf.Max((int)((location.z - 16f) / 64f + 135f) - 1, 0);
                int gridMaxX = Mathf.Min((int)((location.x + 16f) / 64f + 135f) + 1, 269);
                int gridMaxZ = Mathf.Min((int)((location.z + 16f) / 64f + 135f) + 1, 269);

                for (int i = gridMinZ; i <= gridMaxZ; i++)
                {
                    for (int j = gridMinX; j <= gridMaxX; j++)
                    {
                        if (selectBuilding || selectSurfaces || (selectPicker && Filters.Picker.IsBuilding))
                        {
                            ushort building = BuildingManager.instance.m_buildingGrid[i * 270 + j];
                            int count = 0;
                            while (building != 0u)
                            {
                                if (stepOver.isValidB(building) && IsBuildingValid(ref buildingBuffer[building], itemLayers) && buildingBuffer[building].RayCast(building, ray, out float t) && t < smallestDist)
                                {
                                    if (Filters.Filter(buildingBuffer[building].Info, true))
                                    {
                                        id.Building = Building.FindParentBuilding(building);
                                        if (id.Building == 0) id.Building = building;
                                        smallestDist = t;
                                    }
                                }
                                building = buildingBuffer[building].m_nextGridBuilding;

                                if (++count > 49152)
                                {
                                    CODebugBase<LogChannel>.Error(LogChannel.Core, "Buildings: Invalid list detected!\n" + Environment.StackTrace);
                                    break;
                                }
                            }
                        }

                        if (selectProps || selectDecals || selectSurfaces || (selectPicker && Filters.Picker.IsProp))
                        {
                            ushort prop = PropManager.instance.m_propGrid[i * 270 + j];
                            int count = 0;
                            while (prop != 0u)
                            {
                                if (stepOver.isValidP(prop) && Filters.Filter(propBuffer[prop].Info))
                                {
                                    if (propBuffer[prop].RayCast(prop, ray, out float t, out float targetSqr) && t < smallestDist)
                                    {
                                        id.Prop = prop;
                                        smallestDist = t;
                                    }
                                }

                                prop = propBuffer[prop].m_nextGridProp;

                                if (++count > 65536)
                                {
                                    CODebugBase<LogChannel>.Error(LogChannel.Core, "Props: Invalid list detected!\n" + Environment.StackTrace);
                                }
                            }
                        }

                        if (selectNodes || selectBuilding || (selectPicker && Filters.Picker.IsNode))
                        {
                            ushort node = NetManager.instance.m_nodeGrid[i * 270 + j];
                            int count = 0;
                            while (node != 0u)
                            {
                                if (stepOver.isValidN(node) && IsNodeValid(ref nodeBuffer[node], itemLayers) && RayCastNode(node, ref nodeBuffer[node], ray, -1000f, out float t, out float priority) && t < smallestDist)
                                {
                                    ushort building = 0;
                                    if (!Event.current.alt)
                                    {
                                        building = NetNode.FindOwnerBuilding(node, 363f);
                                    }

                                    if (building != 0)
                                    {
                                        if (selectBuilding)
                                        {
                                            id.Building = Building.FindParentBuilding(building);
                                            if (id.Building == 0) id.Building = building;
                                            smallestDist = t;
                                        }
                                    }
                                    else if (selectNodes || (selectPicker && Filters.Picker.IsNode))
                                    {
                                        if (Filters.Filter(nodeBuffer[node]))
                                        {
                                            id.NetNode = node;
                                            smallestDist = t;
                                        }
                                    }
                                }
                                node = nodeBuffer[node].m_nextGridNode;

                                if (++count > 32768)
                                {
                                    CODebugBase<LogChannel>.Error(LogChannel.Core, "Nodes: Invalid list detected!\n" + Environment.StackTrace);
                                }
                            }
                        }

                        if (selectSegments || selectBuilding || (selectPicker && Filters.Picker.IsSegment))
                        {
                            ushort segment = NetManager.instance.m_segmentGrid[i * 270 + j];
                            int count = 0;
                            while (segment != 0u)
                            {
                                if (stepOver.isValidS(segment) && IsSegmentValid(ref segmentBuffer[segment], itemLayers))
                                {
                                    bool hit;
                                    float t, priority;
                                    if (IsCSUROffset(segmentBuffer[segment].Info.m_netAI.m_info))
                                    {
                                        hit = NetSegmentRayCastMasked(segmentBuffer[segment], segment, ray, -1000f, false, out t, out priority);
                                    } else
                                    {
                                        hit = segmentBuffer[segment].RayCast(segment, ray, -1000f, false, out t, out priority);
                                    }
                                    if (hit && t < smallestDist)
                                    {
                                        ushort building = 0;
                                        if (!Event.current.alt)
                                        {
                                            building = FindOwnerBuilding(segment, 363f);
                                        }

                                        if (building != 0)
                                        {
                                            if (selectBuilding)
                                            {
                                                id.Building = Building.FindParentBuilding(building);
                                                if (id.Building == 0) id.Building = building;
                                                smallestDist = t;
                                            }
                                        }
                                        else if (selectSegments || (selectPicker && Filters.Picker.IsSegment))
                                        {
                                            if (!selectNodes || (
                                                (!stepOver.isValidN(segmentBuffer[segment].m_startNode) || !RayCastNode(segmentBuffer[segment].m_startNode, ref nodeBuffer[segmentBuffer[segment].m_startNode], ray, -1000f, out float t2, out priority)) &&
                                                (!stepOver.isValidN(segmentBuffer[segment].m_endNode) || !RayCastNode(segmentBuffer[segment].m_endNode, ref nodeBuffer[segmentBuffer[segment].m_endNode], ray, -1000f, out t2, out priority))
                                            ))
                                            {
                                                if (Filters.Filter(segmentBuffer[segment]))
                                                {
                                                    id.NetSegment = segment;
                                                    smallestDist = t;
                                                }
                                            }
                                        }
                                    }
                                }
                                segment = segmentBuffer[segment].m_nextGridSegment;

                                if (++count > 36864)
                                {
                                    CODebugBase<LogChannel>.Error(LogChannel.Core, "Segments: Invalid list detected!\n" + Environment.StackTrace);
                                    segment = 0;
                                }
                            }
                        }
                    }
                }

                if (selectTrees || (selectPicker && Filters.Picker.IsTree))
                {
                    gridMinX = Mathf.Max((int)((location.x - 8f) / 32f + 270f), 0);
                    gridMinZ = Mathf.Max((int)((location.z - 8f) / 32f + 270f), 0);
                    gridMaxX = Mathf.Min((int)((location.x + 8f) / 32f + 270f), 539);
                    gridMaxZ = Mathf.Min((int)((location.z + 8f) / 32f + 270f), 539);

                    for (int i = gridMinZ; i <= gridMaxZ; i++)
                    {
                        for (int j = gridMinX; j <= gridMaxX; j++)
                        {
                            uint tree = TreeManager.instance.m_treeGrid[i * 540 + j];
                            int count = 0;
                            while (tree != 0)
                            {
                                if (stepOver.isValidT(tree) && treeBuffer[tree].RayCast(tree, ray, out float t, out float targetSqr) && t < smallestDist)
                                {
                                    if (Filters.Filter(treeBuffer[tree].Info))
                                    {
                                        id.Tree = tree;
                                        smallestDist = t;
                                    }
                                }
                                tree = treeBuffer[tree].m_nextGridTree;

                                if (++count > 262144)
                                {
                                    CODebugBase<LogChannel>.Error(LogChannel.Core, "Trees: Invalid list detected!\n" + Environment.StackTrace);
                                }
                            }
                        }
                    }
                }

                repeatSearch = false;
                if (OptionsKeymapping.stepOverKey.IsPressed())
                {
                    if (!_stepProcessed)
                    {
                        _stepProcessed = true;
                        repeatSearch = true;
                        stepOver.Add(id);
                    }
                }
                else
                {
                    _stepProcessed = false;
                }
            }
            while (repeatSearch);

            if (m_debugPanel != null) m_debugPanel.UpdatePanel(id);

            m_hoverInstance = id;
        }


        private HashSet<Instance> GetMarqueeList(Ray mouseRay)
        {
            HashSet<Instance> list = new HashSet<Instance>();

            Building[] buildingBuffer = BuildingManager.instance.m_buildings.m_buffer;
            PropInstance[] propBuffer = PropManager.instance.m_props.m_buffer;
            NetNode[] nodeBuffer = NetManager.instance.m_nodes.m_buffer;
            NetSegment[] segmentBuffer = NetManager.instance.m_segments.m_buffer;
            TreeInstance[] treeBuffer = TreeManager.instance.m_trees.m_buffer;

            m_selection.a = m_mouseStartPosition;
            m_selection.c = RaycastMouseLocation(mouseRay);

            if (m_selection.a.x == m_selection.c.x && m_selection.a.z == m_selection.c.z)
            {
                m_selection = default;
            }
            else
            {
                float angle = Camera.main.transform.localEulerAngles.y * Mathf.Deg2Rad;
                Vector3 down = new Vector3(Mathf.Cos(angle), 0, -Mathf.Sin(angle));
                Vector3 right = new Vector3(-down.z, 0, down.x);

                Vector3 a = m_selection.c - m_selection.a;
                float dotDown = Vector3.Dot(a, down);
                float dotRight = Vector3.Dot(a, right);

                if ((dotDown > 0 && dotRight > 0) || (dotDown <= 0 && dotRight <= 0))
                {
                    m_selection.b = m_selection.a + dotDown * down;
                    m_selection.d = m_selection.a + dotRight * right;
                }
                else
                {
                    m_selection.b = m_selection.a + dotRight * right;
                    m_selection.d = m_selection.a + dotDown * down;
                }

                // Disables select-during-drag
                //if (ToolState == ToolStates.DrawingSelection)
                //{
                //    return list;
                //}

                Vector3 min = m_selection.Min();
                Vector3 max = m_selection.Max();

                int gridMinX = Mathf.Max((int)((min.x - 16f) / 64f + 135f), 0);
                int gridMinZ = Mathf.Max((int)((min.z - 16f) / 64f + 135f), 0);
                int gridMaxX = Mathf.Min((int)((max.x + 16f) / 64f + 135f), 269);
                int gridMaxZ = Mathf.Min((int)((max.z + 16f) / 64f + 135f), 269);

                InstanceID id = new InstanceID();
                ItemClass.Layer itemLayers = GetItemLayers();

                if (PO.Active && filterProcs)
                {
                    foreach (IPO_Object obj in PO.Objects)
                    {
                        if (PointInRectangle(m_selection, obj.Position))
                        {
                            id.NetLane = obj.Id;
                            list.Add(id);
                        }
                    }
                }

                for (int i = gridMinZ; i <= gridMaxZ; i++)
                {
                    for (int j = gridMinX; j <= gridMaxX; j++)
                    {
                        if (filterBuildings || filterSurfaces || (filterPicker && Filters.Picker.IsBuilding))
                        {
                            ushort building = BuildingManager.instance.m_buildingGrid[i * 270 + j];
                            int count = 0;
                            while (building != 0u)
                            {
                                if (IsBuildingValid(ref buildingBuffer[building], itemLayers) && PointInRectangle(m_selection, buildingBuffer[building].m_position))
                                {
                                    if (Filters.Filter(buildingBuffer[building].Info))
                                    {
                                        id.Building = Building.FindParentBuilding(building);
                                        if (id.Building == 0) id.Building = building;
                                        list.Add(id);
                                    }
                                }
                                building = buildingBuffer[building].m_nextGridBuilding;

                                if (++count > 49152)
                                {
                                    CODebugBase<LogChannel>.Error(LogChannel.Core, "Buildings: Invalid list detected!\n" + Environment.StackTrace);
                                    break;
                                }
                            }
                        }

                        if (filterProps || filterDecals || filterSurfaces || (filterPicker && Filters.Picker.IsProp))
                        {
                            ushort prop = PropManager.instance.m_propGrid[i * 270 + j];
                            int count = 0;
                            while (prop != 0u)
                            {
                                if (Filters.Filter(propBuffer[prop].Info))
                                {
                                    if (PointInRectangle(m_selection, propBuffer[prop].Position))
                                    {
                                        id.Prop = prop;
                                        list.Add(id);
                                    }
                                }

                                prop = propBuffer[prop].m_nextGridProp;

                                if (++count > 65536)
                                {
                                    CODebugBase<LogChannel>.Error(LogChannel.Core, "Prop: Invalid list detected!\n" + Environment.StackTrace);
                                }
                            }
                        }

                        if (filterNodes || filterBuildings || (filterPicker && Filters.Picker.IsNode))
                        {
                            ushort node = NetManager.instance.m_nodeGrid[i * 270 + j];
                            int count = 0;
                            while (node != 0u)
                            {
                                if (IsNodeValid(ref nodeBuffer[node], itemLayers) && PointInRectangle(m_selection, nodeBuffer[node].m_position))
                                {
                                    ushort building = NetNode.FindOwnerBuilding(node, 363f);

                                    if (building != 0)
                                    {
                                        if (filterBuildings)
                                        {
                                            id.Building = Building.FindParentBuilding(building);
                                            if (id.Building == 0) id.Building = building;
                                            list.Add(id);
                                        }
                                    }
                                    else if (filterNodes || (filterPicker && Filters.Picker.IsNode))
                                    {
                                        if (Filters.Filter(nodeBuffer[node]))
                                        {
                                            id.NetNode = node;
                                            list.Add(id);
                                        }
                                    }
                                }
                                node = nodeBuffer[node].m_nextGridNode;

                                if (++count > 32768)
                                {
                                    CODebugBase<LogChannel>.Error(LogChannel.Core, "Nodes: Invalid list detected!\n" + Environment.StackTrace);
                                }
                            }
                        }

                        if (filterSegments || filterBuildings || (filterPicker && Filters.Picker.IsSegment))
                        {
                            ushort segment = NetManager.instance.m_segmentGrid[i * 270 + j];
                            int count = 0;
                            while (segment != 0u)
                            {
                                if (IsSegmentValid(ref segmentBuffer[segment], itemLayers) && PointInRectangle(m_selection, segmentBuffer[segment].m_bounds.center))
                                {
                                    ushort building = FindOwnerBuilding(segment, 363f);

                                    if (building != 0)
                                    {
                                        if (filterBuildings)
                                        {
                                            id.Building = Building.FindParentBuilding(building);
                                            if (id.Building == 0) id.Building = building;
                                            list.Add(id);
                                        }
                                    }
                                    else if (filterSegments || (filterPicker && Filters.Picker.IsSegment))
                                    {
                                        if (Filters.Filter(segmentBuffer[segment]))
                                        {
                                            id.NetSegment = segment;
                                            list.Add(id);
                                        }
                                    }
                                }
                                segment = segmentBuffer[segment].m_nextGridSegment;

                                if (++count > 36864)
                                {
                                    CODebugBase<LogChannel>.Error(LogChannel.Core, "Segments: Invalid list detected!\n" + Environment.StackTrace);
                                }
                            }
                        }
                    }
                }

                if (filterTrees || (filterPicker && Filters.Picker.IsTree))
                {
                    gridMinX = Mathf.Max((int)((min.x - 8f) / 32f + 270f), 0);
                    gridMinZ = Mathf.Max((int)((min.z - 8f) / 32f + 270f), 0);
                    gridMaxX = Mathf.Min((int)((max.x + 8f) / 32f + 270f), 539);
                    gridMaxZ = Mathf.Min((int)((max.z + 8f) / 32f + 270f), 539);

                    for (int i = gridMinZ; i <= gridMaxZ; i++)
                    {
                        for (int j = gridMinX; j <= gridMaxX; j++)
                        {
                            uint tree = TreeManager.instance.m_treeGrid[i * 540 + j];
                            int count = 0;
                            while (tree != 0)
                            {
                                if (PointInRectangle(m_selection, treeBuffer[tree].Position))
                                {
                                    if (Filters.Filter(treeBuffer[tree].Info))
                                    {
                                        id.Tree = tree;
                                        list.Add(id);
                                    }
                                }
                                tree = treeBuffer[tree].m_nextGridTree;

                                if (++count > 262144)
                                {
                                    CODebugBase<LogChannel>.Error(LogChannel.Core, "Trees: Invalid list detected!\n" + Environment.StackTrace);
                                }
                            }
                        }
                    }
                }
            }

            return list;
        }


        public static ushort FindOwnerBuilding(ushort segment, float maxDistance)
        {
            Building[] buildingBuffer = BuildingManager.instance.m_buildings.m_buffer;
            ushort[] buildingGrid = BuildingManager.instance.m_buildingGrid;
            NetNode[] nodeBuffer = NetManager.instance.m_nodes.m_buffer;
            NetSegment[] segmentBuffer = NetManager.instance.m_segments.m_buffer;

            ushort startNode = segmentBuffer[segment].m_startNode;
            ushort endNode = segmentBuffer[segment].m_endNode;
            Vector3 startPosition = nodeBuffer[startNode].m_position;
            Vector3 endPosition = nodeBuffer[endNode].m_position;
            Vector3 vector = Vector3.Min(startPosition, endPosition);
            Vector3 vector2 = Vector3.Max(startPosition, endPosition);
            int gridMinX = Mathf.Max((int)((vector.x - maxDistance) / 64f + 135f), 0);
            int gridMinZ = Mathf.Max((int)((vector.z - maxDistance) / 64f + 135f), 0);
            int gridMaxX = Mathf.Min((int)((vector2.x + maxDistance) / 64f + 135f), 269);
            int gridMaxZ = Mathf.Min((int)((vector2.z + maxDistance) / 64f + 135f), 269);

            ushort result = 0;
            float maxDistSqr = maxDistance * maxDistance;
            for (int i = gridMinZ; i <= gridMaxZ; i++)
            {
                for (int j = gridMinX; j <= gridMaxX; j++)
                {
                    ushort building = buildingGrid[i * 270 + j];
                    int count = 0;
                    while (building != 0)
                    {
                        Vector3 position2 = buildingBuffer[building].m_position;
                        float num8 = position2.x - startPosition.x;
                        float num9 = position2.z - startPosition.z;
                        float num10 = num8 * num8 + num9 * num9;
                        if (num10 < maxDistSqr && buildingBuffer[building].ContainsNode(startNode) && buildingBuffer[building].ContainsNode(endNode))
                        {
                            return building;
                        }
                        building = buildingBuffer[building].m_nextGridBuilding;
                        if (++count >= 49152)
                        {
                            CODebugBase<LogChannel>.Error(LogChannel.Core, "Buildings: Invalid list detected!\n" + Environment.StackTrace);
                            break;
                        }
                    }
                }
            }
            return result;
        }

        private bool isLeft(Vector3 P0, Vector3 P1, Vector3 P2)
        {
            return ((P1.x - P0.x) * (P2.z - P0.z) - (P2.x - P0.x) * (P1.z - P0.z)) > 0;
        }

        private bool PointInRectangle(Quad3 rectangle, Vector3 p)
        {
            return isLeft(rectangle.a, rectangle.b, p) && isLeft(rectangle.b, rectangle.c, p) && isLeft(rectangle.c, rectangle.d, p) && isLeft(rectangle.d, rectangle.a, p);
        }

        private ItemClass.Layer GetItemLayers()
        {
            ItemClass.Layer itemLayers = ItemClass.Layer.Default;

            if (InfoManager.instance.CurrentMode == InfoManager.InfoMode.Water)
            {
                itemLayers |= ItemClass.Layer.WaterPipes;
            }
            else if (InfoManager.instance.CurrentMode == InfoManager.InfoMode.Traffic || InfoManager.instance.CurrentMode == InfoManager.InfoMode.Transport)
            {
                itemLayers |= ItemClass.Layer.MetroTunnels;
            }
            else if (InfoManager.instance.CurrentMode == InfoManager.InfoMode.Underground)
            {
                itemLayers = ItemClass.Layer.MetroTunnels; // Removes Default assignment
            }
            //else if (InfoManager.instance.CurrentMode == InfoManager.InfoMode.Transport)
            //{
            //    itemLayers |= ItemClass.Layer.ShipPaths | ItemClass.Layer.AirplanePaths | ItemClass.Layer.BlimpPaths;
            //}
            else
            {
                itemLayers |= ItemClass.Layer.Markers;
            }

            return itemLayers;
        }

        //private bool IsDecal(PropInfo prop)
        //{
        //    if (prop != null && prop.m_material != null)
        //    {
        //        return (prop.m_material.shader == shaderBlend || prop.m_material.shader == shaderSolid);
        //    }

        //    return false;
        //}

        private bool IsBuildingValid(ref Building building, ItemClass.Layer itemLayers)
        {
            if ((building.m_flags & Building.Flags.Created) == Building.Flags.Created)
            {
                return (building.Info.m_class.m_layer & itemLayers) != ItemClass.Layer.None;
            }

            return false;
        }

        private bool IsNodeValid(ref NetNode node, ItemClass.Layer itemLayers)
        {
            if ((node.m_flags & NetNode.Flags.Created) == NetNode.Flags.Created)
            {
                return (node.Info.GetConnectionClass().m_layer & itemLayers) != ItemClass.Layer.None;
            }

            return false;
        }

        private bool IsSegmentValid(ref NetSegment segment, ItemClass.Layer itemLayers)
        {
            if ((segment.m_flags & NetSegment.Flags.Created) == NetSegment.Flags.Created)
            {
                return (segment.Info.GetConnectionClass().m_layer & itemLayers) != ItemClass.Layer.None;
            }

            return false;
        }

        private static bool RayCastNode(ushort nodeid, ref NetNode node, Segment3 ray, float snapElevation, out float t, out float priority)
        {
            NetInfo info = node.Info;
            // NON-STOCK CODE STARTS
            if (IsCSUROffset(info.m_netAI.m_info))
            {
                return RayCastNodeMasked(nodeid, ref node, ray, snapElevation, false, out t, out priority);
            }
            // NON-STOCK CODE ENDS
            float num = (float)node.m_elevation + info.m_netAI.GetSnapElevation();
            float t2;
            if (info.m_netAI.IsUnderground())
            {
                t2 = Mathf.Clamp01(Mathf.Abs(snapElevation + num) / 12f);
            }
            else
            {
                t2 = Mathf.Clamp01(Mathf.Abs(snapElevation - num) / 12f);
            }
            float collisionHalfWidth = Mathf.Max(3f, info.m_netAI.GetCollisionHalfWidth());
            float num2 = Mathf.Lerp(info.GetMinNodeDistance(), collisionHalfWidth, t2);
            if (Segment1.Intersect(ray.a.y, ray.b.y, node.m_position.y, out t))
            {
                float num3 = Vector3.Distance(ray.Position(t), node.m_position);
                if (num3 < num2)
                {
                    priority = Mathf.Max(0f, num3 - collisionHalfWidth);
                    return true;
                }
            }
            t = 0f;
            priority = 0f;
            return false;
        }

        // NON-STOCK CODE STARTS

        private const string CSUR_REGEX = "CSUR(-(T|R|S))? ([[1-9]?[0-9]D?(L|S|C|R)[1-9]*P?)+(=|-)?([[1-9]?[0-9]D?(L|S|C|R)[1-9]*P?)*";
        private const string CSUR_OFFSET_REGEX = "CSUR(-(T|R|S))? ([[1-9]?[0-9](L|R)[1-9]*P?)+(=|-)?([[1-9]?[0-9](L|R)[1-9]*P?)*";

        public static bool IsCSUR(NetInfo asset)
        {
            if (asset == null || asset.m_netAI.GetType() != typeof(RoadAI))
            {
                return false;
            }
            string savenameStripped = asset.name.Substring(asset.name.IndexOf('.') + 1);
            Match m = Regex.Match(savenameStripped, CSUR_REGEX, RegexOptions.IgnoreCase);
            return m.Success;
        }

        public static bool IsCSUROffset(NetInfo asset)
        {
            if (asset == null || asset.m_netAI.GetType() != typeof(RoadAI))
            {
                return false;
            }
            string savenameStripped = asset.name.Substring(asset.name.IndexOf('.') + 1);
            Match m = Regex.Match(savenameStripped, CSUR_OFFSET_REGEX, RegexOptions.IgnoreCase);
            return m.Success;
        }

        public static Vector3 GetNodeDir(ushort node)
        {
            var nm = Singleton<NetManager>.instance;
            var m_node = nm.m_nodes.m_buffer[node];
            for (int i = 0; i < 8; i++)
            {
                if (m_node.GetSegment(i) != 0)
                {
                    if (Singleton<NetManager>.instance.m_segments.m_buffer[m_node.GetSegment(i)].m_startNode == node)
                    {
                        if (Singleton<NetManager>.instance.m_segments.m_buffer[m_node.GetSegment(i)].m_flags.IsFlagSet(NetSegment.Flags.Invert))
                        {
                            return -Singleton<NetManager>.instance.m_segments.m_buffer[m_node.GetSegment(i)].m_startDirection;
                        }
                        else
                        {
                            return Singleton<NetManager>.instance.m_segments.m_buffer[m_node.GetSegment(i)].m_startDirection;
                        }
                    }
                    else if (Singleton<NetManager>.instance.m_segments.m_buffer[m_node.GetSegment(i)].m_endNode == node)
                    {
                        if (Singleton<NetManager>.instance.m_segments.m_buffer[m_node.GetSegment(i)].m_flags.IsFlagSet(NetSegment.Flags.Invert))
                        {
                            return Singleton<NetManager>.instance.m_segments.m_buffer[m_node.GetSegment(i)].m_endDirection;
                        }
                        else
                        {
                            return -Singleton<NetManager>.instance.m_segments.m_buffer[m_node.GetSegment(i)].m_endDirection;
                        }
                    }
                }
            }

            return Vector3.zero;
        }

        private static bool RayCastNodeMasked(ushort nodeid, ref NetNode node, Segment3 ray, float snapElevation, bool bothSides, out float t, out float priority)
        {
            bool lht = false;
            //if (SimulationManager.instance.m_metaData.m_invertTraffic == SimulationMetaData.MetaBool.True) lht = true;
            NetInfo info = node.Info;
            float num = (float)node.m_elevation + info.m_netAI.GetSnapElevation();
            float t2;
            if (info.m_netAI.IsUnderground())
            {
                t2 = Mathf.Clamp01(Mathf.Abs(snapElevation + num) / 12f);
            }
            else
            {
                t2 = Mathf.Clamp01(Mathf.Abs(snapElevation - num) / 12f);
            }
            float collisionHalfWidth = Mathf.Max(3f, info.m_halfWidth);
            float maskHalfWidth = Mathf.Min(collisionHalfWidth - 1.5f, info.m_pavementWidth);
            float num2 = Mathf.Lerp(info.GetMinNodeDistance(), collisionHalfWidth, t2);
            float num2m = Mathf.Lerp(info.GetMinNodeDistance(), maskHalfWidth, t2);
            float num2delta = Mathf.Lerp(info.GetMinNodeDistance(), collisionHalfWidth - maskHalfWidth, t2);
            if (node.CountSegments() <= 2)
            {
                NetManager instance = Singleton<NetManager>.instance;
                NetSegment mysegment = instance.m_segments.m_buffer[node.m_segment0];
                Vector3 direction = mysegment.m_startNode == nodeid ? mysegment.m_startDirection : -mysegment.m_endDirection;
                Debug.Log(direction);
                if ((mysegment.m_flags & NetSegment.Flags.Invert) != 0) lht = true;
                // normal to the right hand side
                Vector3 normal = new Vector3(direction.z, 0, -direction.x).normalized;
                Vector3 trueNodeCenter = node.m_position + (lht ? -collisionHalfWidth : collisionHalfWidth) * normal;
                Debug.Log($"num2: {num2}, num2m: {num2m}");
                Debug.Log($"node: {node.m_position}, center: {trueNodeCenter}");
                if (Segment1.Intersect(ray.a.y, ray.b.y, node.m_position.y, out t))
                {
                    float num3 = Vector3.Distance(ray.Position(t), trueNodeCenter);
                    if (num3 < num2delta)
                    {
                        priority = Mathf.Max(0f, num3 - collisionHalfWidth);
                        return true;
                    }
                }

            } else
            {
                if (Segment1.Intersect(ray.a.y, ray.b.y, node.m_position.y, out t))
                {
                    float num3 = Vector3.Distance(ray.Position(t), node.m_position);
                    if (num3 < num2)
                    {
                        priority = Mathf.Max(0f, num3 - collisionHalfWidth);
                        return true;
                    }
                }
            }
            t = 0f;
            priority = 0f;
            return false;
        }

        public static bool IsTrafficHandSideOf(Segment3 segment, Segment3 ray, float tRay, bool invert)
        {
            Vector3 segmentVector = segment.b - segment.a;
            Vector3 rayVector = ray.Position(tRay) - segment.a;
           // Debug.Log($"startnode->endnode: {segmentVector}, startnode->ray: {rayVector}");
            float crossProduct = rayVector.x * segmentVector.z - segmentVector.x * rayVector.z;
           // Debug.Log($"cross product: {crossProduct}");
            return invert? crossProduct < 0 : crossProduct > 0;
        }

        public static bool NetSegmentRayCastMasked(NetSegment mysegment, ushort segmentID, Segment3 ray, float snapElevation, bool bothSides, out float t, out float priority)
        {
            bool lht = false;
            //if (SimulationManager.instance.m_metaData.m_invertTraffic == SimulationMetaData.MetaBool.True) lht = true;
            Debug.Log(mysegment.m_flags);
            if ((mysegment.m_flags & NetSegment.Flags.Invert) != 0) lht = true;
            bool isMasked = false;
            NetInfo info = mysegment.Info;
            t = 0f;
            priority = 0f;
            Bounds bounds = mysegment.m_bounds;
            bounds.Expand(16f);
            if (!bounds.IntersectRay(new Ray(ray.a, ray.b - ray.a)))
            {
                return false;
            }
            NetManager instance = Singleton<NetManager>.instance;
            Bezier3 bezier = default(Bezier3);
            bezier.a = instance.m_nodes.m_buffer[mysegment.m_startNode].m_position;
            bezier.d = instance.m_nodes.m_buffer[mysegment.m_endNode].m_position;
            bool result = false;

            info.m_netAI.GetRayCastHeights(segmentID, ref mysegment, out float leftMin, out float rightMin, out float max);
            bezier.a.y += max;
            bezier.d.y += max;
            bool flag = (instance.m_nodes.m_buffer[mysegment.m_startNode].m_flags & NetNode.Flags.Middle) != 0;
            bool flag2 = (instance.m_nodes.m_buffer[mysegment.m_endNode].m_flags & NetNode.Flags.Middle) != 0;
            NetSegment.CalculateMiddlePoints(bezier.a, mysegment.m_startDirection, bezier.d, mysegment.m_endDirection, flag, flag2, out bezier.b, out bezier.c);
            float minNodeDistance = info.GetMinNodeDistance();
            // 
            float collisionHalfWidth = info.m_halfWidth;
            float maskHalfWidth = info.m_pavementWidth;
            //
            float num4 = (int)instance.m_nodes.m_buffer[mysegment.m_startNode].m_elevation;
            float num5 = (int)instance.m_nodes.m_buffer[mysegment.m_endNode].m_elevation;
            if (info.m_netAI.IsUnderground())
            {
                num4 = 0f - num4;
                num5 = 0f - num5;
            }
            num4 += info.m_netAI.GetSnapElevation();
            num5 += info.m_netAI.GetSnapElevation();
            float a = Mathf.Lerp(minNodeDistance, collisionHalfWidth, Mathf.Clamp01(Mathf.Abs(snapElevation - num4) / 12f));
            float b2 = Mathf.Lerp(minNodeDistance, collisionHalfWidth, Mathf.Clamp01(Mathf.Abs(snapElevation - num5) / 12f));
            float am = Mathf.Lerp(minNodeDistance, maskHalfWidth, Mathf.Clamp01(Mathf.Abs(snapElevation - num4) / 12f));
            float b2m = Mathf.Lerp(minNodeDistance, maskHalfWidth, Mathf.Clamp01(Mathf.Abs(snapElevation - num5) / 12f));
            float num6 = Mathf.Min(leftMin, rightMin);
            t = 1000000f;
            priority = 1000000f;
            Segment3 segment = default(Segment3);
            segment.a = bezier.a;
            Segment2 segment2 = default(Segment2);
            Debug.Log($"mouse ray: {ray.a} --> {ray.b}");
            Debug.Log($"segment direction: {bezier.a} --> {bezier.b}");
            for (int i = 1; i <= 16; i++)
            {
                segment.b = bezier.Position((float)i / 16f);
                float num7 = ray.DistanceSqr(segment, out float u2, out float v2);
                float num8 = Mathf.Lerp(a, b2, ((float)(i - 1) + v2) / 16f);
                float num8m = Mathf.Lerp(am, b2m, ((float)(i - 1) + v2) / 16f);
                Vector3 vector2 = segment.Position(v2);
                bool atOffsetSide = bothSides || IsTrafficHandSideOf(segment, ray, u2, lht);
                if (atOffsetSide && num7 < priority && Segment1.Intersect(ray.a.y, ray.b.y, vector2.y, out u2))
                {
                    Vector3 vector3 = ray.Position(u2);
                    num7 = Vector3.SqrMagnitude(vector3 - vector2);
                    //Debug.Log($"num7: {num7}, num8: {num8}, num8m: {num8m}");
                    if (num7 < priority && num7 < num8 * num8)
                    {
                           
                        if (flag && i == 1 && v2 < 0.001f)
                        {
                            Vector3 rhs = segment.a - segment.b;
                            u2 += Mathf.Max(0f, Vector3.Dot(vector3, rhs)) / Mathf.Max(0.001f, Mathf.Sqrt(rhs.sqrMagnitude * ray.LengthSqr()));
                        }
                        if (flag2 && i == 16 && v2 > 0.999f)
                        {
                            Vector3 rhs2 = segment.b - segment.a;
                            u2 += Mathf.Max(0f, Vector3.Dot(vector3, rhs2)) / Mathf.Max(0.001f, Mathf.Sqrt(rhs2.sqrMagnitude * ray.LengthSqr()));
                        }
                        priority = num7;
                        t = u2;
                        result = true;
                        if (num7 < num8m * num8m) isMasked = true;
                    }
                }
                if (atOffsetSide && num6 < max)
                {
                    float num9 = vector2.y + num6 - max;
                    if (Mathf.Max(ray.a.y, ray.b.y) > num9 && Mathf.Min(ray.a.y, ray.b.y) < vector2.y)
                    {
                        float num10;
                        if (Segment1.Intersect(ray.a.y, ray.b.y, vector2.y, out u2))
                        {
                            segment2.a = VectorUtils.XZ(ray.Position(u2));
                            num10 = u2;
                        }
                        else
                        {
                            segment2.a = VectorUtils.XZ(ray.a);
                            num10 = 0f;
                        }
                        float num11;
                        if (Segment1.Intersect(ray.a.y, ray.b.y, num9, out u2))
                        {
                            segment2.b = VectorUtils.XZ(ray.Position(u2));
                            num11 = u2;
                        }
                        else
                        {
                            segment2.b = VectorUtils.XZ(ray.b);
                            num11 = 1f;
                        }
                        num7 = segment2.DistanceSqr(VectorUtils.XZ(vector2), out u2);
                        if (num7 < priority && num7 < num8 * num8)
                        {
                            u2 = num10 + (num11 - num10) * u2;
                            Vector3 lhs = ray.Position(u2);
                            if (flag && i == 1 && v2 < 0.001f)
                            {
                                Vector3 rhs3 = segment.a - segment.b;
                                u2 += Mathf.Max(0f, Vector3.Dot(lhs, rhs3)) / Mathf.Max(0.001f, Mathf.Sqrt(rhs3.sqrMagnitude * ray.LengthSqr()));
                            }
                            if (flag2 && i == 16 && v2 > 0.999f)
                            {
                                Vector3 rhs4 = segment.b - segment.a;
                                u2 += Mathf.Max(0f, Vector3.Dot(lhs, rhs4)) / Mathf.Max(0.001f, Mathf.Sqrt(rhs4.sqrMagnitude * ray.LengthSqr()));
                            }
                            priority = num7;
                            t = u2;
                            result = true;
                            if (num7 < num8m * num8m) isMasked = true;
                        }
                    }
                }
                segment.a = segment.b;
            }
            priority = Mathf.Max(0f, Mathf.Sqrt(priority) - collisionHalfWidth);

            if (isMasked) result = false; 
            return result;
        }

        // NON-STOCK CODE ENDS


    }

}
