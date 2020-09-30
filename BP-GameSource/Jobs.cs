﻿using System;
using System.Linq;
using System.Collections.Generic;
using BrokeProtocol.Collections;
using BrokeProtocol.Utility.Networking;
using BrokeProtocol.Entities;
using BrokeProtocol.Utility;
using BrokeProtocol.Utility.Jobs;
using BrokeProtocol.Utility.AI;
using BrokeProtocol.Required;
using BrokeProtocol.Managers;
using BrokeProtocol.Prefabs;
using ENet;
using UnityEngine;
using Random = UnityEngine.Random;
using BrokeProtocol.API;

namespace BrokeProtocol.GameSource.Jobs
{
    public class Citizen : Job
    {
        public override void Loop()
        {
            if (!player.isHuman && Random.value < 0.005f && player.IsMobile && player.svPlayer.currentState.index == StateIndex.Waypoint)
            {
                player.svPlayer.TryFindInnocent();
            }
        }
    }

    public class Hitman : Job
    {
        public override void Loop()
        {
            if (!player.isHuman && Random.value < 0.01f && player.IsMobile && player.svPlayer.currentState.index == StateIndex.Waypoint)
            {
                player.svPlayer.TryFindVictim();
            }
        }
    }

    public class Prisoner : Job
    {
        public override void ResetJobAI() => player.svPlayer.SetState(StateIndex.Waypoint);
    }

    public class Police : Job
    {
        public override void Loop()
        {
            if (!player.isHuman && !player.svPlayer.targetEntity && player.IsMobile &&
                player.HasItem(player.manager.handcuffs) &&
                Random.value > player.svPlayer.SaturationLevel(WaypointType.Player, 30f))
            {
                player.svPlayer.TryFindCriminal();
            }
        }

        public override void ResetJobAI()
        {
            if (!SetSpawnTarget())
            {
                base.ResetJobAI();
            }
        }
    }

    public class Paramedic : TargetPlayerJob
    {
        public override void Loop()
        {
            if (!player.isHuman)
            {
                if (Random.value < 0.1f && player.IsMobile && !player.svPlayer.targetEntity &&
                    player.HasItem(player.manager.defibrillator))
                {
                    player.svPlayer.TryFindKnockedOut();
                }
            }
            else if (!ValidTarget(target))
            {
                SetTarget();
            }
        }

        protected override void FoundTarget()
        {
            base.FoundTarget();
            player.svPlayer.SendGameMessage(targetPlayer.username + " has been knocked out! Check map");
            targetPlayer.svPlayer.SendGameMessage("Paramedic alerted to your location");
        }

        protected override bool ValidTarget(ShEntity target)
        {
            return base.ValidTarget(target) && (target as ShPlayer).IsKnockedOut;
        }

        public override void OnHealEntity(ShEntity entity)
        {
            player.svPlayer.Reward(2, 100);
        }

        public override void OnRevivePlayer(ShPlayer entity)
        {
            player.svPlayer.Reward(3, 250);
        }
    }

    public class Firefighter : TargetEntityJob
    {
        public override void Loop()
        {
            if (!player.isHuman)
            {
                if (Random.value < 0.1f && player.IsMobile && !player.svPlayer.targetEntity &&
                    player.HasItem(player.manager.extinguisher))
                {
                    player.svPlayer.TryFindFire();
                }
            }
            else if (!ValidTarget(target))
            {
                SetTarget();
            }
        }

        protected override GetEntityCallback GetTargetHandler() => () =>
        {
            if (player.svPlayer.svManager.fires.Count > 0)
                return player.svPlayer.svManager.fires.ToArray().GetRandom();
            else
                return null;
        };

        protected override void FoundTarget()
        {
            base.FoundTarget();
            player.svPlayer.SendGameMessage("Fire reported! Check Map");
        }

        public override void OnDamageEntity(ShEntity hitTarget)
        {
            if (hitTarget.svEntity.spawner != player)
            {
                player.svPlayer.Reward(1, 25);
            }
        }
    }

    public class Gangster : Job
    {
        protected int gangstersKilled;

        public override void Loop()
        {
            if (!player.isHuman && Random.value < 0.01f && player.IsMobile
                && player.svPlayer.currentState.index == StateIndex.Waypoint)
            {
                player.svPlayer.TryFindEnemyGang();
            }
        }

        public override float GetSpawnRate()
        {
            ShTerritory territory = player.svPlayer.GetTerritory;
            if (territory && territory.ownerIndex == info.shared.jobIndex)
            {
                if (territory.attackerIndex != Util.InvalidByte)
                {
                    return 1f;
                }
                else
                {
                    return info.spawnRate;
                }
            }
            return 0f;
        }

        public override void SetJob()
        {
            gangstersKilled = 0;
            foreach (ShTerritory territory in player.manager.svManager.territories.Values)
            {
                territory.svEntity.AddSubscribedPlayer(player);
            }
        }

        public override void RemoveJob()
        {
            foreach (ShTerritory territory in player.manager.svManager.territories.Values)
            {
                territory.svEntity.RemoveSubscribedPlayer(player, true);
            }
        }

        public override void OnDestroyEntity(ShEntity entity)
        {
            base.OnDestroyEntity(entity);

            if (entity is ShPlayer victim)
            {
                if (!player.svPlayer.svManager.gangWar)
                {
                    if (player.isHuman && victim.svPlayer.job is Gangster && this != victim.svPlayer.job)
                    {
                        ShTerritory t;
                        if (gangstersKilled >= 1 && (t = player.svPlayer.GetTerritory) && t.ownerIndex != info.shared.jobIndex)
                        {
                            t.svTerritory.StartGangWar(info.shared.jobIndex);
                            gangstersKilled = 0;
                        }
                        else
                        {
                            gangstersKilled++;
                        }

                        player.svPlayer.Reward(2, 50);
                    }
                }
                else if (victim.svPlayer.job is Gangster)
                {
                    ShTerritory t = player.svPlayer.GetTerritory;
                    if (t && t.attackerIndex != Util.InvalidByte)
                    {
                        if (victim.svPlayer.job.info.shared.jobIndex == t.ownerIndex)
                        {
                            t.svTerritory.defendersKilled++;
                            t.svTerritory.SendTerritoryStats();
                            player.svPlayer.Reward(3, 100);
                        }
                        else if (victim.svPlayer.job.info.shared.jobIndex == t.attackerIndex)
                        {
                            t.svTerritory.attackersKilled++;
                            t.svTerritory.SendTerritoryStats();
                            player.svPlayer.Reward(3, 100);
                        }
                    }
                }
            }
        }

        public override void ResetJobAI()
        {
            ShPlayer target = player.svPlayer.spawner;

            if (target && target.IsOutside && target.svPlayer.job is Gangster &&
                target.svPlayer.job != this && player.DistanceSqr(target) <= Util.visibleRangeSqr)
            {
                ShTerritory territory = target.svPlayer.GetTerritory;
                if (territory && territory.ownerIndex == info.shared.jobIndex && territory.attackerIndex != Util.InvalidByte)
                {
                    player.svPlayer.targetEntity = target;
                    if (player.svPlayer.SetState(StateIndex.Attack))
                    {
                        return;
                    }
                }
            }
            base.ResetJobAI();
        }
    }

    public class Mayor : Job
    {
        private readonly Dictionary<string, string> requests = new Dictionary<string, string>();

        private readonly HashSet<string> requestItems = new HashSet<string>{
            "LicenseGun",
            "LicenseDrivers",
            "LicensePilots",
            "LicenseBoating"
        };

        private const string itemMenu = "items";
        private const string requestMenu = "requests";

        private const string accept = "accept";
        private const string deny = "deny";

        public override void SetJob() => ChatHandler.SendToAll("New Mayor: " + player.username);

        public override void RemoveJob() => ChatHandler.SendToAll("Mayor Left: " + player.username);

        public override void Loop()
        {
            List<string> removeKeys = new List<string>();

            foreach(string name in requests.Keys)
            {
                if (!EntityCollections.Accounts.ContainsKey(name))
                {
                    removeKeys.Add(name);
                }    
            }

            foreach(string s in removeKeys)
            {
                requests.Remove(s);
            }
        }

        public override void OnEmployeeAction(ShPlayer target)
        {
            List<LabelID> options = new List<LabelID>();

            foreach (string s in requestItems)
            {
                ShItem item = SceneManager.Instance.GetEntity<ShItem>(s.GetPrefabIndex());

                if (item)
                {
                    options.Add(new LabelID(s, item.itemName + ": $" + item.value));
                }
            }

            target.svPlayer.SendOptionMenu(itemMenu, target.ID, "Items", options.ToArray(), new LabelID[] { new LabelID("Request", "req") }); 
        }

        public override void OnSelfAction()
        {
            List<LabelID> options = new List<LabelID>();

            foreach (KeyValuePair<string, string> pair in requests)
            {
                options.Add(new LabelID(pair.Key, pair.Key + " : " + pair.Value));
            }

            player.svPlayer.SendOptionMenu(requestMenu, player.ID, "Requests", options.ToArray(), new LabelID[] { new LabelID("Accept", accept), new LabelID("Deny", deny) });
        }


        public override void OnOptionMenuAction(int targetID, string menuID, string optionID, string actionID)
        {
            switch(menuID)
            {
                case itemMenu:
                    RequestAdd(targetID, optionID); // actionID can only be 'req' (request)
                    break;
                case requestMenu:
                    ResultHandle(optionID, actionID); // targetID can only be self here
                    break;
                default:
                    break;
            }

        }

        public void RequestAdd(int targetID, string itemName)
        {
            if (!requestItems.Contains(itemName))
            {
                Debug.LogError("[SVR] Item not valid: " + itemName);
                return;
            }

            ShItem item = SceneManager.Instance.GetEntity<ShItem>(itemName);
            if (!item)
            {
                Debug.LogError("[SVR] Item not found: " + itemName);
                return;
            }

            ShPlayer requester = EntityCollections.FindByID<ShPlayer>(targetID);
            if(!requester)
            {
                Debug.LogError("[SVR] Requester not found");
                return;
            }

            if(requester.HasItem(item.index))
            {
                requester.svPlayer.SendGameMessage("Already own item");
            }
            else if (player.MyMoneyCount < item.value)
            {
                requester.svPlayer.SendGameMessage("Not enough money");
            }
            else if (requests.ContainsKey(requester.username))
            {
                requester.svPlayer.SendGameMessage("Previous request still pending");
            }
            else
            {
                requests[requester.username] = itemName;
                requester.svPlayer.SendGameMessage("Request successfully sent");
                player.svPlayer.SendGameMessage(requester.username + " requesting a " + item.itemName);
            }
        }

        public void ResultHandle(string requesterName, string result)
        {
            if (!requests.TryGetValue(requesterName, out string itemName))
            {
                Debug.LogError("[SVR] Requester invalid: " + requesterName);
                return;
            }

            if (EntityCollections.Accounts.TryGetValue(requesterName, out ShPlayer requester))
            {
                if (result == accept)
                {
                    ShItem item = SceneManager.Instance.GetEntity<ShItem>(itemName);
                    if (item)
                    {
                        if (requester.MyMoneyCount >= item.value)
                        {
                            requester.TransferMoney(DeltaInv.RemoveFromMe, item.value, true);
                            requester.TransferItem(DeltaInv.AddToMe, item);
                            player.TransferMoney(DeltaInv.AddToMe, item.value, true);
                        }
                        else
                        {
                            player.svPlayer.SendGameMessage("Player missing funds");
                            requester.svPlayer.SendGameMessage("No funds for license");
                        }
                    }
                }
                else if (result == deny)
                {
                    requester.svPlayer.SendGameMessage("License Denied");
                }
            }

            requests.Remove(requesterName);
            OnSelfAction();
        }
    }

    public abstract class TargetEntityJob : Job
    {
        [NonSerialized]
        public ShEntity target;

        protected delegate ShEntity GetEntityCallback();

        protected virtual bool ValidTarget(ShEntity target) => target && target != player && target.isActiveAndEnabled && !target.IsDead;

        public virtual void ResetTarget()
        {
            player.svPlayer.DestroyGoalMarker();
            target = null;
        }

        protected virtual void FoundTarget() => player.svPlayer.StartGoalMarker(target);

        protected abstract GetEntityCallback GetTargetHandler();

        protected void SetTarget()
        {
            ResetTarget();

            GetEntityCallback handler = GetTargetHandler();

            for (int i = 0; i < 10; i++)
            {
                ShEntity e = handler();

                if (ValidTarget(e))
                {
                    Debug.Log("Found target");
                    target = e;
                    FoundTarget();
                    return;
                }
            }
        }

        public override void RemoveJob() => ResetTarget();
    }

    public abstract class TargetPlayerJob : TargetEntityJob
    {
        [NonSerialized]
        public ShPlayer targetPlayer;

        protected override void FoundTarget()
        {
            base.FoundTarget();
            targetPlayer = target as ShPlayer;
        }

        protected override bool ValidTarget(ShEntity target)
        {
            return base.ValidTarget(target) && (target as ShPlayer).wantedLevel >= info.attackLevel;
        }

        protected override GetEntityCallback GetTargetHandler()
        {
            if (EntityCollections.Humans.Count >= 3)
            {
                return () => player.svPlayer.svManager.RandomHuman;
            }
            else
            {
                return () => player.svPlayer.svManager.RandomAIPlayer;
            }
        }

        public override void ResetTarget()
        {
            targetPlayer = null;
            base.ResetTarget();
        }
    }

    public class SpecOps : TargetPlayerJob
    {
        protected override void FoundTarget()
        {
            base.FoundTarget();
            player.svPlayer.SendGameMessage("Assassination target: " + targetPlayer.username);
            targetPlayer.svPlayer.SendGameMessage("SpecOps dispatched!");
        }

        public override void Loop()
        {
            if (!player.isHuman)
            {
                if (!player.svPlayer.targetEntity && player.IsMobile &&
                    Random.value > player.svPlayer.SaturationLevel(WaypointType.Player, 30f))
                {
                    player.svPlayer.TryFindCriminal();
                }
            }
            else if (!ValidTarget(targetPlayer))
            {
                SetTarget();
            }
        }

        public override void OnDestroyEntity(ShEntity entity)
        {
            base.OnDestroyEntity(entity);
            if (entity is ShPlayer victim && targetPlayer == victim && victim.wantedLevel > 0 && victim.wantedLevel >= info.attackLevel)
            {
                player.svPlayer.Reward(3, 300);
            }
        }

        public override void ResetJobAI()
        {
            if (!SetSpawnTarget())
            {
                base.ResetJobAI();
            }
        }
    }

    public class DeliveryMan : TargetPlayerJob
    {
        [NonSerialized]
        public ShConsumable deliveryItem;

        [NonSerialized]
        public float timeDeadline;

        protected override bool ValidTarget(ShEntity target) => base.ValidTarget(target) && (target is ShPlayer p) && !p.curMount && !(p.svPlayer.job is Prisoner);

        public override void ResetTarget()
        {
            if (deliveryItem)
            {
                if (player.HasItem(deliveryItem))
                {
                    player.TransferItem(DeltaInv.RemoveFromMe, deliveryItem);
                }
                deliveryItem = null;
                player.svPlayer.Send(SvSendType.Self, Channel.Reliable, ClPacket.DestroyTimer);
            }

            base.ResetTarget();
        }

        protected override void FoundTarget()
        {
            base.FoundTarget();
            player.svPlayer.SendGameMessage("Delivery target: " + targetPlayer.username);
            deliveryItem = SceneManager.Instance.consumablesCollection.GetRandom();
            player.TransferItem(DeltaInv.AddToMe, deliveryItem);
            timeDeadline = Time.time + (player.Distance(targetPlayer) * 0.1f) + 20f;
            player.svPlayer.Send(SvSendType.Self, Channel.Reliable, ClPacket.ShowTimer, timeDeadline - Time.time);
        }

        public override void Loop()
        {
            if (player.isHuman)
            {
                if (!ValidTarget(targetPlayer))
                {
                    SetTarget();
                }
                else if (deliveryItem && !player.HasItem(deliveryItem))
                {
                    player.svPlayer.SendGameMessage("Lost Delivery Item");
                    SetTarget();
                }
                else if (targetPlayer && Time.time > timeDeadline)
                {
                    player.svPlayer.SendGameMessage("Out of Time");
                    SetTarget();
                }
            }
        }

        public override void OnSpecialAction(ShPlayer target)
        {
            if (!target || target.IsDead || !(player.svPlayer.job.info.shared.specialAction) || !player.InActionRange(target))
            {
                return;
            }

            if (targetPlayer != target)
            {
                player.svPlayer.SendGameMessage("Wrong target");
                return;
            }

            player.svPlayer.Reward(2, Mathf.CeilToInt(timeDeadline - Time.time));

            target.TransferItem(DeltaInv.AddToMe, deliveryItem);

            player.svPlayer.SendGameMessage("Successfully Delivered " + deliveryItem.itemName + " to " + targetPlayer.username);

            targetPlayer.svPlayer.SendGameMessage("Received delivery from " + player.username);

            ResetTarget();
        }
    }

    public class TaxiDriver : TargetPlayerJob
    {
        [NonSerialized]
        public ShEntity destinationMarker;

        [NonSerialized]
        public float timeDeadline;

        private bool ValidPlayer(ShPlayer player) => player.IsMobile && !(player.svPlayer.job is Prisoner) && player.IsOutside;

        override protected bool ValidTarget(ShEntity target)
        {
            if (target is ShPlayer p)
            {
                if (destinationMarker)
                {
                    return base.ValidTarget(target) && ValidPlayer(p) && p.curMount && p.curMount == player.curMount;
                }
                else
                {
                    return base.ValidTarget(target) && ValidPlayer(p) && !p.curMount;
                }
            }
            return false;
        }

        override public void ResetTarget()
        {
            if (targetPlayer)
            {
                targetPlayer.svPlayer.SvDismount();
                targetPlayer.svPlayer.ResetAI();
            }

            if (destinationMarker)
            {
                destinationMarker.Destroy();
                destinationMarker = null;

                player.svPlayer.Send(SvSendType.Self, Channel.Reliable, ClPacket.DestroyTimer);
            }

            base.ResetTarget();
        }

        protected override GetEntityCallback GetTargetHandler() => () => player.svPlayer.svManager.RandomAIPlayer;

        public override void Loop()
        {
            if (player.isHuman)
            {
                if (!player.IsDriving)
                {
                    ResetTarget();
                }
                else if (!ValidTarget(targetPlayer))
                {
                    SetTarget();
                }
                else if (destinationMarker)
                {
                    if (Time.time > timeDeadline)
                    {
                        player.svPlayer.SendGameMessage("Out of Time");
                        SetTarget();
                    }
                    else if (player.TransportWithinReach(destinationMarker))
                    {
                        player.svPlayer.Reward(2, Mathf.CeilToInt(timeDeadline - Time.time));
                        SetTarget();
                    }
                }
                else if (
                    targetPlayer && player.TransportWithinReach(targetPlayer) &&
                    targetPlayer.svPlayer.SvMountBack(player.curMount.ID, false))
                {
                    player.svPlayer.DestroyGoalMarker();

                    SpawnLocation destination = player.manager.svManager.spawnLocations.GetRandom();

                    destinationMarker = player.manager.svManager.AddNewEntity(
                        player.manager.svManager.markerGoalPrefab,
                        SceneManager.Instance.ExteriorPlace,
                        destination.transform.position,
                        Quaternion.identity,
                        new IDCollection<ShPlayer> { player });

                    player.svPlayer.SendGameMessage("Destination: " + destination.locationName);

                    timeDeadline = Time.time + player.Distance(destination.transform.position) * 0.1f + 20f;

                    player.svPlayer.Send(SvSendType.Self, Channel.Reliable, ClPacket.ShowTimer, timeDeadline - Time.time);
                }
            }
        }

        protected override void FoundTarget()
        {
            base.FoundTarget();
            player.svPlayer.SendGameMessage("Pickup target: " + targetPlayer.username);
        }
    }
}