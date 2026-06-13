using Oxide.Core;
using System.Collections.Generic;

namespace Oxide.Plugins;

[Info("Protect Vehicles", "&anhe", "1.0.4")]
[Description("Protects vehicles from other players.")]
public class ProtectVehicles : RustPlugin
{
    #region Boilerplate

    private HashSet<string> generalPurposeVehicles =
        new HashSet<string>
    {
        "hotairballoon",

        "mlrs.entity",
        "magnetcrane.entity",

        "workcart.entity",
        "trainwagona.entity",
        "locomotive.entity",
        "traincaboose.entity"
    };

    #endregion

    #region Helpers

    private BaseEntity GetRootEntity(BaseEntity entity)
    {
        BaseEntity parent;
        while ((parent = entity.parentEntity.Get(true)) != null)
            entity = parent;

        return entity;
    }

    #endregion

    #region Data

    private class VehicleData
    {
        public ulong userId;
        public ulong teamId;
    }

    private Dictionary<ulong, HashSet<ulong>> vehiclesDict = new();

    private void Init() =>
        LoadData();

    private void Unload() =>
        SaveData();

    private void OnServerSave() =>
        SaveData();

    private void LoadData()
    {
        try
        {
            vehiclesDict =
                Interface.Oxide.DataFileSystem
                    .ReadObject<Dictionary<ulong, HashSet<ulong>>>(Name);

            vehiclesDict ??= new();
        }
        catch
        {
            vehiclesDict = new();

            // Migrate old data format: vehicleId -> { userId, teamId }
            try
            {
                var oldVehiclesDict =
                    Interface.Oxide.DataFileSystem
                        .ReadObject<Dictionary<ulong, VehicleData>>(Name);

                if (oldVehiclesDict == null)
                    return;

                foreach (var entry in oldVehiclesDict)
                {
                    var userIds = new HashSet<ulong>();

                    if (entry.Value.userId != 0)
                        userIds.Add(entry.Value.userId);

                    var team = RelationshipManager.ServerInstance.FindTeam(entry.Value.teamId);
                    if (team != null)
                        foreach (ulong memberId in team.members)
                            userIds.Add(memberId);

                    vehiclesDict[entry.Key] = userIds;
                }

                SaveData();
            }
            catch { }
        }
    }

    private void SaveData() =>
        Interface.Oxide.DataFileSystem
            .WriteObject(Name, vehiclesDict);

    #endregion

    // Auth

    private HashSet<ulong> GetPlayerAndTeamIds(BasePlayer player)
    {
        var userIds = new HashSet<ulong>
        {
            player.userID
        };

        var team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
        if (team != null)
            foreach (ulong memberId in team.members)
                userIds.Add(memberId);

        return userIds;
    }

    private object getAuthorisation(BasePlayer player, ulong vehicleId)
    {
        ulong userId = player.userID;

        if (
            // Admin
            player.IsAdmin ||
            // NPCs
            userId < 76561197960265728
        )
            return null;

        HashSet<ulong> playerAndTeamIds = GetPlayerAndTeamIds(player);

        if (vehiclesDict.TryGetValue(vehicleId, out var authorisedUserIds))
        {
            bool allowed = false;

            foreach (ulong authorisedUserId in authorisedUserIds)
            {
                if (playerAndTeamIds.Contains(authorisedUserId))
                {
                    allowed = true;
                    break;
                }
            }

            if (!allowed)
                return false;

            // Refresh authorised users with yourself and your current team.
            foreach (ulong playerAndTeamId in playerAndTeamIds)
                authorisedUserIds.Add(playerAndTeamId);

            return null;
        }

        vehiclesDict[vehicleId] = playerAndTeamIds;

        return null;
    }

    #region Aliases

    private object CanAccess(BasePlayer player, BaseEntity entity)
    {
        var vehicle = GetRootEntity(entity);

        if (generalPurposeVehicles.Contains(vehicle.ShortPrefabName))
            return null;
        
        ulong vehicleId = vehicle.net.ID.Value;

        return getAuthorisation(player, vehicleId);
    }

    private object CanMove(BasePlayer player, BaseEntity entity)
    {
        if (player.GetBuildingPrivilege()?.IsAuthed(player) == true)
            return null;

        ulong vehicleId = GetRootEntity(entity).net.ID.Value;

        return getAuthorisation(player, vehicleId);
    }

    // Mount

    private object CanMountEntity(BasePlayer player, BaseMountable entity) =>
        entity.PrefabName.Contains("vehicle")
            ? CanAccess(player, entity) : null;

    // Loot

    private object CanLootEntity(BasePlayer rider, RidableHorse horse) =>
        CanAccess(rider, horse);

    private object CanLootEntity(BasePlayer player, BaseEntity entity) =>
        entity.PrefabName.StartsWith("assets/content/vehicles/")
            ? CanAccess(player, entity) : null;

    // Move

    private object OnHorseLead(RidableHorse horse, BasePlayer player) =>
        CanMove(player, horse);

    private object OnVehiclePush(BaseVehicle vehicle, BasePlayer player) =>
        CanMove(player, vehicle);
    
    // Damage

    private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info) =>
        (
            // Vehicle
            entity.PrefabName.StartsWith("assets/content/vehicles/") &&
            // Damaged by player
            info.InitiatorPlayer is BasePlayer player
        )
            ? CanAccess(player, entity) : null;

    #endregion
}