﻿using System;
using System.Collections.Generic;
using System.Linq;

using GTA;

namespace CoopClient.Entities
{
    /// <summary>
    /// Don't use it!
    /// </summary>
    public class EntitiesThread : Script
    {
        /// <summary>
        /// Don't use it!
        /// </summary>
        public EntitiesThread()
        {
            // Required for some synchronization!
            if (Game.Version < GameVersion.v1_0_1290_1_Steam)
            {
                return;
            }

            Tick += OnTick;
            Interval = Util.GetGameMs<int>();
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (Game.IsLoading || !Main.MainNetworking.IsOnServer() || !Main.NpcsAllowed)
            {
                return;
            }

            Dictionary<long, EntitiesNpc> localNPCs = null;
            lock (Main.NPCs)
            {
                localNPCs = new Dictionary<long, EntitiesNpc>(Main.NPCs);

                ulong tickCount = Util.GetTickCount64();
                foreach (KeyValuePair<long, EntitiesNpc> npc in new Dictionary<long, EntitiesNpc>(localNPCs))
                {
                    if ((tickCount - npc.Value.LastUpdateReceived) > 3000)
                    {
                        if (npc.Value.Character != null && npc.Value.Character.Exists() && !npc.Value.Character.IsDead)
                        {
                            npc.Value.Character.Kill();
                            npc.Value.Character.MarkAsNoLongerNeeded();
                            npc.Value.Character.Delete();
                        }

                        if (npc.Value.MainVehicle != null && npc.Value.MainVehicle.Exists() && !npc.Value.MainVehicle.IsDead && npc.Value.MainVehicle.IsSeatFree(VehicleSeat.Driver) && npc.Value.MainVehicle.PassengerCount == 0)
                        {
                            npc.Value.MainVehicle.MarkAsNoLongerNeeded();
                            npc.Value.MainVehicle.Delete();
                        }

                        localNPCs.Remove(npc.Key);
                        Main.NPCs.Remove(npc.Key);
                    }
                }
            }

            foreach (EntitiesNpc npc in localNPCs.Values)
            {
                npc.DisplayLocally(null);
            }

            // Only if that player wants to share his NPCs with others
            if (Main.ShareNpcsWithPlayers)
            {
                // Send all npcs from the current player
                foreach (Ped ped in World.GetNearbyPeds(Game.Player.Character.Position, 150f)
                    .Where(p => p.Handle != Game.Player.Character.Handle && p.RelationshipGroup != Main.RelationshipGroup)
                    .OrderBy(p => (p.Position - Game.Player.Character.Position).Length()))
                {
                    Main.MainNetworking.SendNpcData(ped);
                }
            }
        }
    }
}
