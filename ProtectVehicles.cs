using Oxide.Core;
using System.Collections.Generic;

namespace Oxide.Plugins;

[Info("Protect Vehicles", "&anhe", "1.1.1")]
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

    private HashSet<ulong> GetPlayerOrTeamIds(BasePlayer player)
    {
        var userIds = new HashSet<ulong>
        {
            player.userID
        };

        if (player.Team is { } team)
            foreach (ulong memberId in team.members)
                userIds.Add(memberId);

        return userIds;
    }

    private bool AnyUsersOnline(HashSet<ulong> authorisedUserIds)
    {
        foreach (BasePlayer activePlayer in BasePlayer.activePlayerList)
            if (authorisedUserIds.Contains(activePlayer.userID))
                return true;

        return false;
    }

    private bool IsEntityUnderAnyUserIdsTCs(BaseEntity vehicle, HashSet<ulong> authorisedUserIds)
    {
        if (vehicle.GetBuildingPrivilege() is { } buildingPrivilege)
            foreach (ulong authorisedPlayer in buildingPrivilege.authorizedPlayers)
                if (authorisedUserIds.Contains(authorisedPlayer))
                    return true;
            
        return false;
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

    private void OnServerSave() {
        CleanUpVehiclesDict();
        SaveData();
    }

    private void LoadData()
    {
        try
        {
            vehiclesDict =
                Interface.Oxide.DataFileSystem
                    .ReadObject<Dictionary<ulong, HashSet<ulong>>>(Name)
                ?? new();
        }
        catch
        {
            vehiclesDict = new();
        }
    }

    private void SaveData() =>
        Interface.Oxide.DataFileSystem
            .WriteObject(Name, vehiclesDict);

    #endregion

    private object IsVehicleAuthorised(BasePlayer player, BaseEntity vehicle)
    {
        if (player.IsAdmin || player.IsNpc)
            return null;

        HashSet<ulong> playerAndTeamIds = GetPlayerOrTeamIds(player);
        ulong vehicleId = vehicle.net.ID.Value;

        if (vehiclesDict.TryGetValue(vehicleId, out var authorisedUserIds))
        {
            foreach (ulong authorisedUserId in authorisedUserIds)
            {
                if (playerAndTeamIds.Contains(authorisedUserId))
                {
                    foreach (ulong playerAndTeamId in playerAndTeamIds)
                        authorisedUserIds.Add(playerAndTeamId);

                    return null;
                }
            }

            if (
                AnyUsersOnline(authorisedUserIds) ||
                IsEntityUnderAnyUserIdsTCs(vehicle, authorisedUserIds)
            )
                return false;
            
            vehiclesDict.Remove(vehicleId);
        }

        vehiclesDict[vehicleId] = playerAndTeamIds;

        return null;
    }

    private void CleanUpVehiclesDict()
    {
        var vehiclesToRemove = new List<ulong>();

        foreach (var vehicleId in vehiclesDict.Keys)
        {
            var entity = BaseNetworkable.serverEntities.Find(new NetworkableId(vehicleId)) as BaseEntity;

            if (entity == null || entity.IsDestroyed)
                vehiclesToRemove.Add(vehicleId);
        }

        foreach (var vehicleId in vehiclesToRemove)
            vehiclesDict.Remove(vehicleId);
    }

    #region Hooks

    private object CanAccess(BasePlayer player, BaseEntity entity)
    {
        var vehicle = GetRootEntity(entity);

        if (generalPurposeVehicles.Contains(vehicle.ShortPrefabName))
            return null;

        return IsVehicleAuthorised(player, vehicle);
    }

    private object CanMove(BasePlayer player, BaseEntity entity)
    {
        if (player.IsBuildingAuthed())
            return null;

        var vehicle = GetRootEntity(entity);

        return IsVehicleAuthorised(player, vehicle);
    }

    #endregion

    #region Aliases

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