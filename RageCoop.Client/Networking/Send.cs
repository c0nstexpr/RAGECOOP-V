﻿using GTA;
using GTA.Math;
using GTA.Native;
using Lidgren.Network;
using RageCoop.Core;
using System;
using System.Collections.Generic;

namespace RageCoop.Client
{
    internal static partial class Networking
    {
        /// <summary>
        /// Reduce GC pressure by reusing frequently used packets
        /// </summary>
        static class SendPackets
        {
            public static Packets.PedSync PedPacket = new Packets.PedSync();
            public static Packets.VehicleSync VehicelPacket = new Packets.VehicleSync();
            public static Packets.ProjectileSync ProjectilePacket = new Packets.ProjectileSync();
        }
        public static int SyncInterval = 30;
        public static List<NetConnection> Targets = new List<NetConnection>();
        public static void SendSync(Packet p, ConnectionChannel channel = ConnectionChannel.Default, NetDeliveryMethod method = NetDeliveryMethod.UnreliableSequenced)
        {
            Peer.SendTo(p, Targets, channel, method);
        }
        
        public static void SendPed(SyncedPed c, bool full)
        {
            if (c.LastSentStopWatch.ElapsedMilliseconds<SyncInterval)
            {
                return;
            }
            Ped p = c.MainPed;
            var packet = SendPackets.PedPacket;
            packet.ID =c.ID;
            packet.OwnerID=c.OwnerID;
            packet.Health = p.Health;
            packet.Rotation = p.ReadRotation();
            packet.Velocity = p.ReadVelocity();
            packet.Speed = p.GetPedSpeed();
            packet.Flags = p.GetPedFlags();
            packet.Heading=p.Heading;
            if (packet.Flags.HasPedFlag(PedDataFlags.IsAiming))
            {
                packet.AimCoords = p.GetAimCoord();
            }
            if (packet.Flags.HasPedFlag(PedDataFlags.IsRagdoll))
            {
                packet.HeadPosition=p.Bones[Bone.SkelHead].Position;
                packet.RightFootPosition=p.Bones[Bone.SkelRightFoot].Position;
                packet.LeftFootPosition=p.Bones[Bone.SkelLeftFoot].Position;
            }
            else
            {
                packet.Position = p.ReadPosition();
            }
            c.LastSentStopWatch.Restart();
            if (full)
            {
                packet.CurrentWeaponHash = packet.Flags.HasPedFlag(PedDataFlags.IsInVehicle) ? (uint)p.VehicleWeapon : (uint)p.Weapons.Current.Hash;
                packet.Flags |= PedDataFlags.IsFullSync;
                packet.Clothes=p.GetPedClothes();
                packet.ModelHash=p.Model.Hash;
                packet.WeaponComponents=p.Weapons.Current.GetWeaponComponents();
                packet.WeaponTint=(byte)Function.Call<int>(Hash.GET_PED_WEAPON_TINT_INDEX, p, p.Weapons.Current.Hash);

                Blip b;
                if (c.IsPlayer)
                {
                    packet.BlipColor=Scripting.API.Config.BlipColor;
                    packet.BlipSprite=Scripting.API.Config.BlipSprite;
                    packet.BlipScale=Scripting.API.Config.BlipScale;
                }
                else if ((b = p.AttachedBlip) !=null)
                {
                    packet.BlipColor=b.Color;
                    packet.BlipSprite=b.Sprite;

                    if (packet.BlipSprite==BlipSprite.PoliceOfficer || packet.BlipSprite==BlipSprite.PoliceOfficer2)
                    {
                        packet.BlipScale=0.5f;
                    }
                }
                else
                {
                    packet.BlipColor=(BlipColor)255;
                }
            }
            SendSync(packet, ConnectionChannel.PedSync);
        }
        public static void SendVehicle(SyncedVehicle v, bool full)
        {
            if (v.LastSentStopWatch.ElapsedMilliseconds<SyncInterval)
            {
                return;
            }
            Vehicle veh = v.MainVehicle;
            var packet = SendPackets.VehicelPacket;
            packet.ID =v.ID;
            packet.OwnerID=v.OwnerID;
            packet.Flags = veh.GetVehicleFlags();
            packet.SteeringAngle = veh.SteeringAngle;
            packet.Position = veh.Position;
            packet.Velocity=veh.Velocity;
            packet.Quaternion=veh.ReadQuaternion();
            packet.RotationVelocity=veh.RotationVelocity;
            packet.ThrottlePower = veh.ThrottlePower;
            packet.BrakePower = veh.BrakePower;
            if (v.LastVelocity==default) {v.LastVelocity=packet.Velocity; }
            packet.Acceleration = (packet.Velocity-v.LastVelocity)*1000/v.LastSentStopWatch.ElapsedMilliseconds;
            v.LastSentStopWatch.Restart();
            v.LastVelocity= packet.Velocity;
            if (packet.Flags.HasVehFlag(VehicleDataFlags.IsDeluxoHovering)) { packet.DeluxoWingRatio=v.MainVehicle.GetDeluxoWingRatio(); }
            if (full)
            {
                byte primaryColor = 0;
                byte secondaryColor = 0;
                unsafe
                {
                    Function.Call<byte>(Hash.GET_VEHICLE_COLOURS, veh, &primaryColor, &secondaryColor);
                }
                packet.Flags |= VehicleDataFlags.IsFullSync;
                packet.Colors = new byte[] { primaryColor, secondaryColor };
                packet.DamageModel=veh.GetVehicleDamageModel();
                packet.LandingGear = veh.IsAircraft ? (byte)veh.LandingGearState : (byte)0;
                packet.RoofState=(byte)veh.RoofState;
                packet.Mods = veh.Mods.GetVehicleMods();
                packet.ModelHash=veh.Model.Hash;
                packet.EngineHealth=veh.EngineHealth;
                packet.Passengers=veh.GetPassengers();
                packet.LockStatus=veh.LockStatus;
                packet.LicensePlate=Function.Call<string>(Hash.GET_VEHICLE_NUMBER_PLATE_TEXT, veh);
                packet.Livery=Function.Call<int>(Hash.GET_VEHICLE_LIVERY, veh);
                if (v.MainVehicle==Game.Player.LastVehicle)
                {
                    packet.RadioStation=Util.GetPlayerRadioIndex();
                }
                if (packet.EngineHealth>v.LastEngineHealth)
                {
                    packet.Flags |= VehicleDataFlags.Repaired;
                }
                v.LastEngineHealth=packet.EngineHealth;
            }
            SendSync(packet, ConnectionChannel.VehicleSync);
        }
        public static void SendProjectile(SyncedProjectile sp)
        {
            var p = sp.MainProjectile;
            var packet = SendPackets.ProjectilePacket;
            packet.ID =sp.ID;
            packet.ShooterID=sp.ShooterID;
            packet.Rotation=p.Rotation;
            packet.Position=p.Position;
            packet.Velocity=p.Velocity;
            packet.WeaponHash=(uint)p.WeaponHash;
            packet.Exploded=p.IsDead;
            if (p.IsDead) { EntityPool.RemoveProjectile(sp.ID, "Dead"); }
            SendSync(packet, ConnectionChannel.ProjectileSync);
        }


        #region SYNC EVENTS
        public static void SendBulletShot(Vector3 start, Vector3 end, uint weapon, int ownerID)
        {
            SendSync(new Packets.BulletShot()
            {
                StartPosition = start,
                EndPosition = end,
                OwnerID = ownerID,
                WeaponHash=weapon,
            }, ConnectionChannel.SyncEvents);
        }
        #endregion
        public static void SendChatMessage(string message)
        {
            Peer.SendTo(new Packets.ChatMessage(new Func<string, byte[]>((s) => Security.Encrypt(s.GetBytes())))
            { Username = Main.Settings.Username, Message = message },ServerConnection, ConnectionChannel.Chat, NetDeliveryMethod.ReliableOrdered);
            Peer.FlushSendQueue();
        }
    }
}
