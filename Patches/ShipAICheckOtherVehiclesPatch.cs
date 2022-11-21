﻿using System;
using ColossalFramework;
using ColossalFramework.Math;
using HarmonyLib;
using UnityEngine;

namespace MovableBridge {
    [HarmonyPatch(typeof(ShipAI), "CheckOtherVehicles")]
    public static class ShipAICheckOtherVehiclesPatch {
        private const float kPassingSpeed = 6f;

        public static void Prefix(ref Vehicle vehicleData, ref Vehicle.Frame frameData, ref float maxSpeed, float maxBraking) {
            VehicleInfo vehicleInfo = vehicleData.Info;
            Vector3 position = frameData.m_position;
            Vector2 xz = VectorUtils.XZ(frameData.m_position);
            float y = position.y;
            Quaternion rotation = frameData.m_rotation;
            Vector3 size = vehicleInfo.m_generatedInfo.m_size;

            float vehicleTopY = y + vehicleInfo.m_generatedInfo.m_size.y - vehicleInfo.m_generatedInfo.m_negativeHeight;
            Vector2 forwardDir = VectorUtils.XZ(rotation * Vector3.forward).normalized;
            Vector2 rightDir = VectorUtils.XZ(rotation * Vector3.right).normalized;
            Quad2 passingQuad = new Quad2 {
                a = xz - 0.5f * size.z * forwardDir - 0.5f * size.x * rightDir,
                b = xz - 0.5f * size.z * forwardDir + 0.5f * size.x * rightDir,
                c = xz + 0.5f * size.z * forwardDir + 0.5f * size.x * rightDir,
                d = xz + 0.5f * size.z * forwardDir - 0.5f * size.x * rightDir
            };

            float halfWidth = size.x / 2;
            Quad2 quad01 = QuadUtils.GetSegmentQuad(vehicleData.m_targetPos0, vehicleData.m_targetPos1, halfWidth);
            Quad2 quad12 = QuadUtils.GetSegmentQuad(vehicleData.m_targetPos1, vehicleData.m_targetPos2, halfWidth);
            Quad2 quad23 = QuadUtils.GetSegmentQuad(vehicleData.m_targetPos2, vehicleData.m_targetPos3, halfWidth);

            Vector2 quadMin = Vector2.Min(Vector2.Min(passingQuad.Min(), quad01.Min()), Vector2.Min(quad12.Min(), quad23.Min()));
            Vector2 quadMax = Vector2.Max(Vector2.Max(passingQuad.Max(), quad01.Max()), Vector2.Max(quad12.Max(), quad23.Max()));
            float yMin = Mathf.Min(Mathf.Min(vehicleData.m_targetPos0.y, vehicleData.m_targetPos1.y),
                Mathf.Min(vehicleData.m_targetPos2.y, vehicleData.m_targetPos3.y));
            float yMax = Mathf.Max(Mathf.Max(vehicleData.m_targetPos0.y, vehicleData.m_targetPos1.y),
                Mathf.Max(vehicleData.m_targetPos2.y, vehicleData.m_targetPos3.y));

            int minGridX = Math.Max((int)((quadMin.x - 72f) / 64f + 135f), 0);
            int minGridZ = Math.Max((int)((quadMin.y - 72f) / 64f + 135f), 0);
            int maxGridX = Math.Min((int)((quadMax.x + 72f) / 64f + 135f), 269);
            int maxGridZ = Math.Min((int)((quadMax.y + 72f) / 64f + 135f), 269);
            float minY = yMin - vehicleInfo.m_generatedInfo.m_negativeHeight - 2f;
            float maxY = yMax + vehicleInfo.m_generatedInfo.m_size.y + 2f;
            BuildingManager buildingManager = Singleton<BuildingManager>.instance;
            for (int gridZ = minGridZ; gridZ <= maxGridZ; gridZ++) {
                for (int gridX = minGridX; gridX <= maxGridX; gridX++) {
                    ushort buildingID = buildingManager.m_buildingGrid[gridZ * 270 + gridX];
                    while (buildingID != 0) {
                        bool passing = buildingManager.m_buildings.m_buffer[buildingID].OverlapQuad(buildingID,
                            passingQuad, minY, maxY, ItemClass.CollisionType.Terrain);
                        bool near1 = buildingManager.m_buildings.m_buffer[buildingID].OverlapQuad(buildingID, quad01, minY, maxY, ItemClass.CollisionType.Terrain) 
                                     || buildingManager.m_buildings.m_buffer[buildingID].OverlapQuad(buildingID, quad12, minY, maxY, ItemClass.CollisionType.Terrain);

                        bool near2 = buildingManager.m_buildings.m_buffer[buildingID].OverlapQuad(buildingID, quad23, minY, maxY, ItemClass.CollisionType.Terrain);

                        float maxSpeedForBridge = HandleMovableBridge(ref vehicleData, buildingID, ref buildingManager.m_buildings.m_buffer[buildingID], passing, near1, near2, minY, maxY, maxSpeed, vehicleTopY, forwardDir);
                        if (maxSpeedForBridge < maxSpeed) {
                            maxSpeed = CalculateMaxSpeed(0f, maxSpeedForBridge, maxBraking);
                        }

                        buildingID = buildingManager.m_buildings.m_buffer[buildingID].m_nextGridBuilding;
                    }
                }
            }
#if DEBUG
            if (InputListener.slowDown) {
                float targetSpeed = Mathf.Min(maxSpeed, 1f);
                maxSpeed = CalculateMaxSpeed(0f, targetSpeed, maxBraking);
            }
#endif
        }


        private static float HandleMovableBridge(ref Vehicle vehicleData, ushort buildingID, ref Building buildingData, bool passing, bool near1, bool near2, float minY, float maxY, float maxSpeed, float vehicleTopY, Vector2 forwardDir) {
            BuildingInfo buildingInfo = buildingData.Info;
            if (!(buildingInfo.m_buildingAI is MovableBridgeAI)) return float.MaxValue;

            MovableBridgeAI movableBridgeAi = (MovableBridgeAI)buildingInfo.m_buildingAI;
            float bridgeClearance = buildingData.m_position.y + movableBridgeAi.m_BridgeClearance;
            if (bridgeClearance > vehicleTopY) {
                return float.MaxValue;
            }

            if (!passing && !near1 && !near2) {
                return float.MaxValue;
            }

            Vector2 buildingForwardDir = new Vector2(Mathf.Sin(buildingData.m_angle), Mathf.Cos(buildingData.m_angle));
            var dot = Vector2.Dot(forwardDir, buildingForwardDir);
            bool left = dot > 0;

            ushort bridgeState = MovableBridgeAI.GetBridgeState(ref buildingData);
            buildingData.m_customBuffer1 |= MovableBridgeAI.FLAG_SHIP_NEAR_BRIDGE;
            if (passing || near1) {
                buildingData.m_customBuffer1 |= (left ? MovableBridgeAI.FLAG_SHIP_PASSING_BRIDGE_LEFT : MovableBridgeAI.FLAG_SHIP_PASSING_BRIDGE_RIGHT);
            }

            if (passing) {
                return float.MaxValue;
            }

            if (bridgeState == MovableBridgeAI.STATE_BRIDGE_OPEN_LEFT ||
                (bridgeState == MovableBridgeAI.STATE_BRIDGE_WAITING_LEFT && near1)) {
                if(left) return float.MaxValue;
            } else if (bridgeState == MovableBridgeAI.STATE_BRIDGE_OPEN_RIGHT 
                       || (bridgeState == MovableBridgeAI.STATE_BRIDGE_WAITING_RIGHT && near1)) {
                if (!left) return float.MaxValue;
            } else if (bridgeState == MovableBridgeAI.STATE_BRIDGE_OPEN_BOTH 
                       || (bridgeState == MovableBridgeAI.STATE_BRIDGE_WAITING_BOTH && near1)) {
                return float.MaxValue;

            }

            if (!near1) {
                return kPassingSpeed;
            }

            vehicleData.m_blockCounter = 0;

            return 0f;
        }

        private static float CalculateMaxSpeed(float targetDistance, float targetSpeed, float maxBraking) {
            float num = 0.5f * maxBraking;
            float num2 = num + targetSpeed;
            return Mathf.Sqrt(Mathf.Max(0f, num2 * num2 + 2f * targetDistance * maxBraking)) - num;
        }
    }
}